// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal sealed partial class ReachingDefinitionsAnalyzer : AbstractDataFlowAnalyzer<ReachingDefinitionsBlockAnalysisData>
    {
        private sealed class Walker : OperationWalker
        {
            private ReachingDefinitionsBlockAnalysisData _currentAnalysisData;
            private PooledHashSet<(ISymbol, IOperation)> _unusedDefinitions;
            private PooledHashSet<ILocalSymbol> _referencedLocals;

            private static readonly ObjectPool<Walker> s_visitorPool = new ObjectPool<Walker>(() => new Walker());
            private Walker() { }

            public static void AnalyzeOperationsAndUpdateData(
                IEnumerable<IOperation> operations,
                ReachingDefinitionsBlockAnalysisData analysisData,
                PooledHashSet<(ISymbol, IOperation)> unusedDefinitions,
                PooledHashSet<ILocalSymbol> referencedLocals)
            {
                var visitor = s_visitorPool.Allocate();
                try
                {
                    visitor.Visit(operations, analysisData, unusedDefinitions, referencedLocals);
                }
                finally
                {
                    s_visitorPool.Free(visitor);
                }
            }

            private void Visit(
                IEnumerable<IOperation> operations,
                ReachingDefinitionsBlockAnalysisData analysisData,
                PooledHashSet<(ISymbol, IOperation)> unusedDefinitions,
                PooledHashSet<ILocalSymbol> referencedLocals)
            {
                Debug.Assert(_currentAnalysisData == null);
                Debug.Assert(_unusedDefinitions == null);
                Debug.Assert(_referencedLocals == null);

                _currentAnalysisData = analysisData;
                _unusedDefinitions = unusedDefinitions;
                _referencedLocals = referencedLocals;

                foreach (var operation in operations)
                {
                    Visit(operation);
                }

                _currentAnalysisData = null;
                _unusedDefinitions = null;
                _referencedLocals = null;
            }

            private void OnReadReferenceFound(ISymbol symbol, IOperation operation)
            {
                if (symbol.Kind == SymbolKind.Discard)
                {
                    return;
                }

                if (_unusedDefinitions.Count != 0)
                {
                    var currentDefinitions = _currentAnalysisData.GetCurrentDefinitions(symbol);
                    foreach (var definition in currentDefinitions)
                    {
                        _unusedDefinitions.Remove((symbol, definition));
                    }
                }

                if (symbol is ILocalSymbol localSymbol)
                {
                    _referencedLocals.Add(localSymbol);
                }
            }

            private static bool IsUnusedDefinitionCandidate(ISymbol symbol)
                => !(symbol is IParameterSymbol parameter) || (parameter.RefKind != RefKind.Ref && parameter.RefKind != RefKind.Out);

            private void OnWriteReferenceFound(ISymbol symbol, IOperation operation, bool maybeWritten)
            {
                if (symbol.Kind == SymbolKind.Discard)
                {
                    return;
                }

                _currentAnalysisData.OnWriteReferenceFound(symbol, operation, maybeWritten);

                if (IsUnusedDefinitionCandidate(symbol) && !maybeWritten)
                {
                    _unusedDefinitions.Add((symbol, operation));
                }
            }

            private void OnReferenceFound(ISymbol symbol, IOperation operation)
            {
                var valueUsageInfo = operation.GetValueUsageInfo();
                var isReadFrom = valueUsageInfo.IsReadFrom();
                var isWrittenTo = valueUsageInfo.IsWrittenTo();

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

                    Debug.Assert(operation.Parent.Kind == OperationKind.CompoundAssignment ||
                                 operation.Parent.Kind == OperationKind.Increment ||
                                 operation.Parent is IArgumentOperation argument && argument.Parameter.RefKind == RefKind.Ref,
                                 "Unhandled read-write ordering");
                }

                if (isReadFrom)
                {
                    OnReadReferenceFound(symbol, operation);
                }

                if (isWrittenTo)
                {
                    // maybeWritten == 'ref' argument.
                    OnWriteReferenceFound(symbol, operation, maybeWritten: valueUsageInfo == ValueUsageInfo.ReadableWritableReference);
                }
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
                if (operation.GetVariableInitializer() != null
                    || operation.Parent is IForEachLoopOperation forEachLoop && forEachLoop.LoopControlVariable == operation)
                {
                    OnWriteReferenceFound(operation.Symbol, operation, maybeWritten: false);
                }

                base.VisitVariableDeclarator(operation);
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
                    case MethodKind.LocalFunction:
                    case MethodKind.DelegateInvoke:
                        // We currently do not support analyzing or tracking lambda/local function/delegate invocations.
                        ResetState(operation);
                        break;
                }
            }

            private void ResetState(IOperation operation)
            {
                foreach (var symbol in _unusedDefinitions.Select(d => d.Item1).ToArray())
                {
                    OnReadReferenceFound(symbol, operation);
                }
            }
        }
    }
}
