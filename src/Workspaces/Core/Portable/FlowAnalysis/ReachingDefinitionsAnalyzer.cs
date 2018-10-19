// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal sealed partial class ReachingDefinitionsAnalyzer : AbstractDataFlowAnalyzer<ReachingDefinitionsBlockAnalysisData>
    {
        /// <summary>
        /// Map from each definition to a boolean indicating if the value assinged
        /// at definition is used/read on some control flow path.
        /// </summary>
        private readonly PooledDictionary<(ISymbol Symbol, IOperation Definition), bool> _definitionUsageMap;

        /// <summary>
        /// Set of locals/parameters that have at least one use/read for one of its definitions.
        /// </summary>
        private readonly PooledHashSet<ISymbol> _referencedSymbols;

        private readonly ReachingDefinitionsBlockAnalysisData _currentBlockAnalysisData;
        private readonly Dictionary<BasicBlock, ReachingDefinitionsBlockAnalysisData> _reachingDefinitionsMap;
        private readonly ImmutableArray<IParameterSymbol> _parameters;

        private ReachingDefinitionsAnalyzer(ControlFlowGraph cfg, ISymbol owningSymbol)
        {
            _referencedSymbols = PooledHashSet<ISymbol>.GetInstance();
            _currentBlockAnalysisData = ReachingDefinitionsBlockAnalysisData.GetInstance();
            _parameters = owningSymbol.GetParameters();
            _definitionUsageMap = CreateDefinitionsUsageMap(_parameters);
            _reachingDefinitionsMap = CreateReachingDefinitionsMap(cfg);
        }

        private static PooledDictionary<(ISymbol Symbol, IOperation Definition), bool> CreateDefinitionsUsageMap(
            ImmutableArray<IParameterSymbol> parameters)
        {
            var definitionUsageMap = PooledDictionary<(ISymbol Symbol, IOperation Definition), bool>.GetInstance();

            foreach (var parameter in parameters)
            {
                definitionUsageMap[(parameter, null)] = false;
            }

            return definitionUsageMap;
        }

        private static Dictionary<BasicBlock, ReachingDefinitionsBlockAnalysisData> CreateReachingDefinitionsMap(
            ControlFlowGraph cfg)
        {
            var builder = new Dictionary<BasicBlock, ReachingDefinitionsBlockAnalysisData>(cfg.Blocks.Length);
            foreach (var block in cfg.Blocks)
            {
                builder.Add(block, null);
            }

            return builder;
        }

        public static DefinitionUsageResult AnalyzeAndGetUnusedDefinitions(ControlFlowGraph cfg, ISymbol owningSymbol)
        {
            using (var analyzer = new ReachingDefinitionsAnalyzer(cfg, owningSymbol))
            {
                _ = CustomDataFlowAnalysis<ReachingDefinitionsAnalyzer, ReachingDefinitionsBlockAnalysisData>.Run(cfg.Blocks, analyzer);
                return new DefinitionUsageResult(analyzer._definitionUsageMap.ToImmutableDictionary(), analyzer._referencedSymbols.ToImmutableHashSet());
            }
        }

        public static DefinitionUsageResult AnalyzeAndGetUnusedDefinitions(IOperation rootOperation, ISymbol owningSymbol)
        {
            var definitionUsageMap = CreateDefinitionsUsageMap(owningSymbol.GetParameters());
            var referencedSymbols = PooledHashSet<ISymbol>.GetInstance();
            var analysisData = ReachingDefinitionsBlockAnalysisData.GetInstance();
            try
            {
                Walker.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(rootOperation), analysisData, definitionUsageMap, referencedSymbols);
                return new DefinitionUsageResult(definitionUsageMap.ToImmutableDictionary(), referencedSymbols.ToImmutableHashSet());
            }
            finally
            {
                definitionUsageMap.Free();
                referencedSymbols.Free();
                analysisData.Free();
            }
        }

        protected override void Dispose(bool disposing)
        {
            _definitionUsageMap.Free();
            _referencedSymbols.Free();
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
            BeforeBlockAnalysis();
            Walker.AnalyzeOperationsAndUpdateData(basicBlock.Operations, _currentBlockAnalysisData, _definitionUsageMap, _referencedSymbols);
            AfterBlockAnalysis();
            return _currentBlockAnalysisData;

            // Local functions.
            void BeforeBlockAnalysis()
            {
                _currentBlockAnalysisData.SetAnalysisDataFrom(GetOrCreateBlockData(basicBlock));
                if (basicBlock.Kind == BasicBlockKind.Entry)
                {
                    foreach (var parameter in _parameters)
                    {
                        _definitionUsageMap[(parameter, null)] = false;
                        _currentBlockAnalysisData.OnWriteReferenceFound(parameter, operation: null, maybeWritten: false);
                    }
                }
            }

            void AfterBlockAnalysis()
            {
                // Mark all reachable definitions for ref/out parameters at end of exit block as used.
                if (basicBlock.Kind == BasicBlockKind.Exit && _definitionUsageMap.Count != 0)
                {
                    foreach (var parameter in _parameters)
                    {
                        if (parameter.RefKind == RefKind.Ref || parameter.RefKind == RefKind.Out)
                        {
                            var currentDefinitions = _currentBlockAnalysisData.GetCurrentDefinitions(parameter);
                            foreach (var definition in currentDefinitions)
                            {
                                if (definition != null)
                                {
                                    _definitionUsageMap[(parameter, definition)] = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        protected override ReachingDefinitionsBlockAnalysisData AnalyzeNonConditionalBranch(BasicBlock basicBlock, ReachingDefinitionsBlockAnalysisData currentAnalysisData)
        {
            Walker.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(basicBlock.BranchValue), currentAnalysisData, _definitionUsageMap, _referencedSymbols);
            return currentAnalysisData;
        }

        protected override (ReachingDefinitionsBlockAnalysisData fallThroughSuccessorData, ReachingDefinitionsBlockAnalysisData conditionalSuccessorData) AnalyzeConditionalBranch(BasicBlock basicBlock, ReachingDefinitionsBlockAnalysisData currentAnalysisData)
        {
            Walker.AnalyzeOperationsAndUpdateData(SpecializedCollections.SingletonEnumerable(basicBlock.BranchValue), currentAnalysisData, _definitionUsageMap, _referencedSymbols);
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

    internal sealed class DefinitionUsageResult
    {
        public DefinitionUsageResult(ImmutableDictionary<(ISymbol Symbol, IOperation Definition), bool> definitionUsageMap, ImmutableHashSet<ISymbol> referencedSymbols)
        {
            DefinitionUsageMap = definitionUsageMap;
            ReferencedSymbols = referencedSymbols;
        }

        public ImmutableDictionary<(ISymbol Symbol, IOperation Definition), bool> DefinitionUsageMap { get; }
        public ImmutableHashSet<ISymbol> ReferencedSymbols { get; }

        public IEnumerable<(ISymbol Symbol, IOperation Definition)> GetUnusedDefinitions()
        {
            foreach (var kvp in DefinitionUsageMap)
            {
                if (!kvp.Value)
                {
                    yield return kvp.Key;
                }
            }
        }
    }
}
