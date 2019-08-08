// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    [Export(typeof(ITableControlEventProcessorProvider))]
    [DataSourceType(StandardTableDataSources.ErrorTableDataSource)]
    [DataSource(VisualStudioDiagnosticListTableWorkspaceEventListener.IdentifierString)]
    [Name(Name)]
    [Order(Before = "default")]
    internal partial class DiagnosticTableControlEventProcessorProvider : AbstractTableControlEventProcessorProvider<DiagnosticTableItem>
    {
        internal const string Name = "C#/VB Diagnostic Table Event Processor";
        private readonly VisualStudioDiagnosticListConfigurationStateService _configurationStateService;

        [ImportingConstructor]
        public DiagnosticTableControlEventProcessorProvider(
            IVisualStudioDiagnosticListConfigurationStateService configurationStateService)
        {
            _configurationStateService = (VisualStudioDiagnosticListConfigurationStateService)configurationStateService;
        }

        protected override EventProcessor CreateEventProcessor()
        {
            var eventProcessor = new ConfigurationStateEventProcessor(_configurationStateService);
            return new AggregateDiagnosticTableControlEventProcessor(additionalEventProcessors: eventProcessor);
        }
    }
}
