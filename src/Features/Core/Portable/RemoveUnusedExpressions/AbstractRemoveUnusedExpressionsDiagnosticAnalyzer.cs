// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
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
        private const string UnusedExpressionPreferenceKey = nameof(UnusedExpressionPreferenceKey);
        private const string OwningSymbolKey = nameof(OwningSymbolKey);
        private const string IsUnusedLocalKey = nameof(IsUnusedLocalKey);
        private const string IsConstantValueAssignedKey = nameof(IsConstantValueAssignedKey);

        // IDE0055: "Expression value is never used"
        private static readonly DiagnosticDescriptor s_expressionValueIsUnusedRule = CreateDescriptorWithId(
            IDEDiagnosticIds.ExpressionValueIsUnusedDiagnosticId,
            new LocalizableResourceString(nameof(FeaturesResources.Expression_value_is_never_used), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            new LocalizableResourceString(nameof(FeaturesResources.Expression_value_is_never_used), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            isUnneccessary: true);

        // IDE0056: "Value assigned to '{0}' is never used"
        private static readonly DiagnosticDescriptor s_valueAssignedIsUnusedRule = CreateDescriptorWithId(
            IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId,
            new LocalizableResourceString(nameof(FeaturesResources.Value_assigned_to_symbol_is_never_used), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            new LocalizableResourceString(nameof(FeaturesResources.Value_assigned_to_0_is_never_used), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            isUnneccessary: true);

        private readonly bool _supportsDiscard;

        protected AbstractRemoveUnusedExpressionsDiagnosticAnalyzer(bool supportsDiscard)
            : base(ImmutableArray.Create(s_expressionValueIsUnusedRule, s_valueAssignedIsUnusedRule))
        {
            _supportsDiscard = supportsDiscard;
        }
        
        protected abstract Location GetDefinitionLocationToFade(IOperation unusedDefinition);

        public override bool OpenFileOnly(Workspace workspace) => false;

        // Our analysis is limited to unused expressions in a code block, hence is unaffected by changes outside the code block.
        // Hence, we can support incremental span based method body analysis.
        public override DiagnosticAnalyzerCategory GetAnalyzerCategory() => DiagnosticAnalyzerCategory.SemanticSpanAnalysis;

        protected sealed override void InitializeWorker(AnalysisContext context)
            => context.RegisterOperationBlockStartAction(
                operationBlockStartContext => BlockAnalyzer.Analyze(operationBlockStartContext, GetDefinitionLocationToFade, _supportsDiscard));

        private sealed class BlockAnalyzer
        {
            private readonly Func<IOperation, Location> _getDefinitionLocationToFade;
            private readonly UnusedExpressionAssignmentPreference _preference;
            private readonly ReportDiagnostic _severity;
            private readonly ImmutableDictionary<string, string> _properties;

            private BlockAnalyzer(
                Func<IOperation, Location> getDefinitionLocationToFade,
                UnusedExpressionAssignmentPreference preference,
                ReportDiagnostic severity,
                ImmutableDictionary<string, string> properties)
            {
                _getDefinitionLocationToFade = getDefinitionLocationToFade;
                _preference = preference;
                _severity = severity;
                _properties = properties;
            }

            public static void Analyze(OperationBlockStartAnalysisContext context, Func<IOperation, Location> getDefinitionLocationToFade, bool supportsDiscard)
            {
                if (HasSyntaxErrors())
                {
                    return;
                }

                // All operation blocks for a symbol should belong to the same tree.
                var firstBlock = context.OperationBlocks[0];
                var (preference, severity, properties) = GetOption(firstBlock.Syntax.SyntaxTree,
                    firstBlock.Language, context.Options, context.OwningSymbol, supportsDiscard, context.CancellationToken);
                if (preference == UnusedExpressionAssignmentPreference.None)
                {
                    return;
                }

                Debug.Assert(severity != ReportDiagnostic.Suppress);

                var blockAnalyzer = new BlockAnalyzer(getDefinitionLocationToFade, preference, severity, properties);
                context.RegisterOperationAction(blockAnalyzer.AnalyzeExpressionStatement, OperationKind.ExpressionStatement);
                context.RegisterOperationBlockEndAction(blockAnalyzer.AnalyzeOperationBlockEnd);

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

                // IDE0055: "Expression value is never used"
                var diagnostic = DiagnosticHelper.Create(s_expressionValueIsUnusedRule,
                                                         value.Syntax.GetLocation(),
                                                         _severity,
                                                         additionalLocations: null,
                                                         _properties);
                context.ReportDiagnostic(diagnostic);
            }

            private void AnalyzeOperationBlockEnd(OperationBlockAnalysisContext context)
            {
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
                            if (ShouldReportDiagnostic(unusedSymbol, unusedDefinition, resultFromFlowAnalysis, out var properties))
                            {
                                // IDE0056: "Value assigned to '{0}' is never used"
                                var diagnostic = DiagnosticHelper.Create(s_valueAssignedIsUnusedRule,
                                                                         _getDefinitionLocationToFade(unusedDefinition),
                                                                         _severity,
                                                                         additionalLocations: null,
                                                                         properties,
                                                                         unusedSymbol.Name);
                                context.ReportDiagnostic(diagnostic);
                            }
                        }
                    }
                }

                return;

                // Local functions.
                bool ShouldReportDiagnostic(
                    ISymbol unusedSymbol,
                    IOperation unusedDefinition,
                    UnusedDefinitionsResult resultFromFlowAnalysis,
                    out ImmutableDictionary<string, string> properties)
                {
                    properties = null;

                    var isUnusedLocal = unusedSymbol is ILocalSymbol localSymbol
                        && !resultFromFlowAnalysis.ReferencedLocals.Contains(localSymbol);
                    var isConstantValueAssigned = unusedDefinition.Parent is IAssignmentOperation assignment
                        && assignment.Value.ConstantValue.HasValue
                        && assignment.Target == unusedDefinition;

                    if (isUnusedLocal
                        && !isConstantValueAssigned
                        && _preference == UnusedExpressionAssignmentPreference.UnusedLocalVariable)
                    {
                        // Meets current user preference, skip reporting diagnostic.
                        return false;
                    }

                    if (!isUnusedLocal && !isConstantValueAssigned)
                    {
                        properties = _properties;
                        return true;
                    }

                    var builder = ImmutableDictionary.CreateBuilder<string, string>();
                    builder.AddRange(_properties);
                    if (isUnusedLocal)
                    {
                        builder.Add(IsUnusedLocalKey, string.Empty);
                    }

                    if (isConstantValueAssigned)
                    {
                        builder.Add(IsConstantValueAssignedKey, string.Empty);
                    }

                    properties = builder.ToImmutable();
                    return true;
                }
            }

            private static (UnusedExpressionAssignmentPreference preference, ReportDiagnostic severity, ImmutableDictionary<string, string> properties) GetOption(
                SyntaxTree syntaxTree,
                string language,
                AnalyzerOptions analyzerOptions,
                ISymbol owningSymbol,
                bool supportsDiscard,
                CancellationToken cancellationToken)
            {
                var optionSet = analyzerOptions.GetDocumentOptionSetAsync(syntaxTree, cancellationToken).GetAwaiter().GetResult();
                var option = optionSet?.GetOption(CodeStyleOptions.UnusedExpressionAssignment, language);
                var preference = option?.Value ?? UnusedExpressionAssignmentPreference.None;
                if (preference == UnusedExpressionAssignmentPreference.None ||
                    option.Notification.Severity == ReportDiagnostic.Suppress)
                {
                    return (UnusedExpressionAssignmentPreference.None, ReportDiagnostic.Suppress, null);
                }

                if (!supportsDiscard && preference == UnusedExpressionAssignmentPreference.DiscardVariable)
                {
                    preference = UnusedExpressionAssignmentPreference.UnusedLocalVariable;
                }

                var preferenceStr = preference == UnusedExpressionAssignmentPreference.DiscardVariable
                    ? nameof(UnusedExpressionAssignmentPreference.DiscardVariable)
                    : nameof(UnusedExpressionAssignmentPreference.UnusedLocalVariable);

                var propertiesBuilder = ImmutableDictionary.CreateBuilder<string, string>();
                propertiesBuilder.Add(UnusedExpressionPreferenceKey, preferenceStr);
                propertiesBuilder.Add(OwningSymbolKey, owningSymbol.ToDisplayString());

                return (preference, option.Notification.Severity, propertiesBuilder.ToImmutable());
            }
        }
        
        public static UnusedExpressionAssignmentPreference GetUnusedExpressionAssignmentPreference(Diagnostic diagnostic)
        {
            if (diagnostic.Properties != null &&
                diagnostic.Properties.TryGetValue(UnusedExpressionPreferenceKey, out var preference) &&
                diagnostic.Properties.ContainsKey(OwningSymbolKey))
            {
                switch (preference)
                {
                    case nameof(UnusedExpressionAssignmentPreference.DiscardVariable):
                        return UnusedExpressionAssignmentPreference.DiscardVariable;

                    case nameof(UnusedExpressionAssignmentPreference.UnusedLocalVariable):
                        return UnusedExpressionAssignmentPreference.UnusedLocalVariable;
                }
            }

            return UnusedExpressionAssignmentPreference.None;
        }

        public static string GetOwningMemberSymbolName(Diagnostic diagnostic)
        {
            Debug.Assert(GetUnusedExpressionAssignmentPreference(diagnostic) != UnusedExpressionAssignmentPreference.None);
            return diagnostic.Properties[OwningSymbolKey];
        }

        public static (bool isUnusedLocal, bool isConstantValueAssigned) GetAdditionalPropertiesForDiagnostic(Diagnostic diagnostic)
        {
            Debug.Assert(GetUnusedExpressionAssignmentPreference(diagnostic) != UnusedExpressionAssignmentPreference.None);
            return (isUnusedLocal: diagnostic.Properties.ContainsKey(IsUnusedLocalKey),
                    isConstantValueAssigned: diagnostic.Properties.ContainsKey(IsConstantValueAssignedKey));
        }
    }
}
