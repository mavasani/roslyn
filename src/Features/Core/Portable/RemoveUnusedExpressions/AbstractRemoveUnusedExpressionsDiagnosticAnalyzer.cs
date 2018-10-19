// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Shared.Extensions;

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

        // IDE0057: "Remove unused parameter '{0}'{1}"
        private static readonly DiagnosticDescriptor s_parameterIsUnusedRule = CreateDescriptorWithId(
            IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId,
            new LocalizableResourceString(nameof(FeaturesResources.Remove_unused_parameter), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            new LocalizableResourceString(nameof(FeaturesResources.Remove_unused_parameter_0_1), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            isUnneccessary: true);

        private static readonly PropertiesMap s_propertiesMap = CreatePropertiesMap();
        private readonly bool _supportsDiscard;

        protected AbstractRemoveUnusedExpressionsDiagnosticAnalyzer(bool supportsDiscard)
            : base(ImmutableArray.Create(s_expressionValueIsUnusedRule, s_valueAssignedIsUnusedRule, s_parameterIsUnusedRule))
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
            => context.RegisterCompilationStartAction(
                compilationContext => SymbolStartAnalyzer.CreateAndRegisterActions(compilationContext, GetDefinitionLocationToFade, _supportsDiscard));

        private sealed class SymbolStartAnalyzer
        {
            private readonly Func<IOperation, Location> _getDefinitionLocationToFade;
            private readonly bool _supportsDiscard;
            private readonly INamedTypeSymbol _eventArgsSymbol;
            private readonly ImmutableHashSet<INamedTypeSymbol> _attributeSetForMethodsToIgnore;
            private readonly ConcurrentDictionary<IParameterSymbol, bool> _unusedParameters;
            private readonly ConcurrentDictionary<IMethodSymbol, bool> _methodsUsedAsDelegates;

            public SymbolStartAnalyzer(
                Func<IOperation, Location> getDefinitionLocationToFade,
                bool supportsDiscard,
                INamedTypeSymbol eventArgsNamedType,
                ImmutableHashSet<INamedTypeSymbol> attributeSetForMethodsToIgnore)
            {
                _getDefinitionLocationToFade = getDefinitionLocationToFade;
                _supportsDiscard = supportsDiscard;
                _eventArgsSymbol = eventArgsNamedType;
                _attributeSetForMethodsToIgnore = attributeSetForMethodsToIgnore;
                _unusedParameters = new ConcurrentDictionary<IParameterSymbol, bool>();
                _methodsUsedAsDelegates = new ConcurrentDictionary<IMethodSymbol, bool>();
            }

            public static void CreateAndRegisterActions(
                CompilationStartAnalysisContext context,
                Func<IOperation, Location> getDefinitionLocationToFade,
                bool supportsDiscard)
            {
                var eventsArgSymbol = context.Compilation.GetTypeByMetadataName("System.EventArgs");

                // Ignore conditional methods (One conditional will often call another conditional method as its only use of a parameter)
                var conditionalAttributeSymbol = context.Compilation.ConditionalAttribute();

                // Ignore methods with special serialization attributes (All serialization methods need to take 'StreamingContext')
                var onDeserializingAttribute = context.Compilation.OnDeserializingAttribute();
                var onDeserializedAttribute = context.Compilation.OnDeserializedAttribute();
                var onSerializingAttribute = context.Compilation.OnSerializingAttribute();
                var onSerializedAttribute = context.Compilation.OnSerializedAttribute();
                var obsoleteAttribute = context.Compilation.ObsoleteAttribute();

                var attributeSetForMethodsToIgnore = ImmutableHashSet.Create(
                    conditionalAttributeSymbol,
                    onDeserializedAttribute,
                    onDeserializingAttribute,
                    onSerializedAttribute,
                    onSerializingAttribute,
                    obsoleteAttribute);

                var analyzer = new SymbolStartAnalyzer(getDefinitionLocationToFade, supportsDiscard, eventsArgSymbol, attributeSetForMethodsToIgnore);
                context.RegisterSymbolStartAction(analyzer.OnSymbolStart, SymbolKind.NamedType);
            }

            private void OnSymbolStart(SymbolStartAnalysisContext context)
            {
                context.RegisterOperationBlockStartAction(OnOperationBlock);
                context.RegisterOperationAction(OnMethodReference, OperationKind.MethodReference);
                context.RegisterSymbolEndAction(OnSymbolEnd);
            }

            private void OnOperationBlock(OperationBlockStartAnalysisContext context)
                => BlockAnalyzer.Analyze(context, _getDefinitionLocationToFade, _supportsDiscard, _unusedParameters);

            private void OnMethodReference(OperationAnalysisContext context)
            {
                var methodBinding = (IMethodReferenceOperation)context.Operation;
                _methodsUsedAsDelegates.GetOrAdd(methodBinding.Method.OriginalDefinition, true);
            }

            private void OnSymbolEnd(SymbolAnalysisContext context)
            {
                foreach (var parameterAndUsageKvp in _unusedParameters)
                {
                    var parameter = parameterAndUsageKvp.Key;
                    bool hasReference = parameterAndUsageKvp.Value;
                    if (!IsUnusedParameterCandidate(parameter))
                    {
                        continue;
                    }

                    var location = parameter.Locations[0];
                    var (preference, severity) = GetOption(location.SourceTree,
                        parameter.Language, context.Options, _supportsDiscard, context.CancellationToken);
                    if (preference == UnusedExpressionAssignmentPreference.None)
                    {
                        continue;
                    }

                    // IDE0057: "Remove unused parameter '{0}'{1}"
                    var arg1 = parameter.Name;
                    var arg2 = string.Empty;
                    if (parameter.ContainingSymbol.IsExternallyVisible())
                    {
                        arg2 += FeaturesResources.if_it_is_not_part_of_a_shipped_public_API;
                    }

                    if (hasReference)
                    {
                        arg2 += FeaturesResources.comma_its_initial_value_is_never_used;
                    }

                    var diagnostic = DiagnosticHelper.Create(s_parameterIsUnusedRule,
                                                             location,
                                                             severity,
                                                             additionalLocations: null,
                                                             properties: null,
                                                             arg1,
                                                             arg2);
                    context.ReportDiagnostic(diagnostic);
                }
            }

            private bool IsUnusedParameterCandidate(IParameterSymbol parameter)
            {
                // Ignore implicitly declared methods, extern methods, abstract methods,
                // virtual methods, overrides, interface implementations and accessors.
                if (!(parameter.ContainingSymbol is IMethodSymbol method) ||
                    method.IsImplicitlyDeclared ||
                    method.IsExtern ||
                    method.IsAbstract ||
                    method.IsVirtual ||
                    method.IsOverride ||
                    method.ExplicitOrImplicitInterfaceImplementations().Any() ||
                    method.IsAccessor())
                {
                    return false;
                }

                // Ignore event handler methods "Handler(object, MyEventArgs)"
                if (_eventArgsSymbol != null &&
                    method.Parameters.Length == 2 &&
                    method.Parameters[0].Type.SpecialType == SpecialType.System_Object &&
                    method.Parameters[1].Type.InheritsFromOrEquals(_eventArgsSymbol))
                {
                    return false;
                }

                // Ignore methods with any attributes in 'attributeSetForMethodsToIgnore'.
                if (method.GetAttributes().Any(a => a.AttributeClass != null && _attributeSetForMethodsToIgnore.Contains(a.AttributeClass)))
                {
                    return false;
                }

                // Ignore methods that were used as delegates
                if (_methodsUsedAsDelegates.ContainsKey(method))
                {
                    return false;
                }

                return true;
            }
        }

        private sealed class BlockAnalyzer
        {
            private readonly Func<IOperation, Location> _getDefinitionLocationToFade;
            private readonly UnusedExpressionAssignmentPreference _preference;
            private readonly ReportDiagnostic _severity;
            private readonly ConcurrentDictionary<IParameterSymbol, bool> _unusedParameters;

            private BlockAnalyzer(
                Func<IOperation, Location> getDefinitionLocationToFade,
                UnusedExpressionAssignmentPreference preference,
                ReportDiagnostic severity,
                ConcurrentDictionary<IParameterSymbol, bool> unusedParameters)
            {
                Debug.Assert(preference != UnusedExpressionAssignmentPreference.None);
                Debug.Assert(severity != ReportDiagnostic.Suppress);

                _getDefinitionLocationToFade = getDefinitionLocationToFade;
                _preference = preference;
                _severity = severity;
                _unusedParameters = unusedParameters;
            }

            public static void Analyze(
                OperationBlockStartAnalysisContext context,
                Func<IOperation, Location> getDefinitionLocationToFade,
                bool supportsDiscard,
                ConcurrentDictionary<IParameterSymbol, bool> unusedParameters)
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

                var blockAnalyzer = new BlockAnalyzer(getDefinitionLocationToFade, preference, severity, unusedParameters);
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
                    value.Type.SpecialType == SpecialType.System_Boolean ||
                    value.ConstantValue.HasValue ||
                    value is IAssignmentOperation ||
                    value is IIncrementOrDecrementOperation ||
                    value is IInvalidOperation)
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
                    var resultFromOperationBlockAnalysis = ReachingDefinitionsAnalyzer.AnalyzeAndGetUnusedDefinitions(operationBlock, context.OwningSymbol);
                    if (!resultFromOperationBlockAnalysis.DefinitionUsageMap.IsEmpty)
                    {
                        // Now perform the slower, precise, CFG based reaching definitions dataflow analysis to identify the actual unused definitions.
                        var cfg = context.GetControlFlowGraph(operationBlock);
                        var resultFromFlowAnalysis = ReachingDefinitionsAnalyzer.AnalyzeAndGetUnusedDefinitions(cfg, context.OwningSymbol);

                        Debug.Assert(resultFromFlowAnalysis.GetUnusedDefinitions().Select(d => d.Symbol).ToImmutableHashSet()
                            .IsSubsetOf(resultFromOperationBlockAnalysis.GetUnusedDefinitions().Select(d => d.Symbol)));

                        foreach (var (unusedSymbol, unusedDefinition) in resultFromFlowAnalysis.GetUnusedDefinitions())
                        {
                            if (unusedDefinition == null)
                            {
                                // This indicates the parameter definiton at entry block, which has no corresponding IOperation.
                                // We report _unusedParameters in SymbolStartAnalyzer.OnSymbolEnd, just add it to the map here.
                                var unusedParameter = (IParameterSymbol)unusedSymbol;
                                var hasReference = resultFromFlowAnalysis.ReferencedSymbols.Contains(unusedParameter);

                                // Only flag ref/out parameters if they are completely unreferenced.
                                if (!hasReference || unusedParameter.IsRefOrOut())
                                {
                                    _unusedParameters.GetOrAdd(unusedParameter, hasReference);
                                }

                                continue;
                            }

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
                    DefinitionUsageResult resultFromFlowAnalysis,
                    out ImmutableDictionary<string, string> properties)
                {
                    properties = null;

                    var isUnusedLocalAssignment = unusedSymbol is ILocalSymbol localSymbol &&
                                                  !resultFromFlowAnalysis.ReferencedSymbols.Contains(localSymbol);
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
