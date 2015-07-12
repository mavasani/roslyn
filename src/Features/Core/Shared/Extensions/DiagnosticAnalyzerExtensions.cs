// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Shared.Extensions
{
    internal static class DiagnosticAnalyzerExtensions
    {
        public static DiagnosticAnalyzerCategory GetDiagnosticAnalyzerCategory(this DiagnosticAnalyzer analyzer)
        {
            DiagnosticAnalyzerCategory category;
            if (analyzer is DocumentDiagnosticAnalyzer)
            {
                category = DiagnosticAnalyzerCategory.SyntaxAnalysis | DiagnosticAnalyzerCategory.SemanticDocumentAnalysis;
            }
            else if (analyzer is ProjectDiagnosticAnalyzer)
            {
                category = DiagnosticAnalyzerCategory.ProjectAnalysis;
            }
            else
            {
                var builtInAnalyzer = analyzer as IBuiltInAnalyzer;
                if (builtInAnalyzer != null)
                {
                    category = builtInAnalyzer.GetAnalyzerCategory();
                    Contract.ThrowIfTrue(((category & DiagnosticAnalyzerCategory.SemanticDocumentAnalysis) != 0) &&
                        ((category & DiagnosticAnalyzerCategory.SemanticSpanAnalysis) != 0));
                }
                else
                {
                    // It is not possible to know the category of non-built in analyzers,
                    // so return a worst-case categorization.
                    category = DiagnosticAnalyzerCategory.SyntaxAnalysis | DiagnosticAnalyzerCategory.SemanticDocumentAnalysis | DiagnosticAnalyzerCategory.ProjectAnalysis;
                }
            }

            return category;
        }

        public static bool SupportsSyntaxDiagnosticAnalysis(this DiagnosticAnalyzer analyzer)
        {
            var category = analyzer.GetDiagnosticAnalyzerCategory();
            return (category & DiagnosticAnalyzerCategory.SyntaxAnalysis) != 0;
        }

        public static bool SupportsSemanticDiagnosticAnalysis(this DiagnosticAnalyzer analyzer)
        {
            var category = analyzer.GetDiagnosticAnalyzerCategory();
            return (category & (DiagnosticAnalyzerCategory.SemanticSpanAnalysis | DiagnosticAnalyzerCategory.SemanticDocumentAnalysis)) != 0;
        }

        public static bool SupportsSemanticSpanDiagnosticAnalysis(this DiagnosticAnalyzer analyzer)
        {
            var category = analyzer.GetDiagnosticAnalyzerCategory();
            return (category & DiagnosticAnalyzerCategory.SemanticSpanAnalysis) != 0;
        }

        public static bool SupportsProjectDiagnosticAnalysis(this DiagnosticAnalyzer analyzer)
        {
            var category = analyzer.GetDiagnosticAnalyzerCategory();
            return (category & DiagnosticAnalyzerCategory.ProjectAnalysis) != 0;
        }
    }
}
