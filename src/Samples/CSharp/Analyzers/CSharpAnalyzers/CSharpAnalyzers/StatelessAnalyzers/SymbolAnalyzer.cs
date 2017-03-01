// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpAnalyzers
{
    /// <summary>
    /// Analyzer for reporting symbol diagnostics.
    /// It reports diagnostics for named type symbols that have members with the same name as the named type.
    /// </summary>
    /// <remarks>
    /// For analyzers that requires analyzing symbols or syntax nodes across compilation, see <see cref="CompilationStartedAnalyzer"/> and <see cref="CompilationStartedAnalyzerWithCompilationWideAnalysis"/>.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SymbolAnalyzer : DiagnosticAnalyzer
    {
        #region Descriptor fields
        public const string DiagnosticId = "CSharpAnalyzers";
        internal static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        internal static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, DiagnosticCategories.Stateless, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        #endregion

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSymbolAction(symbolContext =>
            {
                var symbolName = symbolContext.Symbol.Name;

                // Skip the immediate containing type, CS0542 already covers this case.
                var outerType = symbolContext.Symbol.ContainingType?.ContainingType;
                while (outerType != null)
                {
                    // Check if the current outer type has the same name as the given member.
                    if (symbolName.Equals(outerType.Name))
                    {
                        // For all such symbols, report a diagnostic.
                        var diagnostic = Diagnostic.Create(Rule, symbolContext.Symbol.Locations[0], symbolContext.Symbol.Name);
                        symbolContext.ReportDiagnostic(diagnostic);
                        return;
                    }

                    outerType = outerType.ContainingType;
                }                
            },
            SymbolKind.NamedType,
            SymbolKind.Method,
            SymbolKind.Field,
            SymbolKind.Event,
            SymbolKind.Property);
        }
    }
}
