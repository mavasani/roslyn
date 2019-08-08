// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes.Suppression;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource.Configuration
{
    /// <summary>
    /// Service to maintain information about the configuration state of specific set of items in the error list.
    /// </summary>
    [Export(typeof(IVisualStudioDiagnosticListConfigurationStateService))]
    internal class VisualStudioDiagnosticListConfigurationStateService : IVisualStudioDiagnosticListConfigurationStateService
    {
        private readonly VisualStudioWorkspace _workspace;
        private readonly IVsUIShell _shellService;
        private readonly IWpfTableControl _tableControl;

        private int _selectedConfigurableBuildItems;
        private int _selectedConfigurableLiveAnalysisItems;
        private int _selectedConfigurableLiveAnalysisActiveItems;
        private int _selectedConfigurableLiveAnalysisSuppressedItems;
        private int _selectedConfigurableLiveAnalysisCompilerDiagnosticItems;
        private int _selectedConfigurableLiveAnalysisNoLocationDiagnosticItems;
        private int _selectedNonConfigurableItems;

        [ImportingConstructor]
        public VisualStudioDiagnosticListConfigurationStateService(
            SVsServiceProvider serviceProvider,
            VisualStudioWorkspace workspace)
        {
            _workspace = workspace;
            _shellService = (IVsUIShell)serviceProvider.GetService(typeof(SVsUIShell));
            var errorList = serviceProvider.GetService(typeof(SVsErrorList)) as IErrorList;
            _tableControl = errorList?.TableControl;

            ClearState();
            InitializeFromTableControlIfNeeded();
        }

        private int SelectedItems => _selectedConfigurableLiveAnalysisItems + _selectedConfigurableBuildItems + _selectedNonConfigurableItems;

        public bool CanConfigureSelectedEntries => _selectedConfigurableLiveAnalysisItems > 0 || _selectedConfigurableBuildItems > 0;

        // If we can suppress either in source or in suppression file, we enable suppress context menu.
        public bool CanSuppressSelectedEntries => CanSuppressSelectedEntriesInSource || CanSuppressSelectedEntriesInSuppressionFiles;

        // If at least one suppressed item is selected, we enable remove suppressions.
        public bool CanRemoveSuppressionsSelectedEntries => _selectedConfigurableLiveAnalysisSuppressedItems > 0;

        // If at least one Roslyn active item with location is selected, we enable suppress in source.
        // Note that we do not support suppress in source when mix of Roslyn and non-Roslyn items are selected as in-source suppression has different meaning and implementation for these.
        public bool CanSuppressSelectedEntriesInSource => _selectedConfigurableLiveAnalysisActiveItems > 0 &&
            _selectedConfigurableLiveAnalysisItems == _selectedConfigurableLiveAnalysisActiveItems &&
            (_selectedConfigurableLiveAnalysisItems - _selectedConfigurableLiveAnalysisNoLocationDiagnosticItems) > 0;

        // If at least one Roslyn active item is selected, we enable suppress in suppression file.
        // Also, compiler diagnostics cannot be suppressed in suppression file, so there must be at least one non-compiler item.
        public bool CanSuppressSelectedEntriesInSuppressionFiles => _selectedConfigurableLiveAnalysisActiveItems > 0 &&
            (_selectedConfigurableLiveAnalysisItems - _selectedConfigurableLiveAnalysisCompilerDiagnosticItems) > 0;

        private void ClearState()
        {
            _selectedConfigurableBuildItems = 0;
            _selectedConfigurableLiveAnalysisActiveItems = 0;
            _selectedConfigurableLiveAnalysisSuppressedItems = 0;
            _selectedConfigurableLiveAnalysisItems = 0;
            _selectedConfigurableLiveAnalysisCompilerDiagnosticItems = 0;
            _selectedConfigurableLiveAnalysisNoLocationDiagnosticItems = 0;
            _selectedNonConfigurableItems = 0;
        }

        private void InitializeFromTableControlIfNeeded()
        {
            if (_tableControl == null)
            {
                return;
            }

            if (SelectedItems == _tableControl.SelectedEntries.Count())
            {
                // We already have up-to-date state data, so don't need to re-compute.
                return;
            }

            ClearState();
            if (ProcessEntries(_tableControl.SelectedEntries, added: true))
            {
                UpdateQueryStatus();
            }
        }

        /// <summary>
        /// Updates configuration state information when the selected entries change in the error list.
        /// </summary>
        public void ProcessSelectionChanged(TableSelectionChangedEventArgs e)
        {
            var hasAddedConfigurableEntry = ProcessEntries(e.AddedEntries, added: true);
            var hasRemovedConfigurableEntry = ProcessEntries(e.RemovedEntries, added: false);

            // If any configurable entry was added/removed, update query status since each item in the error list
            // can have different context menu.
            if (hasAddedConfigurableEntry || hasRemovedConfigurableEntry)
            {
                UpdateQueryStatus();
            }

            InitializeFromTableControlIfNeeded();
        }

        private bool ProcessEntries(IEnumerable<ITableEntryHandle> entryHandles, bool added)
        {
            var hasConfigurableEntry = false;
            foreach (var entryHandle in entryHandles)
            {
                if (IsConfigurableEntry(entryHandle, out var isConfigurableLiveAnalysisEntry, out var isSuppressedEntry, out var isCompilerDiagnosticEntry, out var isNoLocationDiagnosticEntry))
                {
                    hasConfigurableEntry = true;
                    HandleConfigurableEntry(isConfigurableLiveAnalysisEntry, isSuppressedEntry, isCompilerDiagnosticEntry, isNoLocationDiagnosticEntry, added);
                }
                else
                {
                    HandleNonConfigurableEntry(added);
                }
            }

            return hasConfigurableEntry;
        }

        private static bool IsConfigurableEntry(ITableEntryHandle entryHandle, out bool isLiveAnalysisEntry, out bool isSuppressedEntry, out bool isCompilerDiagnosticEntry, out bool isNoLocationDiagnosticEntry)
        {
            isNoLocationDiagnosticEntry = !entryHandle.TryGetValue(StandardTableColumnDefinitions.DocumentName, out string filePath) ||
                string.IsNullOrEmpty(filePath);

            var roslynSnapshot = GetEntriesSnapshot(entryHandle, out var index);
            var diagnosticData = roslynSnapshot?.GetItem(index)?.Data;
            if (diagnosticData == null)
            {
                isLiveAnalysisEntry = false;
                isSuppressedEntry = false;
                isCompilerDiagnosticEntry = false;
                return false;
            }

            isLiveAnalysisEntry = !diagnosticData.IsBuildDiagnostic();
            isSuppressedEntry = diagnosticData.IsSuppressed;
            isCompilerDiagnosticEntry = SuppressionHelpers.IsCompilerDiagnostic(diagnosticData);
            return IsConfigurableEntry(diagnosticData);
        }

        /// <summary>
        /// Returns true if the given diagnostic is configurable.
        /// </summary>
        /// <returns></returns>
        private static bool IsConfigurableEntry(DiagnosticData entry)
        {
            // Compiler diagnostics with severity 'Error' are not configurable.
            return entry != null &&
                !SuppressionHelpers.IsNotConfigurableDiagnostic(entry);
        }

        private static AbstractTableEntriesSnapshot<DiagnosticTableItem> GetEntriesSnapshot(ITableEntryHandle entryHandle, out int index)
        {
            if (!entryHandle.TryGetSnapshot(out var snapshot, out index))
            {
                return null;
            }

            return snapshot as AbstractTableEntriesSnapshot<DiagnosticTableItem>;
        }

        /// <summary>
        /// Gets <see cref="DiagnosticData"/> objects for selected error list entries.
        /// </summary>
        public ImmutableArray<DiagnosticData> GetSelectedConfigurableItems(bool includeBuildDiagnostics, CancellationToken cancellationToken)
        {
            var builder = ArrayBuilder<DiagnosticData>.GetInstance();

            foreach (var entryHandle in _tableControl.SelectedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                DiagnosticData diagnosticData = null;
                var roslynSnapshot = GetEntriesSnapshot(entryHandle, out var index);
                if (roslynSnapshot != null)
                {
                    diagnosticData = roslynSnapshot.GetItem(index)?.Data;
                    if (IsConfigurableEntry(diagnosticData) &&
                        (includeBuildDiagnostics || diagnosticData.IsBuildDiagnostic()))
                    {
                        builder.Add(diagnosticData);
                    }
                }
            }

            return builder.ToImmutableAndFree();
        }

        private static void UpdateSelectedItems(bool added, ref int count)
        {
            if (added)
            {
                count++;
            }
            else
            {
                count--;
            }
        }

        private void HandleConfigurableEntry(bool isConfigurableLiveAnalysisEntry, bool isSuppressedEntry, bool isCompilerDiagnosticEntry, bool isNoLocationDiagnosticEntry, bool added)
        {
            if (isConfigurableLiveAnalysisEntry)
            {
                UpdateSelectedItems(added, ref _selectedConfigurableLiveAnalysisItems);

                if (isCompilerDiagnosticEntry)
                {
                    UpdateSelectedItems(added, ref _selectedConfigurableLiveAnalysisCompilerDiagnosticItems);
                }

                if (isNoLocationDiagnosticEntry)
                {
                    UpdateSelectedItems(added, ref _selectedConfigurableLiveAnalysisNoLocationDiagnosticItems);
                }

                if (isSuppressedEntry)
                {
                    UpdateSelectedItems(added, ref _selectedConfigurableLiveAnalysisSuppressedItems);
                }
                else
                {
                    UpdateSelectedItems(added, ref _selectedConfigurableLiveAnalysisActiveItems);
                }
            }
        }

        private void HandleNonConfigurableEntry(bool added)
        {
            UpdateSelectedItems(added, ref _selectedNonConfigurableItems);
        }

        private void UpdateQueryStatus()
        {
            // Force the shell to refresh the QueryStatus for all the command since default behavior is it only does query
            // when focus on error list has changed, not individual items.
            if (_shellService != null)
            {
                _shellService.UpdateCommandUI(0);
            }
        }
    }
}
