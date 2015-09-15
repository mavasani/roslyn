// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    [Export(typeof(ITableColumnDefinition))]
    [Name(ColumnName)]
    internal class SuppressionStateColumnDefinition : TableColumnDefinitionBase
    {
        public const string ColumnName = "suppressionstate";
        private static readonly string[] _defaultFilters = new[] { ServicesVSResources.SuppressionStateActive, ServicesVSResources.SuppressionStateSuppressed };
        private static readonly string[] _defaultCheckedFilters = new[] { ServicesVSResources.SuppressionStateActive };
        private bool _defaultVisible = false;

        public override string Name => ColumnName;
        public override string DisplayName => ServicesVSResources.SuppressionStateColumnHeader;
        public override string HeaderName => ServicesVSResources.SuppressionStateColumnHeader;
        public override double MinWidth => 50.0;
        public override bool DefaultVisible => _defaultVisible;
        public override bool IsFilterable => true;
        public override IEnumerable<string> FilterPresets => _defaultFilters;
        
        public static void SetDefaultFilter(IWpfTableControl tableControl)
        {
            var suppressionStateColumn = tableControl.ColumnDefinitionManager.GetColumnDefinition(ColumnName) as SuppressionStateColumnDefinition;
            if (suppressionStateColumn != null)
            {
                tableControl.SetFilter(ColumnName, new ColumnHashSetFilter(suppressionStateColumn, excluded: ServicesVSResources.SuppressionStateSuppressed));
            }
        }
    }
}

