// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

//namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
//{
//    internal sealed class LambdaPointsToAnalyzer : AbstractDataFlowAnalyzer<LambdaBlockAnalysisData>
//    {
//        private readonly PooledHashSet<(ISymbol Symbol, IOperation Definition)> _unusedDefinitions;
//        private readonly PooledHashSet<ILocalSymbol> _referencedLocals;
//        private readonly LambdaBlockAnalysisData _currentBlockAnalysisData;
//        private readonly Dictionary<BasicBlock, LambdaBlockAnalysisData> _reachingDefinitionsMap;

//        private LambdaPointsToAnalyzer(ControlFlowGraph cfg)
//        {
//            _unusedDefinitions = PooledHashSet<(ISymbol, IOperation)>.GetInstance();
//            _referencedLocals = PooledHashSet<ILocalSymbol>.GetInstance();
//            _currentBlockAnalysisData = LambdaBlockAnalysisData.GetInstance();
//            _reachingDefinitionsMap = CreateReachingDefinitionsMap(cfg);
//        }

//        private static Dictionary<BasicBlock, LambdaBlockAnalysisData> CreateReachingDefinitionsMap(ControlFlowGraph cfg)
//        {
//            var builder = new Dictionary<BasicBlock, LambdaBlockAnalysisData>(cfg.Blocks.Length);
//            foreach (var block in cfg.Blocks)
//            {
//                builder.Add(block, null);
//            }

//            return builder;
//        }

//        public static UnusedDefinitionsResult AnalyzeAndGetUnusedDefinitions(ControlFlowGraph cfg)
//        {
//            using (var analyzer = new ReachingAndUnusedDefinitionsAnalyzer(cfg))
//            {
//                _ = CustomDataFlowAnalysis<ReachingAndUnusedDefinitionsAnalyzer, LambdaBlockAnalysisData>.Run(cfg.Blocks, analyzer);
//                return new UnusedDefinitionsResult(analyzer._unusedDefinitions.ToImmutableHashSet(), analyzer._referencedLocals.ToImmutableHashSet());
//            }
//        }

//        public static UnusedDefinitionsResult AnalyzeAndGetUnusedDefinitions(IOperation rootOperation)
//        {
//            var unusedDefinitions = PooledHashSet<(ISymbol, IOperation)>.GetInstance();
//            var referencedLocals = PooledHashSet<ILocalSymbol>.GetInstance();
//            var analysisData = LambdaBlockAnalysisData.GetInstance();
//            try
//            {
//                Visitor.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(rootOperation), analysisData, unusedDefinitions, referencedLocals);
//                return new UnusedDefinitionsResult(unusedDefinitions.ToImmutableHashSet(), referencedLocals.ToImmutableHashSet());
//            }
//            finally
//            {
//                unusedDefinitions.Free();
//                referencedLocals.Free();
//                analysisData.Free();
//            }
//        }

//        protected override void Dispose(bool disposing)
//        {
//            _unusedDefinitions.Free();
//            _referencedLocals.Free();
//            _currentBlockAnalysisData.Free();
//            // TODO free _reachingDefinitionsMap.
//        }

//        private LambdaBlockAnalysisData GetOrCreateBlockData(BasicBlock basicBlock)
//        {
//            if (_reachingDefinitionsMap[basicBlock] == null)
//            {
//                _reachingDefinitionsMap[basicBlock] = LambdaBlockAnalysisData.GetInstance();
//            }

//            return _reachingDefinitionsMap[basicBlock];
//        }

//        protected override LambdaBlockAnalysisData AnalyzeBlock(BasicBlock basicBlock)
//        {
//            _currentBlockAnalysisData.SetAnalysisDataFrom(GetOrCreateBlockData(basicBlock));
//            Visitor.AnalyzeOperationsAndUpdateData(basicBlock.Operations, _currentBlockAnalysisData, _unusedDefinitions, _referencedLocals);
//            return _currentBlockAnalysisData;
//        }

//        protected override LambdaBlockAnalysisData AnalyzeNonConditionalBranch(BasicBlock basicBlock, LambdaBlockAnalysisData currentAnalysisData)
//        {
//            Visitor.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(basicBlock.BranchValue), currentAnalysisData, _unusedDefinitions, _referencedLocals);
//            return currentAnalysisData;
//        }

//        protected override (LambdaBlockAnalysisData fallThroughSuccessorData, LambdaBlockAnalysisData conditionalSuccessorData) AnalyzeConditionalBranch(BasicBlock basicBlock, LambdaBlockAnalysisData currentAnalysisData)
//        {
//            Visitor.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(basicBlock.BranchValue), currentAnalysisData, _unusedDefinitions, _referencedLocals);
//            return (currentAnalysisData, currentAnalysisData);
//        }

//        protected override LambdaBlockAnalysisData GetCurrentAnalysisData(BasicBlock basicBlock)
//            => _reachingDefinitionsMap[basicBlock];

//        protected override LambdaBlockAnalysisData GetInitialAnalysisData()
//            => LambdaBlockAnalysisData.GetInstance();

//        protected override void SetCurrentAnalysisData(BasicBlock basicBlock, LambdaBlockAnalysisData data)
//        {
//            GetOrCreateBlockData(basicBlock).SetAnalysisDataFrom(data);
//            if (!ReferenceEquals(data, _currentBlockAnalysisData))
//            {
//                data.Free();
//            }
//        }

//        protected override bool IsEqual(LambdaBlockAnalysisData analysisData1, LambdaBlockAnalysisData analysisData2)
//            => analysisData1 == null ? analysisData2 == null : analysisData1.Equals(analysisData2);

//        protected override LambdaBlockAnalysisData Merge(LambdaBlockAnalysisData analysisData1, LambdaBlockAnalysisData analysisData2)
//            => LambdaBlockAnalysisData.Merge(analysisData1, analysisData2);

//        private sealed class Visitor : OperationWalker
//        {
//            private LambdaBlockAnalysisData _currentAnalysisData;
//            private PooledHashSet<(ISymbol, IOperation)> _unusedDefinitions;
//            private PooledHashSet<ILocalSymbol> _referencedLocals;

//            private static readonly ObjectPool<Visitor> s_visitorPool = new ObjectPool<Visitor>(() => new Visitor());
//            private Visitor() { }

//            public static void AnalyzeOperationsAndUpdateData(
//                IEnumerable<IOperation> operations,
//                LambdaBlockAnalysisData analysisData,
//                PooledHashSet<(ISymbol, IOperation)> unusedDefinitions,
//                PooledHashSet<ILocalSymbol> referencedLocals)
//            {
//                var visitor = s_visitorPool.Allocate();
//                try
//                {
//                    visitor.Visit(operations, analysisData, unusedDefinitions, referencedLocals);
//                }
//                finally
//                {
//                    s_visitorPool.Free(visitor);
//                }
//            }

//            private void Visit(
//                IEnumerable<IOperation> operations,
//                LambdaBlockAnalysisData analysisData,
//                PooledHashSet<(ISymbol, IOperation)> unusedDefinitions,
//                PooledHashSet<ILocalSymbol> referencedLocals)
//            {
//                Debug.Assert(_currentAnalysisData == null);
//                Debug.Assert(_unusedDefinitions == null);
//                Debug.Assert(_referencedLocals == null);

//                _currentAnalysisData = analysisData;
//                _unusedDefinitions = unusedDefinitions;
//                _referencedLocals = referencedLocals;

//                foreach (var operation in operations)
//                {
//                    Visit(operation);
//                }

//                _currentAnalysisData = null;
//                _unusedDefinitions = null;
//                _referencedLocals = null;
//            }

//            private void OnReadReferenceFound(ISymbol symbol, IOperation operation)
//            {
//                if (_unusedDefinitions.Count != 0)
//                {
//                    var currentDefinitions = _currentAnalysisData.GetCurrentCandidateInvocationTargets(symbol);
//                    foreach (var definition in currentDefinitions)
//                    {
//                        _unusedDefinitions.Remove((symbol, definition));
//                    }
//                }

//                if (symbol is ILocalSymbol localSymbol)
//                {
//                    _referencedLocals.Add(localSymbol);
//                }
//            }

//            private static bool IsUnusedDefinitionCandidate(ISymbol symbol)
//                => !(symbol is IParameterSymbol parameter) || (parameter.RefKind != RefKind.Ref && parameter.RefKind != RefKind.Out);

//            private void OnWriteReferenceFound(ISymbol symbol, IOperation operation, bool maybeWritten)
//            {
//                _currentAnalysisData.OnWriteReferenceFound(symbol, operation, maybeWritten);

//                if (IsUnusedDefinitionCandidate(symbol))
//                {
//                    _unusedDefinitions.Add((symbol, operation));
//                }
//            }

//            private void OnReferenceFound(ISymbol symbol, IOperation operation)
//            {
//                var valueUsageInfo = operation.GetValueUsageInfo();
//                if (valueUsageInfo.ContainsReadOrReadableRef())
//                {
//                    OnReadReferenceFound(symbol, operation);
//                }

//                if (valueUsageInfo.ContainsWriteOrWritableRef())
//                {
//                    // maybeWritten == 'ref' argument.
//                    OnWriteReferenceFound(symbol, operation, maybeWritten: valueUsageInfo == ValueUsageInfo.ReadableWritableRef);
//                }
//            }

//            public override void VisitLocalReference(ILocalReferenceOperation operation)
//            {
//                OnReferenceFound(operation.Local, operation);
//            }

//            public override void VisitParameterReference(IParameterReferenceOperation operation)
//            {
//                OnReferenceFound(operation.Parameter, operation);
//            }

//            public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
//            {
//                if (operation.GetVariableInitializer() != null
//                    || operation.Parent is IForEachLoopOperation forEachLoop && forEachLoop.LoopControlVariable == operation)
//                {
//                    OnWriteReferenceFound(operation.Symbol, operation, maybeWritten: false);
//                }

//                base.VisitVariableDeclarator(operation);
//            }

//            public override void VisitDeclarationPattern(IDeclarationPatternOperation operation)
//            {
//                OnWriteReferenceFound(operation.DeclaredSymbol, operation, maybeWritten: false);
//                OnReadReferenceFound(operation.DeclaredSymbol, operation);
//            }
//        }
//    }

//    internal sealed class LambdaBlockAnalysisData : IEquatable<LambdaBlockAnalysisData>
//    {
//        private static readonly ObjectPool<LambdaBlockAnalysisData> s_pool =
//            new ObjectPool<LambdaBlockAnalysisData>(() => new LambdaBlockAnalysisData());

//        private readonly Dictionary<ISymbol, PooledHashSet<IOperation>> _candidateInvocationTargets;
//        private LambdaBlockAnalysisData()
//        {
//            _candidateInvocationTargets = new Dictionary<ISymbol, PooledHashSet<IOperation>>();
//        }

//        public static LambdaBlockAnalysisData GetInstance() => s_pool.Allocate();
//        public void Free()
//        {
//            Clear();
//            s_pool.Free(this);
//        }

//        private void Clear()
//        {
//            foreach (var value in _candidateInvocationTargets.Values)
//            {
//                value.Free();
//            }

//            _candidateInvocationTargets.Clear();
//        }

//        public void SetAnalysisDataFrom(LambdaBlockAnalysisData other)
//        {
//            Debug.Assert(other != null);

//            Clear();
//            AddEntries(_candidateInvocationTargets, other);
//        }

//        public IEnumerable<IOperation> GetCurrentCandidateInvocationTargets(ISymbol symbol)
//        {
//            if (_candidateInvocationTargets.TryGetValue(symbol, out var values))
//            {
//                foreach (var value in values)
//                {
//                    yield return value;
//                }
//            }
//        }

//        public void OnWriteReferenceFound(ISymbol symbol, IOperation operation, bool maybeWritten)
//        {
//            if (!_candidateInvocationTargets.TryGetValue(symbol, out var values))
//            {
//                values = PooledHashSet<IOperation>.GetInstance();
//                _candidateInvocationTargets.Add(symbol, values);
//            }
//            else if (!maybeWritten)
//            {
//                values.Clear();
//            }

//            values.Add(operation);
//        }

//        public bool Equals(LambdaBlockAnalysisData other)
//            => this.GetHashCode() == (other?.GetHashCode() ?? 0);

//        public override bool Equals(object obj)
//        {
//            return Equals(obj as LambdaBlockAnalysisData);
//        }

//        public override int GetHashCode()
//        {
//            var hashCode = _candidateInvocationTargets.Count;
//            foreach ((int keyHash, int valueHash) in _candidateInvocationTargets.Select(GetKeyValueHashCodeTuple).OrderBy(hashTuple => hashTuple.keyHash))
//            {
//                hashCode = Hash.Combine(Hash.Combine(keyHash, valueHash), hashCode);
//            }

//            return hashCode;

//            // Local functions.
//            (int keyHash, int valueHash) GetKeyValueHashCodeTuple(KeyValuePair<ISymbol, PooledHashSet<IOperation>> keyValuePair)
//                => (keyValuePair.Key.GetHashCode(), Hash.Combine(keyValuePair.Value.Count, Hash.CombineValues(keyValuePair.Value)));
//        }

//        private bool IsEmpty => _candidateInvocationTargets.Count == 0;

//        public static LambdaBlockAnalysisData Merge(LambdaBlockAnalysisData data1, LambdaBlockAnalysisData data2)
//        {
//            if (data1 == null)
//            {
//                Debug.Assert(data2 != null);
//                return data2;
//            }
//            else if (data2 == null)
//            {
//                Debug.Assert(data1 != null);
//                return data1;
//            }
//            else if (data1.IsEmpty)
//            {
//                return data2;
//            }
//            else if (data2.IsEmpty)
//            {
//                return data1;
//            }

//            var mergedData = GetInstance();
//            AddEntries(mergedData._reachingDefinitions, data1);
//            AddEntries(mergedData._reachingDefinitions, data2);

//            if (mergedData.Equals(data1))
//            {
//                mergedData.Free();
//                return data1;
//            }
//            else if (mergedData.Equals(data2))
//            {
//                mergedData.Free();
//                return data2;
//            }

//            return mergedData;
//        }

//        private static void AddEntries(Dictionary<ISymbol, PooledHashSet<IOperation>> result, LambdaBlockAnalysisData source)
//        {
//            if (source != null)
//            {
//                foreach (var kvp in source._reachingDefinitions)
//                {
//                    if (!result.TryGetValue(kvp.Key, out var values))
//                    {
//                        values = PooledHashSet<IOperation>.GetInstance();
//                        result.Add(kvp.Key, values);
//                    }

//                    values.AddRange(kvp.Value);
//                }
//            }
//        }
//    }

//    internal sealed class UnusedDefinitionsResult
//    {
//        public UnusedDefinitionsResult(ImmutableHashSet<(ISymbol, IOperation)> unusedDefinitions, ImmutableHashSet<ILocalSymbol> referencedLocals)
//        {
//            UnusedDefinitions = unusedDefinitions;
//            ReferencedLocals = referencedLocals;
//        }

//        public ImmutableHashSet<(ISymbol Symbol, IOperation Definition)> UnusedDefinitions { get; }
//        public ImmutableHashSet<ILocalSymbol> ReferencedLocals { get; }
//    }
//}
