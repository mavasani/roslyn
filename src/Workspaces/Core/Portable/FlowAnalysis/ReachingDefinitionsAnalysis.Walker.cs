// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal static partial class ReachingDefinitionsAnalysis
    {
        /// <summary>
        /// Operations walker used for walking high-level operation tree
        /// as well as control flow graph based operations.
        /// </summary>
        private sealed class Walker : OperationWalker
        {
            private AnalysisData _currentAnalysisData;
            private IOperation _currentRootOperation;
            private PooledDictionary<IAssignmentOperation, PooledHashSet<(ISymbol, IOperation)>> _pendingWritesMap;

            private static readonly ObjectPool<Walker> s_visitorPool = new ObjectPool<Walker>(() => new Walker());
            private Walker() { }

            public static void AnalyzeOperationsAndUpdateData(
                IEnumerable<IOperation> operations,
                AnalysisData analysisData,
                CancellationToken cancellationToken)
            {
                var visitor = s_visitorPool.Allocate();
                try
                {
                    visitor.Visit(operations, analysisData, cancellationToken);
                }
                finally
                {
                    s_visitorPool.Free(visitor);
                }
            }

            private void Visit(IEnumerable<IOperation> operations, AnalysisData analysisData, CancellationToken cancellationToken)
            {
                Debug.Assert(_currentAnalysisData == null);
                Debug.Assert(_currentRootOperation == null);
                Debug.Assert(_pendingWritesMap == null);

                _pendingWritesMap = PooledDictionary<IAssignmentOperation, PooledHashSet<(ISymbol, IOperation)>>.GetInstance();
                try
                {
                    _currentAnalysisData = analysisData;

                    foreach (var operation in operations)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        _currentRootOperation = operation;
                        Visit(operation);
                    }
                }
                finally
                {
                    _currentAnalysisData = null;
                    _currentRootOperation = null;
                    foreach (var pendingWrites in _pendingWritesMap.Values)
                    {
                        pendingWrites.Free();
                    }
                    _pendingWritesMap.Free();
                    _pendingWritesMap = null;
                }
            }

            private void OnReadReferenceFound(ISymbol symbol, IOperation operation)
                => _currentAnalysisData.OnReadReferenceFound(symbol, operation);

            private void OnWriteReferenceFound(ISymbol symbol, IOperation operation, bool maybeWritten)
            {
                _currentAnalysisData.OnWriteReferenceFound(symbol, operation, maybeWritten);
                ProcessPossibleDelegateCreationAssignment(symbol, operation);
            }

            private void OnLValueCaptureFound(ISymbol symbol, IOperation operation, CaptureId captureId)
                => _currentAnalysisData.OnLValueCaptureFound(symbol, operation, captureId);

            private void OnLValueDereferenceFound(CaptureId captureId)
                 => _currentAnalysisData.OnLValueDereferenceFound(captureId);

            private void OnReferenceFound(ISymbol symbol, IOperation operation)
            {
                var valueUsageInfo = operation.GetValueUsageInfo();
                var isReadFrom = valueUsageInfo.IsReadFrom();
                var isWrittenTo = valueUsageInfo.IsWrittenTo();

                if (isWrittenTo && MakePendingWrite())
                {
                    // Certain writes are processed at a later visit
                    // and are marked as a pending write for post processing.
                    // For example, consider the write to 'x' in "x = M(x, ...)".
                    // We visit the Target (left) of assignment before visiting the Value (right)
                    // of the assignment, as there might be expressions on the left that are evaluated first.
                    // We don't want to mark the symbol read while processing the left of assignment
                    // as there can be references on the right, which reads the prior value.
                    // Instead we mark this as a pending write, which will be processed when we finish visiting the assignment.
                    isWrittenTo = false;
                }

                if (isReadFrom && isWrittenTo)
                {
                    // Read/Write could either be:
                    //  1. A read followed by a write. For example, increment "i++", compound assignment "i += 1", etc.
                    //  2. A declaration/write followed by a read. For example, declaration pattern 'int i' inside
                    //     an is pattern exprssion "if (x is int i)").
                    // Handle scenario 2 (declaration pattern) specially and use an assert to catch unknown cases.
                    if (operation.Kind == OperationKind.DeclarationPattern && operation.Parent?.Kind == OperationKind.IsPattern)
                    {
                        OnWriteReferenceFound(symbol, operation, maybeWritten: false);

                        // Special handling for implicit IsPattern parent operation.
                        // In ControlFlowGraph, we generate implicit IsPattern operation for pattern case clauses,
                        // where is the read is not observable and we want to consider such case clause declaration patterns
                        // as just a write.
                        if (!operation.Parent.IsImplicit)
                        {
                            OnReadReferenceFound(symbol, operation);
                        }

                        return;
                    }

                    Debug.Assert(operation.Parent is IIncrementOrDecrementOperation ||
                                 operation.Parent is IArgumentOperation argument && argument.Parameter.RefKind == RefKind.Ref,
                                 "Unhandled read-write ordering");
                }

                if (isReadFrom)
                {
                    if (operation.Parent is IFlowCaptureOperation flowCapture &&
                        _currentAnalysisData.IsLValueFlowCapture(flowCapture.Id))
                    {
                        OnLValueCaptureFound(symbol, operation, flowCapture.Id);
                    }
                    else
                    {
                        OnReadReferenceFound(symbol, operation);
                    }
                }

                if (isWrittenTo)
                {   
                    // maybeWritten == 'ref' argument.
                    OnWriteReferenceFound(symbol, operation, maybeWritten: valueUsageInfo == ValueUsageInfo.ReadableWritableReference);
                }

                return;

                // Local functions
                bool MakePendingWrite()
                {
                    Debug.Assert(isWrittenTo);

                    if (operation.Parent is IAssignmentOperation assignmentOperation &&
                        assignmentOperation.Target == operation)
                    {
                        var set = PooledHashSet<(ISymbol, IOperation)>.GetInstance();
                        set.Add((symbol, operation));
                        _pendingWritesMap.Add(assignmentOperation, set);
                        return true;
                    }
                    else if(operation.IsInLeftOfDeconstructionAssignment(out var deconstructionAssignment))
                    {
                        if (!_pendingWritesMap.TryGetValue(deconstructionAssignment, out var set))
                        {
                            set = PooledHashSet<(ISymbol, IOperation)>.GetInstance();
                            _pendingWritesMap.Add(deconstructionAssignment, set);
                        }

                        set.Add((symbol, operation));
                        return true;
                    }

                    return false;
                }
            }

            private void ProcessPendingWritesForAssignmentTarget(IAssignmentOperation operation)
            {
                if (_pendingWritesMap.TryGetValue(operation, out var pendingWrites))
                {
                    foreach (var (symbol, definition) in pendingWrites)
                    {
                        OnWriteReferenceFound(symbol, definition, maybeWritten: false);
                    }

                    _pendingWritesMap.Remove(operation);
                }
            }

            public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
            {
                base.VisitSimpleAssignment(operation);
                ProcessPendingWritesForAssignmentTarget(operation);
            }

            public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
            {
                base.VisitCompoundAssignment(operation);
                ProcessPendingWritesForAssignmentTarget(operation);
            }

            public override void VisitDeconstructionAssignment(IDeconstructionAssignmentOperation operation)
            {
                base.VisitDeconstructionAssignment(operation);
                ProcessPendingWritesForAssignmentTarget(operation);
            }

            public override void VisitLocalReference(ILocalReferenceOperation operation)
            {
                OnReferenceFound(operation.Local, operation);
            }

            public override void VisitParameterReference(IParameterReferenceOperation operation)
            {
                OnReferenceFound(operation.Parameter, operation);
            }

            public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
            {
                var variableInitializer = operation.GetVariableInitializer();
                if (variableInitializer != null
                    || operation.Parent is IForEachLoopOperation forEachLoop && forEachLoop.LoopControlVariable == operation)
                {
                    OnWriteReferenceFound(operation.Symbol, operation, maybeWritten: false);
                }

                base.VisitVariableDeclarator(operation);
            }

            public override void VisitFlowCaptureReference(IFlowCaptureReferenceOperation operation)
            {
                base.VisitFlowCaptureReference(operation);

                if (_currentAnalysisData.IsLValueFlowCapture(operation.Id))
                {
                    OnLValueDereferenceFound(operation.Id);
                }
            }

            public override void VisitDeclarationPattern(IDeclarationPatternOperation operation)
            {
                OnReferenceFound(operation.DeclaredSymbol, operation);
            }

            public override void VisitInvocation(IInvocationOperation operation)
            {
                base.VisitInvocation(operation);

                switch (operation.TargetMethod.MethodKind)
                {
                    case MethodKind.AnonymousFunction:
                    case MethodKind.DelegateInvoke:
                        if (operation.Instance != null)
                        {
                            AnalyzePossibleDelegateInvocation(operation.Instance);
                        }
                        else
                        {
                            _currentAnalysisData.ResetState(operation);
                        }
                        break;

                    case MethodKind.LocalFunction:
                        var newAnalysisData = _currentAnalysisData.AnalyzeLocalFunction(operation.TargetMethod);
                        if (newAnalysisData != null)
                        {
                            _currentAnalysisData.SetCurrentBlockAnalysisDataFrom(newAnalysisData);
                        }
                        else
                        {
                            _currentAnalysisData.ResetState(operation);
                        }
                        break;
                }
            }

            public override void VisitArgument(IArgumentOperation operation)
            {
                base.VisitArgument(operation);

                if (operation.Value.Type.IsDelegateType())
                {
                    AnalyzePossibleDelegateInvocation(operation.Value);
                }
            }
            
            public override void VisitLocalFunction(ILocalFunctionOperation operation)
            {
                // Skip visiting if we are doing an operation tree walk of definition.
                // This will only happen if the operation is not the current root operation.
                if (_currentRootOperation != operation)
                {
                    return;
                }

                base.VisitLocalFunction(operation);
            }

            public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
            {
                // Skip visiting if we are doing an operation tree walk of definition.
                // This will only happen if the operation is not the current root operation.
                if (_currentRootOperation != operation)
                {
                    return;
                }

                base.VisitAnonymousFunction(operation);
            }

            public override void VisitFlowAnonymousFunction(IFlowAnonymousFunctionOperation operation)
            {
                // Skip visiting if we are not analyzing an invocation of this lambda.
                // This will only happen if the operation is not the current root operation.
                if (_currentRootOperation != operation)
                {
                    return;
                }

                base.VisitFlowAnonymousFunction(operation);
            }

            private void ProcessPossibleDelegateCreationAssignment(ISymbol symbol, IOperation definition)
            {
                if (symbol.GetSymbolType().TypeKind != TypeKind.Delegate)
                {
                    return;
                }

                IOperation initializerValue = null;
                if (definition is IVariableDeclaratorOperation variableDeclarator)
                {
                    initializerValue = variableDeclarator.GetVariableInitializer()?.Value;
                }
                else if (definition.Parent is ISimpleAssignmentOperation simpleAssignment)
                {
                    initializerValue = simpleAssignment.Value;
                }

                if (initializerValue != null)
                {
                    ProcessPossibleDelegateCreation(initializerValue, definition);
                }
            }

            private void ProcessPossibleDelegateCreation(IOperation creation, IOperation definition)
            {
                var currentOperation = creation;
                while (true)
                {
                    switch (currentOperation.Kind)
                    {
                        case OperationKind.Conversion:
                            currentOperation = ((IConversionOperation)currentOperation).Operand;
                            continue;

                        case OperationKind.Parenthesized:
                            currentOperation = ((IParenthesizedOperation)currentOperation).Operand;
                            continue;

                        case OperationKind.DelegateCreation:
                            currentOperation = ((IDelegateCreationOperation)currentOperation).Target;
                            continue;

                        case OperationKind.AnonymousFunction:
                            // We don't support lambda target analysis for operation tree
                            // and control flow graph should have replaced 'AnonymousFunction' nodes
                            // with 'FlowAnonymousFunction' nodes.
                            throw ExceptionUtilities.Unreachable;

                        case OperationKind.FlowAnonymousFunction:
                            _currentAnalysisData.SetReachingDelegateCreationTarget(definition, currentOperation);
                            return;

                        case OperationKind.MethodReference:
                            var targetMethod = ((IMethodReferenceOperation)currentOperation).Method;
                            if (targetMethod.IsLocalFunction())
                            {
                                _currentAnalysisData.SetReachingDelegateCreationTarget(definition, currentOperation);
                            }
                            return;

                        case OperationKind.LocalReference:
                            var localReference = (ILocalReferenceOperation)currentOperation;
                            _currentAnalysisData.SetReachingDelegateCreationTargetsFromSymbol(definition, localReference.Local);
                            return;

                        case OperationKind.ParameterReference:
                            var parameterReference = (IParameterReferenceOperation)currentOperation;
                            _currentAnalysisData.SetReachingDelegateCreationTargetsFromSymbol(definition, parameterReference.Parameter);
                            return;

                        default:
                            return;
                    }
                }
            }

            private void AnalyzePossibleDelegateInvocation(IOperation operation)
            {
                Debug.Assert(operation.Type.IsDelegateType());

                ProcessPossibleDelegateCreation(creation: operation, definition: operation);
                if (!_currentAnalysisData.TryGetReachingDelegateCreationTargets(operation, out var targets))
                {
                    // Failed to identify targets, so conservatively reset the state.
                    _currentAnalysisData.ResetState(operation);
                    return;
                }
                
                switch (targets.Count)
                {
                    case 0:
                        // None of the delegate invocation targets are lambda/local functions.
                        break;

                    case 1:
                        // Single target, just analyze it explicitly.
                        AnalyzeTarget(targets.Single());
                        break;

                    default:
                        // Multiple potential lambda/local function targets.
                        // Analyze each one and then merge the outputs from all.
                        var savedCurrentAnalysisData = _currentAnalysisData.CreateBasicBlockAnalysisData();
                        savedCurrentAnalysisData.SetAnalysisDataFrom(_currentAnalysisData.CurrentBlockAnalysisData);

                        var mergedAnalysisData = _currentAnalysisData.CreateBasicBlockAnalysisData();
                        foreach (var target in targets)
                        {
                            _currentAnalysisData.SetCurrentBlockAnalysisDataFrom(savedCurrentAnalysisData);
                            AnalyzeTarget(target);
                            mergedAnalysisData = BasicBlockAnalysisData.Merge(mergedAnalysisData,
                                _currentAnalysisData.CurrentBlockAnalysisData, _currentAnalysisData.CreateBasicBlockAnalysisData);
                        }
                        _currentAnalysisData.SetCurrentBlockAnalysisDataFrom(mergedAnalysisData);
                        break;
                }

                return;

                // Local functions.
                void AnalyzeTarget(IOperation target)
                {
                    switch (target.Kind)
                    {
                        case OperationKind.FlowAnonymousFunction:
                            _currentAnalysisData.AnalyzeLambda(target);
                            break;

                        case OperationKind.MethodReference:
                            var methodReference = (IMethodReferenceOperation)target;
                            Debug.Assert(methodReference.Method.IsLocalFunction());
                            _currentAnalysisData.AnalyzeLocalFunction(methodReference.Method);
                            break;

                        default:
                            throw ExceptionUtilities.Unreachable;
                    }
                }
            }
        }
    }
}
