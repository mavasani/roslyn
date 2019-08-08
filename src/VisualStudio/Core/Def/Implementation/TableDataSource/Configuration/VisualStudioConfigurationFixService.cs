// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeFixes.Configuration;
using Microsoft.CodeAnalysis.CodeFixes.Suppression;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.Editor.Implementation.Suggestions;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem;
using Microsoft.VisualStudio.LanguageServices.Implementation.TaskList;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Roslyn.Utilities;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource.Configuration
{
    /// <summary>
    /// Service to compute and apply bulk configuration/suppression fixes.
    /// </summary>
    [Export(typeof(IVisualStudioConfigurationFixService))]
    internal sealed partial class VisualStudioConfigurationFixService : IVisualStudioConfigurationFixService
    {
        private readonly VisualStudioWorkspaceImpl _workspace;
        private readonly IWpfTableControl _tableControl;
        private readonly IDiagnosticAnalyzerService _diagnosticService;
        private readonly ExternalErrorDiagnosticUpdateSource _buildErrorDiagnosticService;
        private readonly ICodeFixService _codeFixService;
        private readonly IFixMultipleOccurrencesService _fixMultipleOccurencesService;
        private readonly ICodeActionEditHandlerService _editHandlerService;
        private readonly VisualStudioDiagnosticListConfigurationStateService _configurationStateService;
        private readonly IWaitIndicator _waitIndicator;
        private readonly IVsHierarchyItemManager _vsHierarchyItemManager;
        private readonly IHierarchyItemToProjectIdMap _projectMap;

        [ImportingConstructor]
        public VisualStudioConfigurationFixService(
            SVsServiceProvider serviceProvider,
            VisualStudioWorkspaceImpl workspace,
            IDiagnosticAnalyzerService diagnosticService,
            ICodeFixService codeFixService,
            ICodeActionEditHandlerService editHandlerService,
            IVisualStudioDiagnosticListConfigurationStateService configurationStateService,
            IWaitIndicator waitIndicator,
            IVsHierarchyItemManager vsHierarchyItemManager)
        {
            _workspace = workspace;
            _diagnosticService = diagnosticService;
            _buildErrorDiagnosticService = workspace.ExternalErrorDiagnosticUpdateSource;
            _codeFixService = codeFixService;
            _configurationStateService = (VisualStudioDiagnosticListConfigurationStateService)configurationStateService;
            _editHandlerService = editHandlerService;
            _waitIndicator = waitIndicator;
            _vsHierarchyItemManager = vsHierarchyItemManager;
            _fixMultipleOccurencesService = workspace.Services.GetService<IFixMultipleOccurrencesService>();
            _projectMap = workspace.Services.GetService<IHierarchyItemToProjectIdMap>();

            var errorList = serviceProvider.GetService(typeof(SVsErrorList)) as IErrorList;
            _tableControl = errorList?.TableControl;
        }

        public bool AddSuppressions(IVsHierarchy projectHierarchyOpt)
        {
            if (_tableControl == null)
            {
                return false;
            }

            var shouldFixInProject = GetShouldFixInProjectDelegate(_vsHierarchyItemManager, _projectMap, projectHierarchyOpt);

            // Apply suppressions fix in global suppressions file for non-compiler diagnostics and
            // in source only for compiler diagnostics.
            var diagnosticsToFix = GetDiagnosticsToFix(shouldFixInProject, selectedEntriesOnly: false, FixKind.AddSuppression);
            if (!ApplySuppressionFix(diagnosticsToFix, shouldFixInProject, filterStaleDiagnostics: false, FixKind.AddSuppression, isSuppressionInSource: false, onlyCompilerDiagnostics: false, showPreviewChangesDialog: false))
            {
                return false;
            }

            return ApplySuppressionFix(diagnosticsToFix, shouldFixInProject, filterStaleDiagnostics: false, FixKind.AddSuppression, isSuppressionInSource: true, onlyCompilerDiagnostics: true, showPreviewChangesDialog: false);
        }

        public bool AddSuppressions(bool selectedErrorListEntriesOnly, bool suppressInSource, IVsHierarchy projectHierarchyOpt)
        {
            if (_tableControl == null)
            {
                return false;
            }

            var shouldFixInProject = GetShouldFixInProjectDelegate(_vsHierarchyItemManager, _projectMap, projectHierarchyOpt);
            return ApplySuppressionFix(shouldFixInProject, selectedErrorListEntriesOnly, FixKind.AddSuppression, isSuppressionInSource: suppressInSource, onlyCompilerDiagnostics: false, showPreviewChangesDialog: true);
        }

        public bool RemoveSuppressions(bool selectedErrorListEntriesOnly, IVsHierarchy projectHierarchyOpt)
        {
            if (_tableControl == null)
            {
                return false;
            }

            var shouldFixInProject = GetShouldFixInProjectDelegate(_vsHierarchyItemManager, _projectMap, projectHierarchyOpt);
            return ApplySuppressionFix(shouldFixInProject, selectedErrorListEntriesOnly, FixKind.RemoveSuppression, isSuppressionInSource: false, onlyCompilerDiagnostics: false, showPreviewChangesDialog: true);
        }

        public bool ConfigureSeverity(ReportDiagnostic severity)
        {
            if (_tableControl == null)
            {
                return false;
            }

            var diagnosticsToFix = GetDiagnosticsToFix(shouldFixInProject: _ => true, selectedEntriesOnly: true, FixKind.ConfigureSeverity);
            if (diagnosticsToFix.IsEmpty)
            {
                return false;
            }

            ConfigurationUpdater.ConfigureSeverityAsync(severity, diagnosticsToFix, solution )

        }

        private static Func<Project, bool> GetShouldFixInProjectDelegate(IVsHierarchyItemManager vsHierarchyItemManager, IHierarchyItemToProjectIdMap projectMap, IVsHierarchy projectHierarchyOpt)
        {
            ProjectId projectIdToMatch = null;
            if (projectHierarchyOpt != null)
            {
                var projectHierarchyItem = vsHierarchyItemManager.GetHierarchyItem(projectHierarchyOpt, VSConstants.VSITEMID_ROOT);
                if (projectMap.TryGetProjectId(projectHierarchyItem, targetFrameworkMoniker: null, out var projectId))
                {
                    projectIdToMatch = projectId;
                }
            }

            return p => projectHierarchyOpt == null || p.Id == projectIdToMatch;
        }

        private async Task<ImmutableArray<DiagnosticData>> GetAllBuildDiagnosticsAsync(Func<Project, bool> shouldFixInProject, CancellationToken cancellationToken)
        {
            var builder = ArrayBuilder<DiagnosticData>.GetInstance();

            var buildDiagnostics = _buildErrorDiagnosticService.GetBuildErrors().Where(d => d.ProjectId != null && d.Severity != DiagnosticSeverity.Hidden);
            var solution = _workspace.CurrentSolution;
            foreach (var diagnosticsByProject in buildDiagnostics.GroupBy(d => d.ProjectId))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (diagnosticsByProject.Key == null)
                {
                    // Diagnostics with no projectId cannot be suppressed.
                    continue;
                }

                var project = solution.GetProject(diagnosticsByProject.Key);
                if (project != null && shouldFixInProject(project))
                {
                    var diagnosticsByDocument = diagnosticsByProject.GroupBy(d => d.DocumentId);
                    foreach (var group in diagnosticsByDocument)
                    {
                        var documentId = group.Key;
                        if (documentId == null)
                        {
                            // Project diagnostics, just add all of them.
                            builder.AddRange(group);
                            continue;
                        }

                        // For document diagnostics, build does not have the computed text span info.
                        // So we explicitly calculate the text span from the source text for the diagnostics.
                        var document = project.GetDocument(documentId);
                        if (document != null)
                        {
                            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                            var text = await tree.GetTextAsync(cancellationToken).ConfigureAwait(false);
                            foreach (var diagnostic in group)
                            {
                                builder.Add(diagnostic.WithCalculatedSpan(text));
                            }
                        }
                    }
                }
            }

            return builder.ToImmutableAndFree();
        }

        private static string GetFixTitle(FixKind fixKind)
        {
            return fixKind switch
            {
                FixKind.AddSuppression => ServicesVSResources.Suppress_diagnostics,
                FixKind.RemoveSuppression => ServicesVSResources.Remove_suppressions,
                _ => throw ExceptionUtilities.Unreachable,
            };
        }

        private ImmutableHashSet<DiagnosticData> GetDiagnosticsToFix(Func<Project, bool> shouldFixInProject, bool selectedEntriesOnly, FixKind fixKind)
        {
            var diagnosticsToFix = ImmutableHashSet<DiagnosticData>.Empty;
            void computeDiagnosticsToFix(IWaitContext context)
            {
                var cancellationToken = context.CancellationToken;

                // If we are fixing selected diagnostics in error list, then get the diagnostics from error list entry snapshots.
                // Otherwise, get all diagnostics from the diagnostic service.
                ImmutableArray<DiagnosticData> computedDiagnostics;
                if (selectedEntriesOnly)
                {
                    // Include build diagnostics only for Configure Severity fix. We do not support adding or removing suppressions for build diagnostics as they might be stale.
                    var includeBuildDiagnostics = fixKind == FixKind.ConfigureSeverity;

                    computedDiagnostics = _configurationStateService.GetSelectedConfigurableItems(includeBuildDiagnostics, cancellationToken);
                }
                else
                {
                    computedDiagnostics = GetAllBuildDiagnosticsAsync(shouldFixInProject, cancellationToken).WaitAndGetResult(cancellationToken);
                }

                diagnosticsToFix = computedDiagnostics.ToImmutableHashSet();
            }

            var title = GetFixTitle(fixKind);
            var result = InvokeWithWaitDialog(computeDiagnosticsToFix, title, ServicesVSResources.Computing_diagnostics);

            // Bail out if the user cancelled.
            if (result == WaitIndicatorResult.Canceled)
            {
                return null;
            }

            return diagnosticsToFix;
        }

        private bool ApplySuppressionFix(Func<Project, bool> shouldFixInProject, bool selectedEntriesOnly, FixKind fixKind, bool isSuppressionInSource, bool onlyCompilerDiagnostics, bool showPreviewChangesDialog)
        {
            Debug.Assert(fixKind == FixKind.AddSuppression || fixKind == FixKind.RemoveSuppression);

            var diagnosticsToFix = GetDiagnosticsToFix(shouldFixInProject, selectedEntriesOnly, fixKind);
            return ApplySuppressionFix(diagnosticsToFix, shouldFixInProject, selectedEntriesOnly, fixKind, isSuppressionInSource, onlyCompilerDiagnostics, showPreviewChangesDialog);
        }

        private bool ApplySuppressionFix(IEnumerable<DiagnosticData> diagnosticsToFix, Func<Project, bool> shouldFixInProject, bool filterStaleDiagnostics, FixKind fixKind, bool isSuppressionInSource, bool onlyCompilerDiagnostics, bool showPreviewChangesDialog)
        {
            Debug.Assert(fixKind == FixKind.AddSuppression || fixKind == FixKind.RemoveSuppression);

            if (diagnosticsToFix == null)
            {
                return false;
            }

            diagnosticsToFix = FilterDiagnostics(diagnosticsToFix, fixKind, isSuppressionInSource, onlyCompilerDiagnostics);
            if (diagnosticsToFix.IsEmpty())
            {
                // Nothing to fix.
                return true;
            }

            ImmutableDictionary<Document, ImmutableArray<Diagnostic>> documentDiagnosticsToFixMap = null;
            ImmutableDictionary<Project, ImmutableArray<Diagnostic>> projectDiagnosticsToFixMap = null;

            var title = GetFixTitle(fixKind);
            var noDiagnosticsToFix = false;
            var cancelled = false;
            var newSolution = _workspace.CurrentSolution;
            HashSet<string> languages = null;

            void computeDiagnosticsAndFix(IWaitContext context)
            {
                var cancellationToken = context.CancellationToken;
                cancellationToken.ThrowIfCancellationRequested();

                documentDiagnosticsToFixMap = GetDocumentDiagnosticsToFixAsync(diagnosticsToFix, shouldFixInProject, filterStaleDiagnostics: filterStaleDiagnostics, cancellationToken: cancellationToken)
                    .WaitAndGetResult(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                projectDiagnosticsToFixMap = isSuppressionInSource ?
                    ImmutableDictionary<Project, ImmutableArray<Diagnostic>>.Empty :
                    GetProjectDiagnosticsToFixAsync(diagnosticsToFix, shouldFixInProject, filterStaleDiagnostics: filterStaleDiagnostics, cancellationToken: cancellationToken)
                        .WaitAndGetResult(cancellationToken);

                if (documentDiagnosticsToFixMap == null ||
                    projectDiagnosticsToFixMap == null ||
                    (documentDiagnosticsToFixMap.IsEmpty && projectDiagnosticsToFixMap.IsEmpty))
                {
                    // Nothing to fix.
                    noDiagnosticsToFix = true;
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Equivalence key determines what fix will be applied.
                // Make sure we don't include any specific diagnostic ID, as we want all of the given diagnostics (which can have varied ID) to be fixed.
                var equivalenceKey = fixKind == FixKind.AddSuppression ?
                    (isSuppressionInSource ? FeaturesResources.in_Source : FeaturesResources.in_Suppression_File) :
                    FeaturesResources.Remove_Suppression;

                // We have different suppression fixers for every language.
                // So we need to group diagnostics by the containing project language and apply fixes separately.
                languages = new HashSet<string>(projectDiagnosticsToFixMap.Select(p => p.Key.Language).Concat(documentDiagnosticsToFixMap.Select(kvp => kvp.Key.Project.Language)));

                foreach (var language in languages)
                {
                    // Use the Fix multiple occurrences service to compute a bulk suppression fix for the specified document and project diagnostics,
                    // show a preview changes dialog and then apply the fix to the workspace.

                    cancellationToken.ThrowIfCancellationRequested();

                    var documentDiagnosticsPerLanguage = GetDocumentDiagnosticsMappedToNewSolution(documentDiagnosticsToFixMap, newSolution, language);
                    if (!documentDiagnosticsPerLanguage.IsEmpty)
                    {
                        var suppressionFixer = GetSuppressionFixer(documentDiagnosticsPerLanguage.SelectMany(kvp => kvp.Value), language, _codeFixService);
                        if (suppressionFixer != null)
                        {
                            var suppressionFixAllProvider = suppressionFixer.GetFixAllProvider();
                            newSolution = _fixMultipleOccurencesService.GetFix(
                                documentDiagnosticsPerLanguage,
                                _workspace,
                                suppressionFixer,
                                suppressionFixAllProvider,
                                equivalenceKey,
                                title,
                                ServicesVSResources.Computing_fix,
                                cancellationToken);
                            if (newSolution == null)
                            {
                                // User cancelled or fixer threw an exception, so we just bail out.
                                cancelled = true;
                                return;
                            }
                        }
                    }

                    var projectDiagnosticsPerLanguage = GetProjectDiagnosticsMappedToNewSolution(projectDiagnosticsToFixMap, newSolution, language);
                    if (!projectDiagnosticsPerLanguage.IsEmpty)
                    {
                        var suppressionFixer = GetSuppressionFixer(projectDiagnosticsPerLanguage.SelectMany(kvp => kvp.Value), language, _codeFixService);
                        if (suppressionFixer != null)
                        {
                            var suppressionFixAllProvider = suppressionFixer.GetFixAllProvider();
                            newSolution = _fixMultipleOccurencesService.GetFix(
                                 projectDiagnosticsPerLanguage,
                                 _workspace,
                                 suppressionFixer,
                                 suppressionFixAllProvider,
                                 equivalenceKey,
                                 title,
                                 ServicesVSResources.Computing_fix,
                                 cancellationToken);
                            if (newSolution == null)
                            {
                                // User cancelled or fixer threw an exception, so we just bail out.
                                cancelled = true;
                                return;
                            }
                        }
                    }
                }
            }

            var result = InvokeWithWaitDialog(computeDiagnosticsAndFix, title, ServicesVSResources.Computing_fix);

            // Bail out if the user cancelled.
            if (cancelled || result == WaitIndicatorResult.Canceled)
            {
                return false;
            }
            else if (noDiagnosticsToFix || newSolution == _workspace.CurrentSolution)
            {
                // No changes.
                return true;
            }

            var languageOpt = languages?.Count == 1 ? languages.Single() : null;
            return ApplyFixCore(newSolution, diagnosticsToFix, title, languageOpt, showPreviewChangesDialog);
        }

        private bool ApplyFixCore(
            Solution newSolution,
            IEnumerable<DiagnosticData> fixedDiagnostics,
            string title,
            string languageOpt,
            bool showPreviewChangesDialog)
        {
            if (showPreviewChangesDialog)
            {
                newSolution = FixAllGetFixesService.PreviewChanges(
                    _workspace.CurrentSolution,
                    newSolution,
                    fixAllPreviewChangesTitle: title,
                    fixAllTopLevelHeader: title,
                    languageOpt,
                    workspace: _workspace);
                if (newSolution == null)
                {
                    return false;
                }
            }

            void applyFix(IWaitContext context)
            {
                var operations = ImmutableArray.Create<CodeActionOperation>(new ApplyChangesOperation(newSolution));
                var cancellationToken = context.CancellationToken;
                _editHandlerService.Apply(
                    _workspace,
                    fromDocument: null,
                    operations: operations,
                    title: title,
                    progressTracker: context.ProgressTracker,
                    cancellationToken: cancellationToken);
            }

            var result = InvokeWithWaitDialog(applyFix, title, ServicesVSResources.Applying_fix);
            if (result == WaitIndicatorResult.Canceled)
            {
                return false;
            }

            // Kick off diagnostic re-analysis for affected projects so that diagnostics gets refreshed.
            Task.Run(() =>
            {
                var reanalyzeDocuments = fixedDiagnostics.Where(d => d.DocumentId != null).Select(d => d.DocumentId).Distinct();
                _diagnosticService.Reanalyze(_workspace, documentIds: reanalyzeDocuments, highPriority: true);

                var reanalyzeProjects = fixedDiagnostics.Where(d => d.DocumentId == null && d.ProjectId != null).Select(d => d.ProjectId).Distinct();
                _diagnosticService.Reanalyze(_workspace, projectIds: reanalyzeProjects, highPriority: true);
            });

            return true;
        }

        private static IEnumerable<DiagnosticData> FilterDiagnostics(IEnumerable<DiagnosticData> diagnostics, FixKind fixKind, bool isSuppressionInSource, bool onlyCompilerDiagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                var isCompilerDiagnostic = SuppressionHelpers.IsCompilerDiagnostic(diagnostic);
                if (onlyCompilerDiagnostics && !isCompilerDiagnostic)
                {
                    continue;
                }

                switch (fixKind)
                {
                    case FixKind.AddSuppression:
                        // Compiler diagnostics can only be suppressed in source.
                        if (!diagnostic.IsSuppressed &&
                            (isSuppressionInSource || !isCompilerDiagnostic))
                        {
                            yield return diagnostic;
                        }

                        break;

                    case FixKind.RemoveSuppression:
                        if (diagnostic.IsSuppressed)
                        {
                            yield return diagnostic;
                        }

                        break;

                    case FixKind.ConfigureSeverity:
                        yield return diagnostic;
                        break;

                    default:
                        throw ExceptionUtilities.Unreachable;
                }
            }
        }

        private WaitIndicatorResult InvokeWithWaitDialog(
            Action<IWaitContext> action, string waitDialogTitle, string waitDialogMessage)
        {
            var cancelled = false;
            var result = _waitIndicator.Wait(
                waitDialogTitle,
                waitDialogMessage,
                allowCancel: true,
                showProgress: true,
                action: waitContext =>
                {
                    try
                    {
                        action(waitContext);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                });

            return cancelled ? WaitIndicatorResult.Canceled : result;
        }

        private static ImmutableDictionary<Document, ImmutableArray<Diagnostic>> GetDocumentDiagnosticsMappedToNewSolution(ImmutableDictionary<Document, ImmutableArray<Diagnostic>> documentDiagnosticsToFixMap, Solution newSolution, string language)
        {
            ImmutableDictionary<Document, ImmutableArray<Diagnostic>>.Builder builder = null;
            foreach (var kvp in documentDiagnosticsToFixMap)
            {
                if (kvp.Key.Project.Language != language)
                {
                    continue;
                }

                var document = newSolution.GetDocument(kvp.Key.Id);
                if (document != null)
                {
                    builder = builder ?? ImmutableDictionary.CreateBuilder<Document, ImmutableArray<Diagnostic>>();
                    builder.Add(document, kvp.Value);
                }
            }

            return builder != null ? builder.ToImmutable() : ImmutableDictionary<Document, ImmutableArray<Diagnostic>>.Empty;
        }

        private static ImmutableDictionary<Project, ImmutableArray<Diagnostic>> GetProjectDiagnosticsMappedToNewSolution(ImmutableDictionary<Project, ImmutableArray<Diagnostic>> projectDiagnosticsToFixMap, Solution newSolution, string language)
        {
            ImmutableDictionary<Project, ImmutableArray<Diagnostic>>.Builder projectDiagsBuilder = null;
            foreach (var kvp in projectDiagnosticsToFixMap)
            {
                if (kvp.Key.Language != language)
                {
                    continue;
                }

                var project = newSolution.GetProject(kvp.Key.Id);
                if (project != null)
                {
                    projectDiagsBuilder = projectDiagsBuilder ?? ImmutableDictionary.CreateBuilder<Project, ImmutableArray<Diagnostic>>();
                    projectDiagsBuilder.Add(project, kvp.Value);
                }
            }

            return projectDiagsBuilder != null ? projectDiagsBuilder.ToImmutable() : ImmutableDictionary<Project, ImmutableArray<Diagnostic>>.Empty;
        }

        private static CodeFixProvider GetSuppressionFixer(IEnumerable<Diagnostic> diagnostics, string language, ICodeFixService codeFixService)
        {
            // Fetch the suppression fixer to apply the fix.
            return codeFixService.GetSuppressionFixer(language, diagnostics.Select(d => d.Id));
        }

        private async Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>> GetDocumentDiagnosticsToFixAsync(IEnumerable<DiagnosticData> diagnosticsToFix, Func<Project, bool> shouldFixInProject, bool filterStaleDiagnostics, CancellationToken cancellationToken)
        {
            bool isDocumentDiagnostic(DiagnosticData d) => d.DataLocation != null && d.HasTextSpan;

            var builder = ImmutableDictionary.CreateBuilder<DocumentId, List<DiagnosticData>>();
            foreach (var diagnosticData in diagnosticsToFix.Where(isDocumentDiagnostic))
            {
                if (!builder.TryGetValue(diagnosticData.DocumentId, out var diagnosticsPerDocument))
                {
                    diagnosticsPerDocument = new List<DiagnosticData>();
                    builder[diagnosticData.DocumentId] = diagnosticsPerDocument;
                }

                diagnosticsPerDocument.Add(diagnosticData);
            }

            if (builder.Count == 0)
            {
                return ImmutableDictionary<Document, ImmutableArray<Diagnostic>>.Empty;
            }

            var finalBuilder = ImmutableDictionary.CreateBuilder<Document, ImmutableArray<Diagnostic>>();
            var latestDocumentDiagnosticsMapOpt = filterStaleDiagnostics ? new Dictionary<DocumentId, ImmutableHashSet<DiagnosticData>>() : null;
            foreach (var group in builder.GroupBy(kvp => kvp.Key.ProjectId))
            {
                var projectId = group.Key;
                var project = _workspace.CurrentSolution.GetProject(projectId);
                if (project == null || !shouldFixInProject(project))
                {
                    continue;
                }

                if (filterStaleDiagnostics)
                {
                    var uniqueDiagnosticIds = group.SelectMany(kvp => kvp.Value.Select(d => d.Id)).ToImmutableHashSet();
                    var latestProjectDiagnostics = (await _diagnosticService.GetDiagnosticsForIdsAsync(project.Solution, project.Id, diagnosticIds: uniqueDiagnosticIds, includeSuppressedDiagnostics: true, cancellationToken: cancellationToken)
                        .ConfigureAwait(false)).Where(isDocumentDiagnostic);

                    latestDocumentDiagnosticsMapOpt.Clear();
                    foreach (var kvp in latestProjectDiagnostics.Where(d => d.DocumentId != null).GroupBy(d => d.DocumentId))
                    {
                        latestDocumentDiagnosticsMapOpt.Add(kvp.Key, kvp.ToImmutableHashSet());
                    }
                }

                foreach (var documentDiagnostics in group)
                {
                    var document = project.GetDocument(documentDiagnostics.Key);
                    if (document == null)
                    {
                        continue;
                    }

                    IEnumerable<DiagnosticData> documentDiagnosticsToFix;
                    if (filterStaleDiagnostics)
                    {
                        if (!latestDocumentDiagnosticsMapOpt.TryGetValue(document.Id, out var latestDocumentDiagnostics))
                        {
                            // Ignore stale diagnostics in error list.
                            latestDocumentDiagnostics = ImmutableHashSet<DiagnosticData>.Empty;
                        }

                        // Filter out stale diagnostics in error list.
                        documentDiagnosticsToFix = documentDiagnostics.Value.Where(d => latestDocumentDiagnostics.Contains(d) || SuppressionHelpers.IsSynthesizedExternalSourceDiagnostic(d));
                    }
                    else
                    {
                        documentDiagnosticsToFix = documentDiagnostics.Value;
                    }

                    if (documentDiagnosticsToFix.Any())
                    {
                        var diagnostics = await documentDiagnosticsToFix.ToDiagnosticsAsync(project, cancellationToken).ConfigureAwait(false);
                        finalBuilder.Add(document, diagnostics);
                    }
                }
            }

            return finalBuilder.ToImmutableDictionary();
        }

        private async Task<ImmutableDictionary<Project, ImmutableArray<Diagnostic>>> GetProjectDiagnosticsToFixAsync(IEnumerable<DiagnosticData> diagnosticsToFix, Func<Project, bool> shouldFixInProject, bool filterStaleDiagnostics, CancellationToken cancellationToken)
        {
            bool isProjectDiagnostic(DiagnosticData d) => d.DataLocation == null && d.ProjectId != null;
            var builder = ImmutableDictionary.CreateBuilder<ProjectId, List<DiagnosticData>>();
            foreach (var diagnosticData in diagnosticsToFix.Where(isProjectDiagnostic))
            {
                if (!builder.TryGetValue(diagnosticData.ProjectId, out var diagnosticsPerProject))
                {
                    diagnosticsPerProject = new List<DiagnosticData>();
                    builder[diagnosticData.ProjectId] = diagnosticsPerProject;
                }

                diagnosticsPerProject.Add(diagnosticData);
            }

            if (builder.Count == 0)
            {
                return ImmutableDictionary<Project, ImmutableArray<Diagnostic>>.Empty;
            }

            var finalBuilder = ImmutableDictionary.CreateBuilder<Project, ImmutableArray<Diagnostic>>();
            var latestDiagnosticsToFixOpt = filterStaleDiagnostics ? new HashSet<DiagnosticData>() : null;
            foreach (var kvp in builder)
            {
                var projectId = kvp.Key;
                var project = _workspace.CurrentSolution.GetProject(projectId);
                if (project == null || !shouldFixInProject(project))
                {
                    continue;
                }

                var diagnostics = kvp.Value;
                IEnumerable<DiagnosticData> projectDiagnosticsToFix;
                if (filterStaleDiagnostics)
                {
                    var uniqueDiagnosticIds = diagnostics.Select(d => d.Id).ToImmutableHashSet();
                    var latestDiagnosticsFromDiagnosticService = (await _diagnosticService.GetDiagnosticsForIdsAsync(project.Solution, project.Id, diagnosticIds: uniqueDiagnosticIds, includeSuppressedDiagnostics: true, cancellationToken: cancellationToken)
                        .ConfigureAwait(false));

                    latestDiagnosticsToFixOpt.Clear();
                    latestDiagnosticsToFixOpt.AddRange(latestDiagnosticsFromDiagnosticService.Where(isProjectDiagnostic));

                    // Filter out stale diagnostics in error list.
                    projectDiagnosticsToFix = diagnostics.Where(d => latestDiagnosticsFromDiagnosticService.Contains(d) || SuppressionHelpers.IsSynthesizedExternalSourceDiagnostic(d));
                }
                else
                {
                    projectDiagnosticsToFix = diagnostics;
                }

                if (projectDiagnosticsToFix.Any())
                {
                    var projectDiagnostics = await projectDiagnosticsToFix.ToDiagnosticsAsync(project, cancellationToken).ConfigureAwait(false);
                    finalBuilder.Add(project, projectDiagnostics);
                }
            }

            return finalBuilder.ToImmutableDictionary();
        }
    }
}
