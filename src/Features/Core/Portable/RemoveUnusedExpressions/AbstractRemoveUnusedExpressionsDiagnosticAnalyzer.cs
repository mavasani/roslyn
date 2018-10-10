// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedExpressionsDiagnosticAnalyzer : AbstractCodeStyleDiagnosticAnalyzer
    {
        // IDE0055: "Expression value is never used"
        private static readonly DiagnosticDescriptor s_expressionValueIsUnusedRule = CreateDescriptorWithId(
            IDEDiagnosticIds.ExpressionValueIsUnusedDiagnosticId,
            new LocalizableResourceString(nameof(FeaturesResources.Expression_value_is_never_used), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            new LocalizableResourceString(nameof(FeaturesResources.Expression_value_is_never_used), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            defaultSeverity: DiagnosticSeverity.Info,
            isUnneccessary: true);

        // IDE0056: "Value assigned to '{0}' is never used"
        private static readonly DiagnosticDescriptor s_valueAssignedIsUnusedRule = CreateDescriptorWithId(
            IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId,
            new LocalizableResourceString(nameof(FeaturesResources.Value_assigned_to_symbol_is_never_used), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            new LocalizableResourceString(nameof(FeaturesResources.Value_assigned_to_0_is_never_used), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            defaultSeverity: DiagnosticSeverity.Info,
            isUnneccessary: true);

        private readonly bool _supportsDisard;

        protected AbstractRemoveUnusedExpressionsDiagnosticAnalyzer(bool supportsDiscard)
            : base(ImmutableArray.Create(s_expressionValueIsUnusedRule, s_valueAssignedIsUnusedRule))
        {
            _supportsDisard = supportsDiscard;
        }

        protected abstract Location GetDefinitionLocationToFade(IOperation unusedDefinition);

        public override bool OpenFileOnly(Workspace workspace) => false;

        // Our analysis is limited to unused expressions in a code block, hence is unaffected by changes outside the code block.
        // Hence, we can support incremental span based method body analysis.
        public override DiagnosticAnalyzerCategory GetAnalyzerCategory() => DiagnosticAnalyzerCategory.SemanticSpanAnalysis;

        protected sealed override void InitializeWorker(AnalysisContext context)
            => context.RegisterOperationBlockStartAction(AnalyzeOperationBlockStart);

        private void AnalyzeOperationBlockStart(OperationBlockStartAnalysisContext context)
        {
            if (HasSyntaxErrors())
            {
                return;
            }

            context.RegisterOperationAction(AnalyzeExpressionStatement, OperationKind.ExpressionStatement);
            context.RegisterOperationBlockEndAction(AnalyzeOperationBlockEnd);
            return;

            // Local Functions.
            bool HasSyntaxErrors()
            {
                foreach (var operationBlock in context.OperationBlocks)
                {
                    if (operationBlock.SemanticModel.GetSyntaxDiagnostics(operationBlock.Syntax.Span, context.CancellationToken).HasAnyErrors())
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void AnalyzeExpressionStatement(OperationAnalysisContext context)
        {
            var value = ((IExpressionStatementOperation)context.Operation).Operation;
            if (value.Type == null ||
                value.Type.SpecialType == SpecialType.System_Void ||
                value is IAssignmentOperation ||
                value is IIncrementOrDecrementOperation)
            {
                return;
            }

            var (preference, severity) = GetOption(value.Syntax.SyntaxTree, value.Language, context.Options, context.CancellationToken);
            if (preference == UnusedExpressionAssignmentPreference.None ||
                severity == ReportDiagnostic.Suppress)
            {
                return;
            }

            // IDE0055: "Expression value is never used"
            var diagnostic = DiagnosticHelper.Create(s_expressionValueIsUnusedRule,
                                                     value.Syntax.GetLocation(),
                                                     severity,
                                                     additionalLocations: null,
                                                     properties: null);
            context.ReportDiagnostic(diagnostic);
        }

        private void AnalyzeOperationBlockEnd(OperationBlockAnalysisContext context)
        {
            if (context.OperationBlocks.IsEmpty)
            {
                return;
            }

            var (preference, severity) = GetOption(context.OperationBlocks[0].Syntax.SyntaxTree, context.OperationBlocks[0].Language, context.Options, context.CancellationToken);
            if (preference == UnusedExpressionAssignmentPreference.None ||
                severity == ReportDiagnostic.Suppress)
            {
                return;
            }

            foreach (var operationBlock in context.OperationBlocks)
            {
                // First perform the fast, aggressive, imprecise operation-tree based reaching definitions analysis.
                // This analysis might flag some "used" definitions as "unused", but will not miss reporting any truly unused definitions.
                // This initial pass helps us reduce the number of methods for which we perform the slower second pass.
                var resultFromOperationBlockAnalysis = ReachingAndUnusedDefinitionsAnalyzer.AnalyzeAndGetUnusedDefinitions(operationBlock);
                if (!resultFromOperationBlockAnalysis.UnusedDefinitions.IsEmpty)
                {
                    // Now perform the slower, precise, CFG based reaching definitions dataflow analysis to identify the actual unused definitions.
                    var cfg = context.GetControlFlowGraph(operationBlock);
                    var resultFromFlowAnalysis = ReachingAndUnusedDefinitionsAnalyzer.AnalyzeAndGetUnusedDefinitions(cfg);

                    Debug.Assert(resultFromFlowAnalysis.UnusedDefinitions.Select(d => d.Symbol).ToImmutableHashSet()
                        .IsSubsetOf(resultFromOperationBlockAnalysis.UnusedDefinitions.Select(d => d.Symbol)));

                    foreach (var (unusedSymbol, unusedDefinition) in resultFromFlowAnalysis.UnusedDefinitions)
                    {
                        if (preference == UnusedExpressionAssignmentPreference.UnusedLocalVariable &&
                            unusedSymbol is ILocalSymbol localSymbol &&
                            !resultFromFlowAnalysis.ReferencedLocals.Contains(localSymbol))
                        {
                            continue;
                        }

                        // IDE0056: "Value assigned to '{0}' is never used"
                        var diagnostic = DiagnosticHelper.Create(s_valueAssignedIsUnusedRule,
                                                                 GetDefinitionLocationToFade(unusedDefinition),
                                                                 severity,
                                                                 additionalLocations: null,
                                                                 properties: null,
                                                                 unusedSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }

        private (UnusedExpressionAssignmentPreference preference, ReportDiagnostic severity) GetOption(
            SyntaxTree syntaxTree,
            string language,
            AnalyzerOptions analyzerOptions,
            CancellationToken cancellationToken)
        {
            var optionSet = analyzerOptions.GetDocumentOptionSetAsync(syntaxTree, cancellationToken).GetAwaiter().GetResult();
            if (optionSet == null)
            {
                return (UnusedExpressionAssignmentPreference.None, ReportDiagnostic.Suppress);
            }

            var option = optionSet.GetOption(CodeStyleOptions.UnusedExpressionAssignment, language);
            var preference = option.Value;
            if (!_supportsDisard && preference == UnusedExpressionAssignmentPreference.DiscardVariable)
            {
                preference = UnusedExpressionAssignmentPreference.UnusedLocalVariable; 
            }

            return (preference, option.Notification.Severity);
        }
    }
}
