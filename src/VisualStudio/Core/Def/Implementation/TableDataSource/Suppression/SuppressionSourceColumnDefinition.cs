// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    /// <summary>
    /// Error list column for Suppression source information of a diagnostic.
    /// </summary>
    [Export(typeof(ITableColumnDefinition))]
    [Name(ColumnName)]
    internal class SuppressionSourceColumnDefinition : TableColumnDefinitionBase
    {
        public const string ColumnName = "suppressionsource";

        public override string Name => ColumnName;
        public override string DisplayName => ServicesVSResources.Suppression_Source;
        public override string HeaderName => ServicesVSResources.Suppression_Source;
        public override double MinWidth => 100.0;
        public override bool DefaultVisible => false;
        public override bool IsFilterable => true;
    }
}

