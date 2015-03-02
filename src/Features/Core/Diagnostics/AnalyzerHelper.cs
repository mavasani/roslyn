// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis.ErrorReporting;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal static class AnalyzerHelper
    {
        private const string CSharpCompilerAnalyzerTypeName = "Microsoft.CodeAnalysis.Diagnostics.CSharp.CSharpCompilerDiagnosticAnalyzer";
        private const string VisualBasicCompilerAnalyzerTypeName = "Microsoft.CodeAnalysis.Diagnostics.VisualBasic.VisualBasicCompilerDiagnosticAnalyzer";

        public static bool IsBuiltInAnalyzer(this DiagnosticAnalyzer analyzer)
        {
            return analyzer is IBuiltInAnalyzer || analyzer is DocumentDiagnosticAnalyzer || analyzer is ProjectDiagnosticAnalyzer || analyzer.IsCompilerAnalyzer();
        }

        public static bool IsCompilerAnalyzer(this DiagnosticAnalyzer analyzer)
        {
            // TODO: find better way.
            var typeString = analyzer.GetType().ToString();
            if (typeString == CSharpCompilerAnalyzerTypeName)
            {
                return true;
            }

            if (typeString == VisualBasicCompilerAnalyzerTypeName)
            {
                return true;
            }

            return false;
        }

        public static ValueTuple<string, VersionStamp> GetUniqueId(this DiagnosticAnalyzer analyzer)
        {
            // Get the unique ID for given diagnostic analyzer.
            // note that we also put version stamp so that we can detect changed analyzer.
            var type = analyzer.GetType();
            return ValueTuple.Create(type.AssemblyQualifiedName, GetAnalyzerVersion(type.Assembly.Location));
        }

        internal static void OnAnalyzerException_NoTelemetryLogging(
            Exception e,
            DiagnosticAnalyzer analyzer,
            Diagnostic diagnostic,
            AbstractHostDiagnosticUpdateSource hostDiagnosticUpdateSource,
            Project projectOpt = null)
        {
            if (diagnostic != null)
            {
                hostDiagnosticUpdateSource?.ReportAnalyzerDiagnostic(analyzer, diagnostic, hostDiagnosticUpdateSource?.Workspace, projectOpt);
            }

            if (IsBuiltInAnalyzer(analyzer))
            {
                FatalError.ReportWithoutCrashUnlessCanceled(e);
            }
        }

        private static VersionStamp GetAnalyzerVersion(string path)
        {
            if (path == null || !File.Exists(path))
            {
                return VersionStamp.Default;
            }

            return VersionStamp.Create(File.GetLastWriteTimeUtc(path));
        }
    }
}