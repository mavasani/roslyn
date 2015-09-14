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
using Microsoft.CodeAnalysis.CodeFixes.Suppression;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Suppression
{
    [Export(typeof(IVisualStudioBulkSuppressionService))]
    internal class VisualStudioBulkSuppressionService : IVisualStudioBulkSuppressionService
    {
        private readonly VisualStudioWorkspace _workspace;
        private readonly ICodeFixService _codeFixService;
        private readonly IWpfTableControl _errorListTableControl;
        private readonly IFixMultipleOccurrencesService _fixMultipleOccurencesService;
        private readonly IWaitIndicator _waitIndicator;

        [ImportingConstructor]
        public VisualStudioBulkSuppressionService(
            SVsServiceProvider serviceProvider,
            VisualStudioWorkspace workspace,
            ICodeFixService codeFixService,
            IFixMultipleOccurrencesService fixMultipleOccurencesFixService,
            IWaitIndicator waitIndicator)
        {
            _workspace = workspace;
            _codeFixService = codeFixService;
            _fixMultipleOccurencesService = fixMultipleOccurencesFixService;
            _waitIndicator = waitIndicator;

            var errorList = serviceProvider.GetService(typeof(SVsErrorList)) as IErrorList;
            _errorListTableControl = errorList?.TableControl;
        }

        public void AddSuppressions(bool selectedErrorListEntriesOnly, bool suppressInSuppressionFile)
        {
            if (_errorListTableControl == null)
            {
                return;
            }

            ApplySuppressionFix(selectedErrorListEntriesOnly, suppressInSuppressionFile);
        }

        public void RemoveSuppressions(bool selectedErrorListEntriesOnly)
        {
            if (_errorListTableControl == null)
            {
                return;
            }            
        }

        private void ApplySuppressionFix(bool selectedErrorListEntriesOnly, bool isSuppressionInSource)
        {
            var cts = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                var selectedEntriesMap = await GetEntriesToFixMapAsync(_workspace, selectedErrorListEntriesOnly, cts.Token).ConfigureAwait(false);
                if (selectedEntriesMap.Count > 0)
                {
                    return;
                }

                var equivalenceKey = isSuppressionInSource ? FeaturesResources.SuppressWithPragma : FeaturesResources.SuppressWithGlobalSuppressMessage;
                var groups = selectedEntriesMap.GroupBy(entry => entry.Key.Project.Language);
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

                    var result = _waitIndicator.Wait(
                        previewChangesTitle,
                        waitDialogMessage,
                        allowCancel: true,
                        action: waitContext =>
                        {
                            try
                            {
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
                                    return;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                            }
                        });

                    if (documentDiagnosticsPerLanguage == null || suppressionFixer == null)
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
                        cts.Token);

                    newSolution = _workspace.CurrentSolution;
                    if (result == WaitIndicatorResult.Canceled || currentSolution == newSolution)
                    {
                        break;
                    }

                    needsMappingToNewSolution = true;
                }
            }, cts.Token);

            task.Wait(cts.Token);
        }

        private static AbstractTableEntriesSnapshot<DiagnosticData> GetEntriesSnapshot(ITableEntryHandle entryHandle)
        {
            int index;
            return GetEntriesSnapshot(entryHandle, out index);
        }

        private static AbstractTableEntriesSnapshot<DiagnosticData> GetEntriesSnapshot(ITableEntryHandle entryHandle, out int index)
        {
            ITableEntriesSnapshot snapshot;
            if (!entryHandle.TryGetSnapshot(out snapshot, out index))
            {
                return null;
            }

            return snapshot as AbstractTableEntriesSnapshot<DiagnosticData>;
        }

        private async Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>> GetEntriesToFixMapAsync(Workspace workspace, bool selectedEntriesOnly, CancellationToken cancellationToken)
        {
            var builder = ImmutableDictionary.CreateBuilder<DocumentId, List<DiagnosticData>>();
            var entries = selectedEntriesOnly ? _errorListTableControl.SelectedEntries : _errorListTableControl.Entries;
            foreach (var entryHandle in _errorListTableControl.SelectedEntries)
            {
                int index;
                var roslynSnapshot = GetEntriesSnapshot(entryHandle, out index);
                if (roslynSnapshot != null)
                {
                    var diagnosticData = roslynSnapshot.GetItem(index);
                    List<DiagnosticData> diagnosticsPerDocument;
                    if (!builder.TryGetValue(diagnosticData.DocumentId, out diagnosticsPerDocument))
                    {
                        diagnosticsPerDocument = new List<DiagnosticData>();
                        builder[diagnosticData.DocumentId] = diagnosticsPerDocument;
                    }

                    diagnosticsPerDocument.Add(diagnosticData);
                }
            }

            if (builder.Count == 0)
            {
                return ImmutableDictionary<Document, ImmutableArray<Diagnostic>>.Empty;
            }

            var finalBuilder = ImmutableDictionary.CreateBuilder<Document, ImmutableArray<Diagnostic>>();
            string language = null;
            foreach (var group in builder.GroupBy(kvp => kvp.Key.ProjectId))
            {
                var projectId = group.Key;
                var project = workspace.CurrentSolution.GetProject(projectId);
                if (projectId == null)
                {
                    continue;
                }

                if (language == null)
                {
                    language = project.Language;
                }
                else if (project.Language != language)
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

        private static bool TryGetValue(ITableEntryHandle entryHandle, string keyName, out string content)
        {
            content = null;

            object contentObj;
            if (!entryHandle.TryGetValue(keyName, out contentObj))
            {
                return false;
            }

            content = contentObj as string;
            return !string.IsNullOrEmpty(content);
        }

        private static bool TryGetValue(ITableEntryHandle entryHandle, string keyName, out int content)
        {
            content = -1;

            object contentObj;
            if (!entryHandle.TryGetValue(keyName, out contentObj))
            {
                return false;
            }

            content = (int)contentObj;
            return content >= 0;
        }
    }
}
