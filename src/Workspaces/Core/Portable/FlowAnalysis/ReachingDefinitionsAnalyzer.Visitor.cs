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
                _currentAnalysisData.OnWriteReferenceFound(symbol, operation, maybeWritten);

                if (IsUnusedDefinitionCandidate(symbol) && !maybeWritten)
                {
                    _unusedDefinitions.Add((symbol, operation));
                }
            }

            private void OnReferenceFound(ISymbol symbol, IOperation operation)
            {
                var valueUsageInfo = operation.GetValueUsageInfo();
                if (valueUsageInfo.IsReadFrom())
                {
                    OnReadReferenceFound(symbol, operation);
                }

                if (valueUsageInfo.IsWrittenTo())
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
                OnWriteReferenceFound(operation.DeclaredSymbol, operation, maybeWritten: false);
                OnReadReferenceFound(operation.DeclaredSymbol, operation);
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
