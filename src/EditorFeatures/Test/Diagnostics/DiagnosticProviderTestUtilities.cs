// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics.EngineV1;
using Microsoft.CodeAnalysis.Editor.UnitTests.Diagnostics;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Diagnostics;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.UnitTests.Diagnostics
{
    public static class DiagnosticProviderTestUtilities
    {
        private static IEnumerable<Diagnostic> GetDiagnostics(DiagnosticAnalyzer analyzerOpt, Document document, TextSpan span, Project project, bool getDocumentDiagnostics, bool getProjectDiagnostics, Action<Exception, DiagnosticAnalyzer, Diagnostic> onAnalyzerException, bool logAnalyzerExceptionAsDiagnostics)
        {
            var documentDiagnostics = SpecializedCollections.EmptyEnumerable<Diagnostic>();
            var projectDiagnostics = SpecializedCollections.EmptyEnumerable<Diagnostic>();

            // If no user diagnostic analyzer, then test compiler diagnostics.
            var analyzer = analyzerOpt ?? DiagnosticExtensions.GetCompilerDiagnosticAnalyzer(project.Language);

            // If the test is not configured with a custom onAnalyzerException handler AND has not requested exceptions to be handled and logged as diagnostics, then FailFast on exceptions.
            if (onAnalyzerException == null && !logAnalyzerExceptionAsDiagnostics)
            {
                onAnalyzerException = DiagnosticExtensions.FailFastOnAnalyzerException;
            }

            var exceptionDiagnosticsSource = new TestHostDiagnosticUpdateSource(project.Solution.Workspace);
            var diagnosticService = new DiagnosticAnalyzerService(project.Language, analyzer, exceptionDiagnosticsSource);

            if (getDocumentDiagnostics)
            {
                var tree = document.GetSyntaxTreeAsync().Result;
                var root = document.GetSyntaxRootAsync().Result;
                var semanticModel = document.GetSemanticModelAsync().Result;

                var dxs = diagnosticService.GetDiagnosticsAsync(project.Solution, project.Id, document.Id, CancellationToken.None).WaitAndGetResult(CancellationToken.None);
                documentDiagnostics = dxs.Select(d => d.ToDiagnostic(root.SyntaxTree)).Where(d => d.Location.SourceSpan.IntersectsWith(span));
            }

            if (getProjectDiagnostics)
            {
                var dxs = diagnosticService.GetProjectDiagnosticsForIdsAsync(project.Solution, project.Id, cancellationToken: CancellationToken.None).WaitAndGetResult(CancellationToken.None);
                projectDiagnostics = dxs.Select(d => d.ToDiagnostic(null));
            }

            var exceptionDiagnostics = exceptionDiagnosticsSource.TestOnly_GetReportedDiagnostics(analyzer).Select(d => d.ToDiagnostic(tree: null));

            return documentDiagnostics.Concat(projectDiagnostics).Concat(exceptionDiagnostics);
        }

        public static IEnumerable<Diagnostic> GetAllDiagnostics(DiagnosticAnalyzer providerOpt, Document document, TextSpan span, Action<Exception, DiagnosticAnalyzer, Diagnostic> onAnalyzerException = null, bool logAnalyzerExceptionAsDiagnostics = false)
        {
            return GetDiagnostics(providerOpt, document, span, document.Project, getDocumentDiagnostics: true, getProjectDiagnostics: true, onAnalyzerException: onAnalyzerException, logAnalyzerExceptionAsDiagnostics: logAnalyzerExceptionAsDiagnostics);
        }

        public static IEnumerable<Diagnostic> GetAllDiagnostics(DiagnosticAnalyzer providerOpt, Project project, Action<Exception, DiagnosticAnalyzer, Diagnostic> onAnalyzerException = null, bool logAnalyzerExceptionAsDiagnostics = false)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (var document in project.Documents)
            {
                var span = document.GetSyntaxRootAsync().Result.FullSpan;
                var documentDiagnostics = GetDocumentDiagnostics(providerOpt, document, span, onAnalyzerException, logAnalyzerExceptionAsDiagnostics);
                diagnostics.AddRange(documentDiagnostics);
            }

            var projectDiagnostics = GetProjectDiagnostics(providerOpt, project, onAnalyzerException, logAnalyzerExceptionAsDiagnostics);
            diagnostics.AddRange(projectDiagnostics);
            return diagnostics;
        }

        public static IEnumerable<Diagnostic> GetAllDiagnostics(DiagnosticAnalyzer providerOpt, Solution solution, Action<Exception, DiagnosticAnalyzer, Diagnostic> onAnalyzerException = null, bool logAnalyzerExceptionAsDiagnostics = false)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (var project in solution.Projects)
            {
                var projectDiagnostics = GetAllDiagnostics(providerOpt, project, onAnalyzerException, logAnalyzerExceptionAsDiagnostics);
                diagnostics.AddRange(projectDiagnostics);
            }

            return diagnostics;
        }

        public static IEnumerable<Diagnostic> GetDocumentDiagnostics(DiagnosticAnalyzer providerOpt, Document document, TextSpan span, Action<Exception, DiagnosticAnalyzer, Diagnostic> onAnalyzerException = null, bool logAnalyzerExceptionAsDiagnostics = false)
        {
            return GetDiagnostics(providerOpt, document, span, document.Project, getDocumentDiagnostics: true, getProjectDiagnostics: false, onAnalyzerException: onAnalyzerException, logAnalyzerExceptionAsDiagnostics: logAnalyzerExceptionAsDiagnostics);
        }

        public static IEnumerable<Diagnostic> GetProjectDiagnostics(DiagnosticAnalyzer providerOpt, Project project, Action<Exception, DiagnosticAnalyzer, Diagnostic> onAnalyzerException = null, bool logAnalyzerExceptionAsDiagnostics = false)
        {
            return GetDiagnostics(providerOpt, null, default(TextSpan), project, getDocumentDiagnostics: false, getProjectDiagnostics: true, onAnalyzerException: onAnalyzerException, logAnalyzerExceptionAsDiagnostics: logAnalyzerExceptionAsDiagnostics);
        }
    }
}
