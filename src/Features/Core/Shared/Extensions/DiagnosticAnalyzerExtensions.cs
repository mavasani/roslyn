using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics.EngineV1;
using Roslyn.Utilities;
using System;

namespace Microsoft.CodeAnalysis.Shared.Extensions
{
    internal static class DiagnosticAnalyzerExtensions
    {
        public static DiagnosticAnalyzerCategory GetDiagnosticAnalyzerCategory(this DiagnosticAnalyzer analyzer)
        {
            var category = DiagnosticAnalyzerCategory.None;

            if (analyzer is DocumentDiagnosticAnalyzer)
            {
                category |= DiagnosticAnalyzerCategory.SyntaxAnalysis | DiagnosticAnalyzerCategory.SemanticDocumentAnalysis;
            }
            else if (analyzer is ProjectDiagnosticAnalyzer)
            {
                category |= DiagnosticAnalyzerCategory.ProjectAnalysis;
            }
            else
            {
                var builtInAnalyzer = analyzer as IBuiltInAnalyzer;
                if (builtInAnalyzer != null)
                {
                    category = builtInAnalyzer.GetAnalyzerCategory();
                }
                else
                {
                    // It is not possible to know the categorization for a public analyzer,
                    // so return a worst-case categorization.
                    category = (DiagnosticAnalyzerCategory.SyntaxAnalysis | DiagnosticAnalyzerCategory.SemanticDocumentAnalysis | DiagnosticAnalyzerCategory.ProjectAnalysis);
                }
            }

            return category;
        }

        private static bool HasSemanticDocumentActions(AnalyzerActionCounts analyzerActions)
        {
            return analyzerActions.SymbolActionsCount > 0 ||
                analyzerActions.SyntaxNodeActionsCount > 0 ||
                analyzerActions.SemanticModelActionsCount > 0 ||
                analyzerActions.CodeBlockActionsCount > 0 ||
                analyzerActions.CodeBlockStartActionsCount > 0;
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

        public static bool SupportsSpanBasedSemanticDiagnosticAnalysis(this DiagnosticAnalyzer analyzer)
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
