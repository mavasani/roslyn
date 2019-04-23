// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Analyzer.Utilities;
using Analyzer.Utilities.PooledObjects;
using Microsoft.CodeAnalysis.CodeQuality;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.DisposeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.PointsToAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.DisposeAnalysis
{
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    internal sealed class DisposableFieldsShouldBeDisposedDiagnosticAnalyzer
        : AbstractCodeQualityDiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor s_disposableFieldsShouldBeDisposedRule = CreateDescriptor(
            IDEDiagnosticIds.DisposableFieldsShouldBeDisposedDiagnosticId,
            new LocalizableResourceString(nameof(FeaturesResources.Disposable_fields_should_be_disposed), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            new LocalizableResourceString(nameof(FeaturesResources.Type_0_contains_field_1_that_is_of_IDisposable_type_2_but_it_is_never_disposed_Change_the_Dispose_method_on_0_to_call_Close_or_Dispose_on_this_field), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            isUnneccessary: false);

        public DisposableFieldsShouldBeDisposedDiagnosticAnalyzer()
            : base(ImmutableArray.Create(s_disposableFieldsShouldBeDisposedRule), GeneratedCodeAnalysisFlags.None)
        {
        }

        public override bool OpenFileOnly(Workspace workspace) => false;

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory() => DiagnosticAnalyzerCategory.SemanticDocumentAnalysis;

        protected override void InitializeWorker(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(compilationContext =>
            {
                if (!DisposeAnalysisHelper.TryGetOrCreate(compilationContext.Compilation, out var disposeAnalysisHelper))
                {
                    return;
                }

                compilationContext.RegisterSymbolStartAction(
                    symbolStartContext => SymbolAnalyzer.OnSymbolStart(symbolStartContext, disposeAnalysisHelper),
                    SymbolKind.NamedType);
            });
        }

        private sealed class SymbolAnalyzer
        {
            private readonly ConcurrentDictionary<IFieldSymbol, /*disposed*/bool> _fieldDisposeValueMap;
            private readonly DisposeAnalysisHelper _disposeAnalysisHelper;

            public SymbolAnalyzer(DisposeAnalysisHelper disposeAnalysisHelper)
            {
                _disposeAnalysisHelper = disposeAnalysisHelper;
                _fieldDisposeValueMap = new ConcurrentDictionary<IFieldSymbol, bool>();
            }

            public static void OnSymbolStart(SymbolStartAnalysisContext symbolStartContext, DisposeAnalysisHelper disposeAnalysisHelper)
            {
                var analyzer = new SymbolAnalyzer(disposeAnalysisHelper);
                symbolStartContext.RegisterOperationBlockStartAction(analyzer.OnOperationBlockStart);
                symbolStartContext.RegisterSymbolEndAction(analyzer.OnSymbolEnd);
            }

            private void AddOrUpdateFieldDisposedValue(IFieldSymbol field, bool disposed)
            {
                Debug.Assert(!field.IsStatic);
                Debug.Assert(field.Type.IsDisposable(_disposeAnalysisHelper.IDisposable));

                _fieldDisposeValueMap.AddOrUpdate(field,
                    addValue: disposed,
                    updateValueFactory: (f, currentValue) => currentValue || disposed);
            }

            private void OnSymbolEnd(SymbolAnalysisContext symbolEndContext)
            {
                foreach (var kvp in _fieldDisposeValueMap)
                {
                    IFieldSymbol field = kvp.Key;
                    bool disposed = kvp.Value;
                    if (!disposed)
                    {
                        // '{0}' contains field '{1}' that is of IDisposable type '{2}', but it is never disposed. Change the Dispose method on '{0}' to call Close or Dispose on this field.
                        var arg1 = field.ContainingType.Name;
                        var arg2 = field.Name;
                        var arg3 = field.Type.Name;
                        var diagnostic = Diagnostic.Create(s_disposableFieldsShouldBeDisposedRule, field.Locations[0], arg1, arg2, arg3);
                        symbolEndContext.ReportDiagnostic(diagnostic);
                    }
                }
            }

            private void OnOperationBlockStart(OperationBlockStartAnalysisContext operationBlockStartContext)
            {
                switch (operationBlockStartContext.OwningSymbol)
                {
                    case IFieldSymbol _:
                        if (operationBlockStartContext.OperationBlocks.Length == 1 &&
                            operationBlockStartContext.OperationBlocks[0] is IFieldInitializerOperation fieldInitializer)
                        {
                            foreach (var field in fieldInitializer.InitializedFields)
                            {
                                if (!field.IsStatic &&
                                    _disposeAnalysisHelper.GetDisposableFields(field.ContainingType).Contains(field))
                                {
                                    AddOrUpdateFieldDisposedValue(field, disposed: false);
                                }
                            }
                        }

                        break;

                    case IMethodSymbol containingMethod:
                        OnMethodOperationBlockStart(operationBlockStartContext, containingMethod);
                        break;
                }
            }

            private void OnMethodOperationBlockStart(OperationBlockStartAnalysisContext operationBlockStartContext, IMethodSymbol containingMethod)
            {
                if (_disposeAnalysisHelper.HasAnyDisposableCreationDescendant(operationBlockStartContext.OperationBlocks, containingMethod))
                {
                    if (_disposeAnalysisHelper.TryGetOrComputeResult(operationBlockStartContext.OperationBlocks,
                        containingMethod, operationBlockStartContext.Options, s_disposableFieldsShouldBeDisposedRule, trackInstanceFields: false,
                        trackExceptionPaths: false, operationBlockStartContext.CancellationToken,
                        out var disposeAnalysisResult, out var pointsToAnalysisResult))
                    {
                        Debug.Assert(disposeAnalysisResult != null);
                        Debug.Assert(pointsToAnalysisResult != null);

                        operationBlockStartContext.RegisterOperationAction(operationContext =>
                        {
                            var fieldReference = (IFieldReferenceOperation)operationContext.Operation;
                            var field = fieldReference.Field;

                            // Only track instance fields on the current instance.
                            if (field.IsStatic || fieldReference.Instance?.Kind != OperationKind.InstanceReference)
                            {
                                return;
                            }

                            // Check if this is a Disposable field that is not currently being tracked.
                            if (_fieldDisposeValueMap.ContainsKey(field) ||
                                    !_disposeAnalysisHelper.GetDisposableFields(field.ContainingType).Contains(field))
                            {
                                return;
                            }

                            // We have a field reference for a disposable field.
                            // Check if it is being assigned a locally created disposable object.
                            if (fieldReference.Parent is ISimpleAssignmentOperation simpleAssignmentOperation &&
                                    simpleAssignmentOperation.Target == fieldReference)
                            {
                                PointsToAbstractValue assignedPointsToValue = pointsToAnalysisResult[simpleAssignmentOperation.Value.Kind, simpleAssignmentOperation.Value.Syntax];
                                foreach (var location in assignedPointsToValue.Locations)
                                {
                                    if (_disposeAnalysisHelper.IsDisposableCreationOrDisposeOwnershipTransfer(location, containingMethod))
                                    {
                                        AddOrUpdateFieldDisposedValue(field, disposed: false);
                                        break;
                                    }
                                }
                            }
                        },
                        OperationKind.FieldReference);
                    }
                }

                // Mark fields disposed in Dispose method(s).
                if (containingMethod.GetDisposeMethodKind(_disposeAnalysisHelper.IDisposable, _disposeAnalysisHelper.Task) != DisposeMethodKind.None)
                {
                    var disposableFields = _disposeAnalysisHelper.GetDisposableFields(containingMethod.ContainingType);
                    if (!disposableFields.IsEmpty)
                    {
                        if (_disposeAnalysisHelper.TryGetOrComputeResult(operationBlockStartContext.OperationBlocks, containingMethod,
                            operationBlockStartContext.Options, s_disposableFieldsShouldBeDisposedRule, trackInstanceFields: true, trackExceptionPaths: false, cancellationToken: operationBlockStartContext.CancellationToken,
                            disposeAnalysisResult: out var disposeAnalysisResult, pointsToAnalysisResult: out var pointsToAnalysisResult))
                        {
                            BasicBlock exitBlock = disposeAnalysisResult.ControlFlowGraph.GetExit();
                            foreach (var fieldWithPointsToValue in disposeAnalysisResult.TrackedInstanceFieldPointsToMap)
                            {
                                IFieldSymbol field = fieldWithPointsToValue.Key;
                                PointsToAbstractValue pointsToValue = fieldWithPointsToValue.Value;

                                Debug.Assert(field.Type.IsDisposable(_disposeAnalysisHelper.IDisposable));
                                ImmutableDictionary<AbstractLocation, DisposeAbstractValue> disposeDataAtExit = disposeAnalysisResult.ExitBlockOutput.Data;
                                var disposed = false;
                                foreach (var location in pointsToValue.Locations)
                                {
                                    if (disposeDataAtExit.TryGetValue(location, out DisposeAbstractValue disposeValue))
                                    {
                                        switch (disposeValue.Kind)
                                        {
                                            // For MaybeDisposed, conservatively mark the field as disposed as we don't support path sensitive analysis.
                                            case DisposeAbstractValueKind.MaybeDisposed:
                                            case DisposeAbstractValueKind.Unknown:
                                            case DisposeAbstractValueKind.Escaped:
                                            case DisposeAbstractValueKind.Disposed:
                                                disposed = true;
                                                AddOrUpdateFieldDisposedValue(field, disposed);
                                                break;
                                        }
                                    }

                                    if (disposed)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
