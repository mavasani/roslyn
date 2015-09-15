// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Suppression
{
    [Export(typeof(IVisualStudioSuppressionFixService))]
    internal sealed class VisualStudioSuppressionFixService : IVisualStudioSuppressionFixService
    {
        private readonly VisualStudioWorkspace _workspace;
        private readonly IWpfTableControl _tableControl;
        private readonly ICodeFixService _codeFixService;
        private readonly IFixMultipleOccurrencesService _fixMultipleOccurencesService;
        private readonly DiagnosticTableControlSuppressionStateService _suppressionStateService;
        private readonly IWaitIndicator _waitIndicator;

        [ImportingConstructor]
        public VisualStudioSuppressionFixService(
            SVsServiceProvider serviceProvider,
            VisualStudioWorkspace workspace,
            ICodeFixService codeFixService,
            DiagnosticTableControlSuppressionStateService suppressionStateService,
            IWaitIndicator waitIndicator)
        {
            _workspace = workspace;
            _codeFixService = codeFixService;
            _suppressionStateService = suppressionStateService;
            _waitIndicator = waitIndicator;
            _fixMultipleOccurencesService = workspace.Services.GetService<IFixMultipleOccurrencesService>();

            var errorList = serviceProvider.GetService(typeof(SVsErrorList)) as IErrorList;
            _tableControl = errorList?.TableControl;
        }

        public void AddSuppressions(bool selectedErrorListEntriesOnly, bool suppressInSource)
        {
            if (_tableControl == null)
            {
                return;
            }

            ApplySuppressionFix(selectedErrorListEntriesOnly, suppressInSource);
        }

        public void RemoveSuppressions(bool selectedErrorListEntriesOnly)
        {
            if (_tableControl == null)
            {
                return;
            }
        }

        private void ApplySuppressionFix(bool selectedEntriesOnly, bool suppressInSource)
        {
            ImmutableDictionary<Document, ImmutableArray<Diagnostic>> diagnosticsToFixMap = null;

            var result = _waitIndicator.Wait(
                ServicesVSResources.SuppressMultipleOccurrences,
                ServicesVSResources.ComputingSuppressionFix,
                allowCancel: true,
                action: waitContext =>
                {
                    try
                    {
                        var diagnosticsToFix = _suppressionStateService.GetItems(
                                selectedEntriesOnly,
                                isAddSuppression: true,
                                isSuppressionInSource: suppressInSource,
                                cancellationToken: waitContext.CancellationToken);

                        if (diagnosticsToFix.IsEmpty)
                        {
                            return;
                        }

                        waitContext.CancellationToken.ThrowIfCancellationRequested();
                        diagnosticsToFixMap = GetDiagnosticsToFixMapAsync(_workspace, diagnosticsToFix, waitContext.CancellationToken).WaitAndGetResult(waitContext.CancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                });

            if (result == WaitIndicatorResult.Canceled ||
                diagnosticsToFixMap == null || diagnosticsToFixMap.IsEmpty)
            {
                return;
            }

            var equivalenceKey = suppressInSource ? FeaturesResources.SuppressWithPragma : FeaturesResources.SuppressWithGlobalSuppressMessage;
            var groups = diagnosticsToFixMap.GroupBy(entry => entry.Key.Project.Language);
            var hasMultipleLangauges = groups.Count() > 1;
            var currentSolution = _workspace.CurrentSolution;
            var newSolution = currentSolution;
            var needsMappingToNewSolution = false;

            foreach (var group in groups)
            {
                var language = group.Key;
                var previewChangesTitle = hasMultipleLangauges ? string.Format(ServicesVSResources.SuppressMultipleOccurrencesForLanguage, language) : ServicesVSResources.SuppressMultipleOccurrences;
                var waitDialogMessage = hasMultipleLangauges ? string.Format(ServicesVSResources.ComputingSuppressionFixForLanguage, language) : ServicesVSResources.ComputingSuppressionFix;

                ImmutableDictionary<Document, ImmutableArray<Diagnostic>> documentDiagnosticsPerLanguage = null;
                CodeFixProvider suppressionFixer = null;

                if (needsMappingToNewSolution)
                {
                    var builder = ImmutableDictionary.CreateBuilder<Document, ImmutableArray<Diagnostic>>();
                    foreach (var kvp in group)
                    {
                        var document = newSolution.GetDocument(kvp.Key.Id);
                        if (document != null)
                        {
                            builder.Add(document, kvp.Value);
                        }
                    }

                    documentDiagnosticsPerLanguage = builder.ToImmutable();
                }
                else
                {
                    documentDiagnosticsPerLanguage = group.ToImmutableDictionary();
                }

                var allDiagnosticsBuilder = ImmutableArray.CreateBuilder<Diagnostic>();
                foreach (var documentDiagnostics in documentDiagnosticsPerLanguage)
                {
                    allDiagnosticsBuilder.AddRange(documentDiagnostics.Value);
                }

                suppressionFixer = _codeFixService.GetSuppressionFixer(language, allDiagnosticsBuilder.ToImmutable());
                if (suppressionFixer == null)
                {
                    continue;
                }

                _fixMultipleOccurencesService.ComputeAndApplyFix(
                    documentDiagnosticsPerLanguage,
                    _workspace,
                    suppressionFixer,
                    suppressionFixer.GetFixAllProvider(),
                    equivalenceKey,
                    previewChangesTitle,
                    waitDialogMessage,
                    CancellationToken.None);
                
                newSolution = _workspace.CurrentSolution;
                if (currentSolution == newSolution)
                {
                    break;
                }

                needsMappingToNewSolution = true;
            }
        }

        private async Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>> GetDiagnosticsToFixMapAsync(Workspace workspace, IEnumerable<DiagnosticData> diagnosticsToFix, CancellationToken cancellationToken)
        {
            var builder = ImmutableDictionary.CreateBuilder<DocumentId, List<DiagnosticData>>();
            foreach (var diagnosticData in diagnosticsToFix)
            {
                List<DiagnosticData> diagnosticsPerDocument;
                if (!builder.TryGetValue(diagnosticData.DocumentId, out diagnosticsPerDocument))
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
            foreach (var group in builder.GroupBy(kvp => kvp.Key.ProjectId))
            {
                var projectId = group.Key;
                var project = workspace.CurrentSolution.GetProject(projectId);
                if (projectId == null)
                {
                    continue;
                }

                var documentsToTreeMap = await GetDocumentIdsToTreeMapAsync(project, cancellationToken).ConfigureAwait(false);
                foreach (var documentDiagnostics in group)
                {
                    var document = project.GetDocument(documentDiagnostics.Key);
                    if (document == null)
                    {
                        continue;
                    }

                    var diagnostics = await DiagnosticData.ToDiagnosticsAsync(project, documentDiagnostics.Value, cancellationToken).ConfigureAwait(false);
                    finalBuilder.Add(document, diagnostics.ToImmutableArray());
                }
            }

            return finalBuilder.ToImmutableDictionary();
        }

        private static async Task<ImmutableDictionary<DocumentId, SyntaxTree>> GetDocumentIdsToTreeMapAsync(Project project, CancellationToken cancellationToken)
        {
            var builder = ImmutableDictionary.CreateBuilder<DocumentId, SyntaxTree>();
            foreach (var document in project.Documents)
            {
                var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                builder.Add(document.Id, tree);
            }

            return builder.ToImmutable();
        }
    }
}
