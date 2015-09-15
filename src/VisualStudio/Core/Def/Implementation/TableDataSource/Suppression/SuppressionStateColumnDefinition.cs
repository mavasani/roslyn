// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    /// <summary>
    /// Error list column for Suppression state of a diagnostic.
    /// </summary>
    /// <remarks>
    /// TODO: Move this column down to the shell as it is shared by multiple issue sources (Roslyn and FxCop).
    /// </remarks>
    [Export(typeof(ITableColumnDefinition))]
    [Name(ColumnName)]
    internal class SuppressionStateColumnDefinition : TableColumnDefinitionBase
    {
        public const string ColumnName = "suppressionstate";
        private static readonly string[] _defaultFilters = new[] { ServicesVSResources.SuppressionStateActive, ServicesVSResources.SuppressionStateSuppressed };
        private static readonly string[] _defaultCheckedFilters = new[] { ServicesVSResources.SuppressionStateActive };
        private readonly DiagnosticTableControlSuppressionStateService _suppressionStateService;
        private bool _changeVisibilityOnSuppressedDiagnostic = true;
        private bool _visiblity = false;

        [ImportingConstructor]
        public SuppressionStateColumnDefinition(DiagnosticTableControlSuppressionStateService suppressionStateService)
        {
            _suppressionStateService = suppressionStateService;
        }

        public override string Name => ColumnName;
        public override string DisplayName => ServicesVSResources.SuppressionStateColumnHeader;
        public override string HeaderName => ServicesVSResources.SuppressionStateColumnHeader;
        public override double MinWidth => 50.0;
        public override bool DefaultVisible => false;
        public override bool IsFilterable => true;
        public override IEnumerable<string> FilterPresets => _defaultFilters;
        
        public static void SetDefaultFilter(IWpfTableControl tableControl)
        {
            // We want only the active diagnostics to show up in the error list by default.
            var suppressionStateColumn = tableControl.ColumnDefinitionManager.GetColumnDefinition(ColumnName) as SuppressionStateColumnDefinition;
            if (suppressionStateColumn != null)
            {
                tableControl.SetFilter(ColumnName, new ColumnHashSetFilter(suppressionStateColumn, excluded: ServicesVSResources.SuppressionStateSuppressed));
                tableControl.FiltersChanged += suppressionStateColumn.TableControl_FiltersChanged;
                tableControl.EntriesChanged += suppressionStateColumn.TableControl_EntriesChanged;
            }
        }

        private void TableControl_FiltersChanged(object sender, FiltersChangedEventArgs e)
        {
            // User explicitly changed a filter, so don't muck with column visibility ourselves.
            _changeVisibilityOnSuppressedDiagnostic = false;
        }

        private void TableControl_EntriesChanged(object sender, EntriesChangedEventArgs e)
        {
            if (_changeVisibilityOnSuppressedDiagnostic)
            {
                this.
                _suppressionStateService.CanRemoveSuppressionsSelectedEntries
            }
        }
    }
}

