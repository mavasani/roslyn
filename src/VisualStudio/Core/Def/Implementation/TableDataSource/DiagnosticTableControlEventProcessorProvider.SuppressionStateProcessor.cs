// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.LanguageServices.Implementation.Suppression;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    internal partial class DiagnosticTableControlEventProcessorProvider : AbstractTableControlEventProcessorProvider<DiagnosticData>, IVisualStudioDiagnosticListCommandHandler
    {
        private partial class SuppressionStateEventProcessor : EventProcessor
        {
            private readonly VisualStudioWorkspace _workspace;
            private readonly IVsUIShell _shellService;
            private readonly IVisualStudioBulkSuppressionService _suppressionsService;

            private int _selectedActiveItems;
            private int _selectedSuppressedItems;
            private int _selectedRoslynItems;
            private int _selectedNonSuppressionStateItems;

            public SuppressionStateEventProcessor(IServiceProvider serviceProvider, VisualStudioWorkspace workspace, IVisualStudioBulkSuppressionService suppressionsService, IWpfTableControl tableControl)
                : base(tableControl)
            {
                _workspace = workspace;
                _suppressionsService = suppressionsService;
                _shellService = (IVsUIShell)serviceProvider.GetService(typeof(IVsUIShell));
                var fixMultipleOccurencesService = _workspace.Services.GetService<IFixMultipleOccurrencesService>();
                var menuCommandService = (IMenuCommandService)serviceProvider.GetService(typeof(IMenuCommandService));
                if (menuCommandService != null && fixMultipleOccurencesService != null)
                {
                    AddSuppressionsCommandHandlers(menuCommandService);
                }

                ClearState();
                InitializeFromTableControlIfNeeded();
            }

            private int SelectedItems => _selectedActiveItems + _selectedSuppressedItems + _selectedNonSuppressionStateItems;

            private void ClearState()
            {
                _selectedActiveItems = 0;
                _selectedSuppressedItems = 0;
                _selectedRoslynItems = 0;
                _selectedNonSuppressionStateItems = 0;
            }

            private void InitializeFromTableControlIfNeeded()
            {
                if (SelectedItems == TableControl.SelectedEntries.Count())
                {
                    // We already have up-to-date state data, so don't need to re-compute.
                    return;
                }

                ClearState();
                if (ProcessEntries(TableControl.SelectedEntries, added: true))
                {
                    UpdateQueryStatus();
                }
            }

            private void AddSuppressionsCommandHandlers(IMenuCommandService menuCommandService)
            {
                if (menuCommandService != null)
                {
                    AddCommand(menuCommandService, ID.RoslynCommands.AddSuppressions, delegate { }, OnAddSuppressionsStatus);
                    AddCommand(menuCommandService, ID.RoslynCommands.AddSuppressionsInSource, OnAddSuppressionsInSource, OnAddSuppressionsInSourceStatus);
                    AddCommand(menuCommandService, ID.RoslynCommands.AddSuppressionsInSuppressionFile, OnAddSuppressionsInSuppressionFile, OnAddSuppressionsInSuppressionFileStatus);
                    AddCommand(menuCommandService, ID.RoslynCommands.RemoveSuppressions, OnRemoveSuppressions, OnRemoveSuppressionsStatus);
                }
            }

            public override void PostprocessSelectionChanged(TableSelectionChangedEventArgs e)
            {
                var hasAddedSuppressionStateEntry = ProcessEntries(e.AddedEntries, added: true);
                var hasRemovedSuppressionStateEntry = ProcessEntries(e.RemovedEntries, added: false);

                // If any entry that supports suppression state was ever involved, update query status since each item in the error list
                // can have different context menu.
                if (hasAddedSuppressionStateEntry || hasRemovedSuppressionStateEntry)
                {
                    UpdateQueryStatus();
                }

                InitializeFromTableControlIfNeeded();
            }

            private bool ProcessEntries(IEnumerable<ITableEntryHandle> entryHandles, bool added)
            {
                bool isRoslynEntry, isSuppressedEntry;
                var hasSuppressionStateEntry = false;
                foreach (var entryHandle in entryHandles)
                {
                    if (EntrySupportsSuppressionState(entryHandle, out isRoslynEntry, out isSuppressedEntry))
                    {
                        hasSuppressionStateEntry = true;
                        HandleSuppressionStateEntry(isRoslynEntry, isSuppressedEntry, added);
                    }
                    else
                    {
                        HandleNonSuppressionStateEntry(added);
                    }
                }

                return hasSuppressionStateEntry;
            }

            private static bool EntrySupportsSuppressionState(ITableEntryHandle entryHandle, out bool isRoslynEntry, out bool isSuppressedEntry)
            {
                isRoslynEntry = false;
                isSuppressedEntry = false;

                object value;
                if (!entryHandle.TryGetValue(SuppressionStateColumnDefinition.ColumnName, out value))
                {
                    return false;
                }

                var entryValue = value as string;
                if (entryValue == null)
                {
                    return false;
                }

                if (entryValue == ServicesVSResources.SuppressionStateSuppressed)
                {
                    isSuppressedEntry = true;
                }
                else if (entryValue != ServicesVSResources.SuppressionStateActive)
                {
                    return false;
                }

                isRoslynEntry = GetEntriesSnapshot(entryHandle) != null;
                return true;
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

            private void HandleSuppressionStateEntry(bool isRoslynEntry, bool isSuppressedEntry, bool added)
            {
                if (isRoslynEntry)
                {
                    UpdateSelectedItems(added, ref _selectedRoslynItems);
                }

                if (isSuppressedEntry)
                {
                    UpdateSelectedItems(added, ref _selectedSuppressedItems);
                }
                else
                {
                    UpdateSelectedItems(added, ref _selectedActiveItems);
                }
            }

            private void HandleNonSuppressionStateEntry(bool added)
            {
                UpdateSelectedItems(added, ref _selectedNonSuppressionStateItems);
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

            // If we can suppress either in source or in suppression file, we enable suppress context menu.
            private bool CanSuppress => CanSuppressInSource || CanSuppressInSuppressionFile;

            // If only suppressed items are selected, we enable remove suppressions.
            private bool CanRemoveSuppressions => _selectedActiveItems == 0 && _selectedSuppressedItems > 0;

            // If only Roslyn active items are selected, we enable suppress in source.
            private bool CanSuppressInSource => _selectedActiveItems > 0 &&
                _selectedSuppressedItems == 0 &&
                _selectedRoslynItems == _selectedActiveItems;

            // If only active items are selected (Roslyn or FxCop), we enable suppress in suppression file.
            private bool CanSuppressInSuppressionFile => _selectedActiveItems > 0 &&
                _selectedSuppressedItems == 0 &&
                _selectedRoslynItems == _selectedActiveItems;

            private void OnAddSuppressionsStatus(object sender, EventArgs e)
            {
                MenuCommand command = sender as MenuCommand;
                command.Visible = CanSuppress;
                command.Enabled = !KnownUIContexts.SolutionBuildingContext.IsActive;
            }

            private void OnRemoveSuppressionsStatus(object sender, EventArgs e)
            {
                MenuCommand command = sender as MenuCommand;
                command.Visible = CanRemoveSuppressions;
                command.Enabled = !KnownUIContexts.SolutionBuildingContext.IsActive;
            }

            private void OnAddSuppressionsInSourceStatus(object sender, EventArgs e)
            {
                MenuCommand command = sender as MenuCommand;
                command.Visible = CanSuppressInSource;
                command.Enabled = !KnownUIContexts.SolutionBuildingContext.IsActive;
            }

            private void OnAddSuppressionsInSuppressionFileStatus(object sender, EventArgs e)
            {
                MenuCommand command = sender as MenuCommand;
                command.Visible = CanSuppressInSuppressionFile;
                command.Enabled = !KnownUIContexts.SolutionBuildingContext.IsActive;
            }

            private void OnAddSuppressionsInSource(object sender, EventArgs e)
            {
                _suppressionsService.AddSuppressions(selectedErrorListEntriesOnly: true, suppressInSuppressionFile: false);
            }

            private void OnAddSuppressionsInSuppressionFile(object sender, EventArgs e)
            {
                _suppressionsService.AddSuppressions(selectedErrorListEntriesOnly: true, suppressInSuppressionFile: true);
            }

            private void OnRemoveSuppressions(object sender, EventArgs e)
            {
            }

            /// <summary>
            /// Add a command handler and status query handler for a menu item
            /// </summary>
            private static OleMenuCommand AddCommand(
                IMenuCommandService menuCommandService,
                int commandId,
                EventHandler invokeHandler,
                EventHandler beforeQueryStatus)
            {
                var commandIdWithGroupId = new CommandID(Guids.RoslynGroupId, commandId);
                var command = new OleMenuCommand(invokeHandler, delegate { }, beforeQueryStatus, commandIdWithGroupId);
                menuCommandService.AddCommand(command);
                return command;
            }   
        }
    }
}
