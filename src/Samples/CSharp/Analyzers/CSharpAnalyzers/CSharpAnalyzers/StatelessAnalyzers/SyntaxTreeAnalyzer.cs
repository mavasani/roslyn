// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpAnalyzers
{
    /// <summary>
    /// Analyzer for reporting syntax tree diagnostics.
    /// It reports diagnostics for all source files which have documentation comment diagnostics turned off.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SyntaxTreeAnalyzer : DiagnosticAnalyzer
    {
        #region Descriptor fields

        internal static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.SyntaxTreeAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.SyntaxTreeAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.SyntaxTreeAnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        
        internal static DiagnosticDescriptor Rule = new DiagnosticDescriptor("CSharpAnalyzer", Title, MessageFormat, DiagnosticCategories.Stateless, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        #endregion

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxTreeAction(syntaxTreeContext =>
            {
                // Iterate through all statements in the tree.
                var root = syntaxTreeContext.Tree.GetRoot(syntaxTreeContext.CancellationToken);
                foreach (var statement in root.DescendantNodes().OfType<StatementSyntax>())
                {
                    // Skip analyzing block statements.
                    if (statement is BlockSyntax)
                    {
                        continue;
                    }

                    // Report issue for all statements that are nested within a statement, but not inside a block statement.
                    if (statement.Parent is StatementSyntax && !(statement.Parent is BlockSyntax))
                    {
                        var diagnostic = Diagnostic.Create(Rule, statement.GetFirstToken().GetLocation());
                        syntaxTreeContext.ReportDiagnostic(diagnostic);
                    }
                }
            });
        }
    }
}
