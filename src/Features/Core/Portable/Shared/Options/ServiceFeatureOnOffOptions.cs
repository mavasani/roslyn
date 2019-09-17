// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.Shared.Options
{
    internal static class ServiceFeatureOnOffOptions
    {
        private const bool CSharpClosedFileDiagnosticsEnabledByDefault = false;
        private const bool DefaultClosedFileDiagnosticsEnabledByDefault = true;

        /// <summary>
        /// this option is solely for performance. don't confused by option name. 
        /// this option doesn't mean we will show all diagnostics that belong to opened files when turned off,
        /// rather it means we will only show diagnostics that are cheap to calculate for small scope such as opened files.
        /// </summary>
        public static readonly PerLanguageOption<bool?> ClosedFileDiagnostic = new PerLanguageOption<bool?>(
            "ServiceFeaturesOnOff", "Closed File Diagnostic", defaultValue: null,
            storageLocations: new RoamingProfileStorageLocation("TextEditor.%LANGUAGE%.Specific.Closed File Diagnostic"));

        public static bool IsClosedFileDiagnosticsEnabled(Project project)
        {
            return IsClosedFileDiagnosticsEnabled(project.Solution.Options, project.Language);
        }

        public static bool IsClosedFileDiagnosticsEnabled(OptionSet options, string language)
        {
            var option = options.GetOption(ClosedFileDiagnostic, language);
            if (!option.HasValue)
            {
                return language == LanguageNames.CSharp ?
                    CSharpClosedFileDiagnosticsEnabledByDefault :
                    DefaultClosedFileDiagnosticsEnabledByDefault;
            }

            return option.Value;
        }

        public static readonly PerLanguageOption<bool> PowerSaveMode = new PerLanguageOption<bool>(
            nameof(ServiceFeatureOnOffOptions), nameof(PowerSaveMode), defaultValue: false,
            storageLocations: new RoamingProfileStorageLocation($"TextEditor.%LANGUAGE%.Specific.PowerSaveMode"));

        public static readonly PerLanguageOption<bool> PowerSaveMode_DisableAnalyzerExecution = new PerLanguageOption<bool>(
            nameof(ServiceFeatureOnOffOptions), nameof(PowerSaveMode_DisableAnalyzerExecution), defaultValue: true,
            storageLocations: new RoamingProfileStorageLocation($"TextEditor.%LANGUAGE%.Specific.PowerSaveMode_DisableAnalyzerExecution"));

        public static readonly PerLanguageOption<bool> PowerSaveMode_DisableSuggestUsingsFromNuGetPackagesAndFrameworkAssemblies = new PerLanguageOption<bool>(
            nameof(ServiceFeatureOnOffOptions), nameof(PowerSaveMode_DisableSuggestUsingsFromNuGetPackagesAndFrameworkAssemblies), defaultValue: true,
            storageLocations: new RoamingProfileStorageLocation($"TextEditor.%LANGUAGE%.Specific.PowerSaveMode_DisableSuggestUsingsFromNuGetPackagesAndFrameworkAssemblies"));

        public static bool IsAnalyzerExecutionDisabled(OptionSet options, string languageName)
            => options.GetOption(PowerSaveMode, languageName) &&
               options.GetOption(PowerSaveMode_DisableAnalyzerExecution, languageName);

        public static bool IsSuggestUsingsFromNuGetPackagesAndFrameworkAssembliesDisabled(OptionSet options, string languageName)
            => options.GetOption(PowerSaveMode, languageName) &&
               options.GetOption(PowerSaveMode_DisableSuggestUsingsFromNuGetPackagesAndFrameworkAssemblies, languageName);
    }
}
