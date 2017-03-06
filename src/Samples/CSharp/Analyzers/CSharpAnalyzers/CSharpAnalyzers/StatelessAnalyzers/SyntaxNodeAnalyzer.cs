// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpAnalyzers
{
    /// <summary>
    /// Analyzer for reporting syntax node diagnostics.
    /// It reports diagnostics for implicitly typed local variables, recommending explicit type specification.
    /// </summary>
    /// <remarks>
    /// For analyzers that requires analyzing symbols or syntax nodes across compilation, see <see cref="CompilationStartedAnalyzer"/> and <see cref="CompilationStartedAnalyzerWithCompilationWideAnalysis"/>.
    /// For analyzers that requires analyzing symbols or syntax nodes across a code block, see <see cref="CodeBlockStartedAnalyzer"/>.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SyntaxNodeAnalyzer : DiagnosticAnalyzer
    {
        #region Descriptor fields

        internal static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.SyntaxNodeAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.SyntaxNodeAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.SyntaxNodeAnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        internal static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticIds.SyntaxNodeAnalyzerRuleId, Title, MessageFormat, DiagnosticCategories.Stateless, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        #endregion

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(syntaxNodeContext =>
            {
                // Find implicitly typed variable declarations.
                // Do not flag implicitly typed declarations that declare more than one variables,
                // as the compiler already generates error CS0819 for those cases.
                var declaration = (VariableDeclarationSyntax)syntaxNodeContext.Node;
                if (!declaration.Type.IsVar || declaration.Variables.Count != 1)
                {
                    return;
                }

                // Do not flag variable declarations with error type or special System types, such as int, char, string, etc.
                var typeInfo = syntaxNodeContext.SemanticModel.GetTypeInfo(declaration.Type, syntaxNodeContext.CancellationToken);
                if (typeInfo.Type.TypeKind == TypeKind.Error || typeInfo.Type.SpecialType != SpecialType.None)
                {
                    return;
                }

                // Report a diagnostic.
                var variable = declaration.Variables[0];
                var diagnostic = Diagnostic.Create(Rule, variable.GetLocation(), variable.Identifier.ValueText);
                syntaxNodeContext.ReportDiagnostic(diagnostic);
            }, SyntaxKind.VariableDeclaration);
        }
    }
}



