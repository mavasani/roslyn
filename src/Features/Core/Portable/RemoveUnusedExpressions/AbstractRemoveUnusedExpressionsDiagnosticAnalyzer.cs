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
    using PropertiesMap = ImmutableDictionary<(UnusedExpressionAssignmentPreference preference, bool isUnusedLocalAssignment, bool isRemovableAssignment),
                                              ImmutableDictionary<string, string>>;

    internal abstract class AbstractRemoveUnusedExpressionsDiagnosticAnalyzer : AbstractCodeStyleDiagnosticAnalyzer
    {
        private const string UnusedExpressionPreferenceKey = nameof(UnusedExpressionPreferenceKey);
        private const string IsUnusedLocalAssignmentKey = nameof(IsUnusedLocalAssignmentKey);
        private const string IsRemovableAssignmentKey = nameof(IsRemovableAssignmentKey);

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

        private static readonly PropertiesMap s_propertiesMap = CreatePropertiesMap();
        private readonly bool _supportsDiscard;

        protected AbstractRemoveUnusedExpressionsDiagnosticAnalyzer(bool supportsDiscard)
            : base(ImmutableArray.Create(s_expressionValueIsUnusedRule, s_valueAssignedIsUnusedRule))
        {
            _supportsDiscard = supportsDiscard;
        }

        private static PropertiesMap CreatePropertiesMap()
        {
            var builder = ImmutableDictionary.CreateBuilder<(UnusedExpressionAssignmentPreference preference, bool isUnusedLocalAssignment, bool isRemovableAssignment),
                                                            ImmutableDictionary<string, string>>();
            AddEntries(UnusedExpressionAssignmentPreference.DiscardVariable);
            AddEntries(UnusedExpressionAssignmentPreference.UnusedLocalVariable);
            return builder.ToImmutable();

            void AddEntries(UnusedExpressionAssignmentPreference preference)
            {
                AddEntries2(preference, isUnusedLocalAssignment: true);
                AddEntries2(preference, isUnusedLocalAssignment: false);
            }

            void AddEntries2(UnusedExpressionAssignmentPreference preference, bool isUnusedLocalAssignment)
            {
                AddEntryCore(preference, isUnusedLocalAssignment, isRemovableAssignment: true);
                AddEntryCore(preference, isUnusedLocalAssignment, isRemovableAssignment: false);
            }

            void AddEntryCore(UnusedExpressionAssignmentPreference preference, bool isUnusedLocalAssignment, bool isRemovableAssignment)
            {
                var propertiesBuilder = ImmutableDictionary.CreateBuilder<string, string>();

                propertiesBuilder.Add(UnusedExpressionPreferenceKey, preference.ToString());
                if (isUnusedLocalAssignment)
                {
                    propertiesBuilder.Add(IsUnusedLocalAssignmentKey, string.Empty);
                }
                if (isRemovableAssignment)
                {
                    propertiesBuilder.Add(IsRemovableAssignmentKey, string.Empty);
                }

                builder.Add((preference, isUnusedLocalAssignment, isRemovableAssignment), propertiesBuilder.ToImmutable());
            }
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

            private BlockAnalyzer(
                Func<IOperation, Location> getDefinitionLocationToFade,
                UnusedExpressionAssignmentPreference preference,
                ReportDiagnostic severity)
            {
                Debug.Assert(preference != UnusedExpressionAssignmentPreference.None);
                Debug.Assert(severity != ReportDiagnostic.Suppress);

                _getDefinitionLocationToFade = getDefinitionLocationToFade;
                _preference = preference;
                _severity = severity;
            }

            public static void Analyze(
                OperationBlockStartAnalysisContext context,
                Func<IOperation, Location> getDefinitionLocationToFade,
                bool supportsDiscard)
            {
                if (HasSyntaxErrors())
                {
                    return;
                }

                // All operation blocks for a symbol belong to the same tree.
                var firstBlock = context.OperationBlocks[0];
                var (preference, severity) = GetOption(firstBlock.Syntax.SyntaxTree, firstBlock.Language, context.Options, supportsDiscard, context.CancellationToken);
                if (preference == UnusedExpressionAssignmentPreference.None)
                {
                    return;
                }

                Debug.Assert(severity != ReportDiagnostic.Suppress);

                var blockAnalyzer = new BlockAnalyzer(getDefinitionLocationToFade, preference, severity);
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
                var properties = s_propertiesMap[(_preference, isUnusedLocalAssignment: false, isRemovableAssignment: false)];
                var diagnostic = DiagnosticHelper.Create(s_expressionValueIsUnusedRule,
                                                         value.Syntax.GetLocation(),
                                                         _severity,
                                                         additionalLocations: null,
                                                         properties);
                context.ReportDiagnostic(diagnostic);
            }

            private void AnalyzeOperationBlockEnd(OperationBlockAnalysisContext context)
            {
                foreach (var operationBlock in context.OperationBlocks)
                {
                    // First perform the fast, aggressive, imprecise operation-tree based reaching definitions analysis.
                    // This analysis might flag some "used" definitions as "unused", but will not miss reporting any truly unused definitions.
                    // This initial pass helps us reduce the number of methods for which we perform the slower second pass.
                    var resultFromOperationBlockAnalysis = ReachingDefinitionsAnalyzer.AnalyzeAndGetUnusedDefinitions(operationBlock);
                    if (!resultFromOperationBlockAnalysis.UnusedDefinitions.IsEmpty)
                    {
                        // Now perform the slower, precise, CFG based reaching definitions dataflow analysis to identify the actual unused definitions.
                        var cfg = context.GetControlFlowGraph(operationBlock);
                        var resultFromFlowAnalysis = ReachingDefinitionsAnalyzer.AnalyzeAndGetUnusedDefinitions(cfg);

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

                    var isUnusedLocalAssignment = unusedSymbol is ILocalSymbol localSymbol &&
                                                  !resultFromFlowAnalysis.ReferencedLocals.Contains(localSymbol);
                    var isRemovableAssignment = IsRemovableAssignment(unusedDefinition);

                    if (isUnusedLocalAssignment &&
                        !isRemovableAssignment &&
                        _preference == UnusedExpressionAssignmentPreference.UnusedLocalVariable)
                    {
                        // Meets current user preference, skip reporting diagnostic.
                        return false;
                    }

                    properties = s_propertiesMap[(_preference, isUnusedLocalAssignment, isRemovableAssignment)];
                    return true;
                }

                bool IsRemovableAssignment(IOperation unusedDefinition)
                {
                    if (unusedDefinition.Parent is IAssignmentOperation assignment &&
                        assignment.Target == unusedDefinition)
                    {
                        if (assignment.Value.ConstantValue.HasValue)
                        {
                            return true;
                        }

                        switch (assignment.Value.Kind)
                        {
                            case OperationKind.ParameterReference:
                            case OperationKind.LocalReference:
                                return true;

                            case OperationKind.FieldReference:
                                var fieldReference = (IFieldReferenceOperation)assignment.Value;
                                return fieldReference.Instance == null || fieldReference.Instance.Kind == OperationKind.InstanceReference;
                        }
                    }

                    return false;
                }
            }

            private static (UnusedExpressionAssignmentPreference preference, ReportDiagnostic severity) GetOption(
                SyntaxTree syntaxTree,
                string language,
                AnalyzerOptions analyzerOptions,
                bool supportsDiscard,
                CancellationToken cancellationToken)
            {
                var optionSet = analyzerOptions.GetDocumentOptionSetAsync(syntaxTree, cancellationToken).GetAwaiter().GetResult();
                var option = optionSet?.GetOption(CodeStyleOptions.UnusedExpressionAssignment, language);
                var preference = option?.Value ?? UnusedExpressionAssignmentPreference.None;
                if (preference == UnusedExpressionAssignmentPreference.None ||
                    option.Notification.Severity == ReportDiagnostic.Suppress)
                {
                    return (UnusedExpressionAssignmentPreference.None, ReportDiagnostic.Suppress);
                }

                if (!supportsDiscard && preference == UnusedExpressionAssignmentPreference.DiscardVariable)
                {
                    preference = UnusedExpressionAssignmentPreference.UnusedLocalVariable;
                }

                return (preference, option.Notification.Severity);
            }
        }

        public static UnusedExpressionAssignmentPreference GetUnusedExpressionAssignmentPreference(Diagnostic diagnostic)
        {
            if (diagnostic.Properties != null &&
                diagnostic.Properties.TryGetValue(UnusedExpressionPreferenceKey, out var preference))
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

        public static bool GetIsUnusedLocalDiagnostic(Diagnostic diagnostic)
        {
            Debug.Assert(GetUnusedExpressionAssignmentPreference(diagnostic) != UnusedExpressionAssignmentPreference.None);
            return diagnostic.Properties.ContainsKey(IsUnusedLocalAssignmentKey);
        }

        public static bool GetIsRemovableAssignmentDiagnostic(Diagnostic diagnostic)
        {
            Debug.Assert(GetUnusedExpressionAssignmentPreference(diagnostic) != UnusedExpressionAssignmentPreference.None);
            return diagnostic.Properties.ContainsKey(IsRemovableAssignmentKey);
        }
    }
}
