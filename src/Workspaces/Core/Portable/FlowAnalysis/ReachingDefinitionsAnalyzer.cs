// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal sealed partial class ReachingDefinitionsAnalyzer : AbstractDataFlowAnalyzer<ReachingDefinitionsBlockAnalysisData>
    {
        private readonly PooledHashSet<(ISymbol Symbol, IOperation Definition)> _unusedDefinitions;
        private readonly PooledHashSet<ILocalSymbol> _referencedLocals;
        private readonly ReachingDefinitionsBlockAnalysisData _currentBlockAnalysisData;
        private readonly Dictionary<BasicBlock, ReachingDefinitionsBlockAnalysisData> _reachingDefinitionsMap;

        private ReachingDefinitionsAnalyzer(ControlFlowGraph cfg)
        {
            _unusedDefinitions = PooledHashSet<(ISymbol, IOperation)>.GetInstance();
            _referencedLocals = PooledHashSet<ILocalSymbol>.GetInstance();
            _currentBlockAnalysisData = ReachingDefinitionsBlockAnalysisData.GetInstance();
            _reachingDefinitionsMap = CreateReachingDefinitionsMap(cfg);
        }

        private static Dictionary<BasicBlock, ReachingDefinitionsBlockAnalysisData> CreateReachingDefinitionsMap(ControlFlowGraph cfg)
        {
            var builder = new Dictionary<BasicBlock, ReachingDefinitionsBlockAnalysisData>(cfg.Blocks.Length);
            foreach (var block in cfg.Blocks)
            {
                builder.Add(block, null);
            }

            return builder;
        }

        public static UnusedDefinitionsResult AnalyzeAndGetUnusedDefinitions(ControlFlowGraph cfg)
        {
            using (var analyzer = new ReachingDefinitionsAnalyzer(cfg))
            {
                _ = CustomDataFlowAnalysis<ReachingDefinitionsAnalyzer, ReachingDefinitionsBlockAnalysisData>.Run(cfg.Blocks, analyzer);
                return new UnusedDefinitionsResult(analyzer._unusedDefinitions.ToImmutableHashSet(), analyzer._referencedLocals.ToImmutableHashSet());
            }
        }

        public static UnusedDefinitionsResult AnalyzeAndGetUnusedDefinitions(IOperation rootOperation)
        {
            var unusedDefinitions = PooledHashSet<(ISymbol, IOperation)>.GetInstance();
            var referencedLocals = PooledHashSet<ILocalSymbol>.GetInstance();
            var analysisData = ReachingDefinitionsBlockAnalysisData.GetInstance();
            try
            {
                Walker.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(rootOperation), analysisData, unusedDefinitions, referencedLocals);
                return new UnusedDefinitionsResult(unusedDefinitions.ToImmutableHashSet(), referencedLocals.ToImmutableHashSet());
            }
            finally
            {
                unusedDefinitions.Free();
                referencedLocals.Free();
                analysisData.Free();
            }
        }

        protected override void Dispose(bool disposing)
        {
            _unusedDefinitions.Free();
            _referencedLocals.Free();
            _currentBlockAnalysisData.Free();
        }

        private ReachingDefinitionsBlockAnalysisData GetOrCreateBlockData(BasicBlock basicBlock)
        {
            if (_reachingDefinitionsMap[basicBlock] == null)
            {
                _reachingDefinitionsMap[basicBlock] = ReachingDefinitionsBlockAnalysisData.GetInstance();
            }

            return _reachingDefinitionsMap[basicBlock];
        }

        protected override ReachingDefinitionsBlockAnalysisData AnalyzeBlock(BasicBlock basicBlock)
        {
            _currentBlockAnalysisData.SetAnalysisDataFrom(GetOrCreateBlockData(basicBlock));
            Walker.AnalyzeOperationsAndUpdateData(basicBlock.Operations, _currentBlockAnalysisData, _unusedDefinitions, _referencedLocals);
            return _currentBlockAnalysisData;
        }

        protected override ReachingDefinitionsBlockAnalysisData AnalyzeNonConditionalBranch(BasicBlock basicBlock, ReachingDefinitionsBlockAnalysisData currentAnalysisData)
        {
            Walker.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(basicBlock.BranchValue), currentAnalysisData, _unusedDefinitions, _referencedLocals);
            return currentAnalysisData;
        }

        protected override (ReachingDefinitionsBlockAnalysisData fallThroughSuccessorData, ReachingDefinitionsBlockAnalysisData conditionalSuccessorData) AnalyzeConditionalBranch(BasicBlock basicBlock, ReachingDefinitionsBlockAnalysisData currentAnalysisData)
        {
            Walker.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(basicBlock.BranchValue), currentAnalysisData, _unusedDefinitions, _referencedLocals);
            return (currentAnalysisData, currentAnalysisData);
        }

        protected override ReachingDefinitionsBlockAnalysisData GetCurrentAnalysisData(BasicBlock basicBlock)
            => _reachingDefinitionsMap[basicBlock];

        protected override ReachingDefinitionsBlockAnalysisData GetInitialAnalysisData()
            => ReachingDefinitionsBlockAnalysisData.GetInstance();

        protected override void SetCurrentAnalysisData(BasicBlock basicBlock, ReachingDefinitionsBlockAnalysisData data)
        {
            GetOrCreateBlockData(basicBlock).SetAnalysisDataFrom(data);
            if (!ReferenceEquals(data, _currentBlockAnalysisData))
            {
                data.Free();
            }
        }

        protected override bool IsEqual(ReachingDefinitionsBlockAnalysisData analysisData1, ReachingDefinitionsBlockAnalysisData analysisData2)
            => analysisData1 == null ? analysisData2 == null : analysisData1.Equals(analysisData2);

        protected override ReachingDefinitionsBlockAnalysisData Merge(ReachingDefinitionsBlockAnalysisData analysisData1, ReachingDefinitionsBlockAnalysisData analysisData2)
            => ReachingDefinitionsBlockAnalysisData.Merge(analysisData1, analysisData2);
    }

    internal sealed class UnusedDefinitionsResult
    {
        public UnusedDefinitionsResult(ImmutableHashSet<(ISymbol, IOperation)> unusedDefinitions, ImmutableHashSet<ILocalSymbol> referencedLocals)
        {
            UnusedDefinitions = unusedDefinitions;
            ReferencedLocals = referencedLocals;
        }

        public ImmutableHashSet<(ISymbol Symbol, IOperation Definition)> UnusedDefinitions { get; }
        public ImmutableHashSet<ILocalSymbol> ReferencedLocals { get; }
    }
}
