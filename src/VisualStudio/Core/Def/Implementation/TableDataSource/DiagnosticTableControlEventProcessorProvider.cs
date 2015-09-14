// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.LanguageServices.Implementation.Suppression;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    [Export(typeof(ITableControlEventProcessorProvider))]
    [Export(typeof(IVisualStudioDiagnosticListCommandHandler))]
    [DataSourceType(StandardTableDataSources.ErrorTableDataSource)]
    [DataSource(VisualStudioDiagnosticListTable.IdentifierString)]
    [Name(Name)]
    [Order(Before = "default")]
    internal partial class DiagnosticTableControlEventProcessorProvider : AbstractTableControlEventProcessorProvider<DiagnosticData>, IVisualStudioDiagnosticListCommandHandler
    {
        internal const string Name = "C#/VB Diagnostic Table Event Processor";

        private readonly IVisualStudioBulkSuppressionService _suppressionService;
        private IServiceProvider _serviceProvider;
        private VisualStudioWorkspace _workspace;

        [ImportingConstructor]
        public DiagnosticTableControlEventProcessorProvider(IVisualStudioBulkSuppressionService suppressionService)
        {
            _suppressionService = suppressionService;
        }

        void IVisualStudioDiagnosticListCommandHandler.Initialize(IServiceProvider serviceProvider, VisualStudioWorkspace workspace)
        {
            _serviceProvider = serviceProvider;
            _workspace = workspace;
        }

        protected override EventProcessor CreateEventProcessor(IWpfTableControl tableControl)
        {
            if (_serviceProvider != null)
            {
                var suppressionStateEventProcessor = new SuppressionStateEventProcessor(_serviceProvider, _workspace, _suppressionService, tableControl);
                return new AggregateDiagnosticTableControlEventProcessor(tableControl, suppressionStateEventProcessor);
            }

            return base.CreateEventProcessor(tableControl);
        }
    }
}
