﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;

#if CODE_STYLE
using Microsoft.CodeAnalysis.CSharp.Internal.CodeStyle;
#else
using Microsoft.CodeAnalysis.CSharp.CodeStyle;
using Microsoft.CodeAnalysis.Options;
#endif

namespace Microsoft.CodeAnalysis.CSharp.ConvertSwitchStatementToExpression
{
    using Constants = ConvertSwitchStatementToExpressionConstants;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed partial class ConvertSwitchStatementToExpressionDiagnosticAnalyzer : AbstractBuiltInCodeStyleDiagnosticAnalyzer
    {
        public ConvertSwitchStatementToExpressionDiagnosticAnalyzer()
            : base(IDEDiagnosticIds.ConvertSwitchStatementToExpressionDiagnosticId,
                CSharpCodeStyleOptions.PreferSwitchExpression,
                LanguageNames.CSharp,
                new LocalizableResourceString(nameof(CSharpAnalyzersResources.Convert_switch_statement_to_expression), CSharpAnalyzersResources.ResourceManager, typeof(CSharpAnalyzersResources)),
                new LocalizableResourceString(nameof(CSharpAnalyzersResources.Use_switch_expression), CSharpAnalyzersResources.ResourceManager, typeof(CSharpAnalyzersResources)))
        {
        }

        protected override void InitializeWorker(AnalysisContext context)
            => context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.SwitchStatement);

        private void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
        {
            var switchStatement = context.Node;
            if (switchStatement.ContainsDirectives)
            {
                return;
            }

            var syntaxTree = switchStatement.SyntaxTree;

            if (((CSharpParseOptions)syntaxTree.Options).LanguageVersion < LanguageVersion.CSharp8)
            {
                return;
            }

            var options = context.Options;
            var cancellationToken = context.CancellationToken;
            var optionSet = options.GetOptions(syntaxTree, cancellationToken);
            if (optionSet == null)
            {
                return;
            }

            var styleOption = optionSet.GetOption(CSharpCodeStyleOptions.PreferSwitchExpression);
            if (!styleOption.Value)
            {
                // User has disabled this feature.
                return;
            }

            if (switchStatement.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            var (nodeToGenerate, declaratorToRemoveOpt) =
                Analyzer.Analyze(
                    (SwitchStatementSyntax)switchStatement,
                    context.SemanticModel,
                    out var shouldRemoveNextStatement);
            if (nodeToGenerate == default)
            {
                return;
            }

            var additionalLocations = ArrayBuilder<Location>.GetInstance();
            additionalLocations.Add(switchStatement.GetLocation());
            additionalLocations.AddOptional(declaratorToRemoveOpt?.GetLocation());

            context.ReportDiagnostic(DiagnosticHelper.Create(Descriptor,
                // Report the diagnostic on the "switch" keyword.
                location: switchStatement.GetFirstToken().GetLocation(),
                effectiveSeverity: styleOption.Notification.Severity,
                additionalLocations: additionalLocations.ToArrayAndFree(),
                properties: ImmutableDictionary<string, string>.Empty
                    .Add(Constants.NodeToGenerateKey, ((int)nodeToGenerate).ToString(CultureInfo.InvariantCulture))
                    .Add(Constants.ShouldRemoveNextStatementKey, shouldRemoveNextStatement.ToString(CultureInfo.InvariantCulture))));
        }

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory()
            => DiagnosticAnalyzerCategory.SemanticSpanAnalysis;
    }
}
