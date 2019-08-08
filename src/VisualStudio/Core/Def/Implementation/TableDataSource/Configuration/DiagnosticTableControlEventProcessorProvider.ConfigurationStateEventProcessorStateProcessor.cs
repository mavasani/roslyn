// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource.Configuration;
using Microsoft.VisualStudio.Shell.TableControl;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    internal partial class DiagnosticTableControlEventProcessorProvider
    {
        private partial class ConfigurationStateEventProcessor : EventProcessor
        {
            private readonly VisualStudioDiagnosticListConfigurationStateService _configurationStateService;

            public ConfigurationStateEventProcessor(VisualStudioDiagnosticListConfigurationStateService configurationStateService)
            {
                _configurationStateService = configurationStateService;
            }

            public override void PostprocessSelectionChanged(TableSelectionChangedEventArgs e)
            {
                // Update the configuration state information for the new error list selection.
                _configurationStateService.ProcessSelectionChanged(e);
            }
        }
    }
}
