// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Design;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.LanguageServices.Implementation.Suppression;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.TableControl;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    internal partial class DiagnosticTableControlEventProcessorProvider : AbstractTableControlEventProcessorProvider<DiagnosticData>
    {
        private partial class SuppressionStateEventProcessor : EventProcessor
        {
            private readonly DiagnosticTableControlSuppressionStateService _suppressionStateService;

            public SuppressionStateEventProcessor(DiagnosticTableControlSuppressionStateService suppressionStateService)
            {
                _suppressionStateService = suppressionStateService;
            }

            public override void PostprocessSelectionChanged(TableSelectionChangedEventArgs e)
            {
                _suppressionStateService.ProcessSelectionChanged(e);
            }
        }
    }
}
