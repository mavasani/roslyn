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

        /// <summary>
        /// Option to disable analyzer execution during live analysis.
        /// </summary>
        public static readonly Option<bool> DisableAnalyzers = new Option<bool>(
            nameof(ServiceFeatureOnOffOptions), nameof(DisableAnalyzers), defaultValue: false,
            storageLocations: new RoamingProfileStorageLocation($"Options.DisableAnalyzers"));

        /// <summary>
        /// Option to turn configure background analysis mode.
        /// </summary>
        public static readonly Option<AnalysisMode> BackgroundAnalysisMode = new Option<AnalysisMode>(
            nameof(ServiceFeatureOnOffOptions), nameof(BackgroundAnalysisMode), defaultValue: AnalysisMode.Default,
            storageLocations: new RoamingProfileStorageLocation($"Options.BackgroundAnalysisMode"));

        /// <summary>
        /// Enables forced <see cref="AnalysisMode.Lightweight"/> mode when low VM is detected to improve performance.
        /// </summary>
        public static bool LowMemoryForcedLightweightMode = false;

        public static bool IsLightweightAnalysisModeEnabled(Project project)
            => IsLightweightAnalysisModeEnabled(project.Solution.Options);

        public static bool IsLightweightAnalysisModeEnabled(OptionSet options)
            => options.GetOption(BackgroundAnalysisMode) == AnalysisMode.Lightweight ||
               LowMemoryForcedLightweightMode;

        public static bool IsBackgroundAnalysisDisabled(Project project)
            => IsBackgroundAnalysisDisabled(project.Solution.Options);

        public static bool IsBackgroundAnalysisDisabled(OptionSet options)
            => IsBackgroundAnalysisDisabledByUserOption(options) ||
               LowMemoryForcedLightweightMode;

        private static bool IsBackgroundAnalysisDisabledByUserOption(OptionSet options)
            => options.GetOption(BackgroundAnalysisMode) switch
            {
                AnalysisMode.Lightweight => true,
                AnalysisMode.PowerSave => true,
                _ => false
            };

        public static bool IsAnalyzerExecutionDisabled(Project project)
            => IsLightweightAnalysisModeEnabled(project) ||
               project.Solution.Options.GetOption(DisableAnalyzers);
    }
}
