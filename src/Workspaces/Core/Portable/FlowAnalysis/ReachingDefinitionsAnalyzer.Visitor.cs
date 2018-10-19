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
            /// <summary>
            /// Map from each definition to a boolean indicating if the value assinged
            /// at definition is used/read on some control flow path.
            /// </summary>
            private PooledDictionary<(ISymbol symbol, IOperation operation), bool> _definitionUsageMap;
            private PooledHashSet<ISymbol> _referencedSymbols;
            private ReachingDefinitionsBlockAnalysisData _currentAnalysisData;

            private static readonly ObjectPool<Walker> s_visitorPool = new ObjectPool<Walker>(() => new Walker());
            private Walker() { }

            public static void AnalyzeOperationsAndUpdateData(
                IEnumerable<IOperation> operations,
                ReachingDefinitionsBlockAnalysisData analysisData,
                PooledDictionary<(ISymbol, IOperation), bool> definitionUsageMap,
                PooledHashSet<ISymbol> referencedLocals)
            {
                var visitor = s_visitorPool.Allocate();
                try
                {
                    visitor.Visit(operations, analysisData, definitionUsageMap, referencedLocals);
                }
                finally
                {
                    s_visitorPool.Free(visitor);
                }
            }

            private void Visit(
                IEnumerable<IOperation> operations,
                ReachingDefinitionsBlockAnalysisData analysisData,
                PooledDictionary<(ISymbol, IOperation), bool> definitionUsageMap,
                PooledHashSet<ISymbol> referencedSymbols)
            {
                Debug.Assert(_currentAnalysisData == null);
                Debug.Assert(_definitionUsageMap == null);
                Debug.Assert(_referencedSymbols == null);

                _currentAnalysisData = analysisData;
                _definitionUsageMap = definitionUsageMap;
                _referencedSymbols = referencedSymbols;

                foreach (var operation in operations)
                {
                    Visit(operation);
                }

                _currentAnalysisData = null;
                _definitionUsageMap = null;
                _referencedSymbols = null;
            }

            private void OnReadReferenceFound(ISymbol symbol, IOperation operation)
            {
                if (symbol.Kind == SymbolKind.Discard)
                {
                    return;
                }

                if (_definitionUsageMap.Count != 0)
                {
                    var currentDefinitions = _currentAnalysisData.GetCurrentDefinitions(symbol);
                    foreach (var definition in currentDefinitions)
                    {
                        _definitionUsageMap[(symbol, definition)] = true;
                    }
                }

                _referencedSymbols.Add(symbol);
            }

            private void OnWriteReferenceFound(ISymbol symbol, IOperation operation, bool maybeWritten)
            {
                var definition = (symbol, operation);
                if (symbol.Kind == SymbolKind.Discard)
                {
                    // Skip discard symbols and also for already processed writes (back edge from loops).
                    return;
                }

                _currentAnalysisData.OnWriteReferenceFound(symbol, operation, maybeWritten);

                // Only mark as unused definition if we are processing it for the first time (not from back edge for loops)
                if (!_definitionUsageMap.ContainsKey(definition) &&
                    !maybeWritten)
                {
                    _definitionUsageMap.Add((symbol, operation), false);
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

                    Debug.Assert(operation.Parent is ICompoundAssignmentOperation ||
                                 operation.Parent is IIncrementOrDecrementOperation ||
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

            public override void VisitArgument(IArgumentOperation operation)
            {
                base.VisitArgument(operation);

                
            }

            private void ResetState(IOperation operation)
            {
                foreach (var symbol in _definitionUsageMap.Keys.Select(d => d.symbol).ToArray())
                {
                    OnReadReferenceFound(symbol, operation);
                }
            }
        }
    }
}
