// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal static partial class ReachingDefinitionsAnalysis
    {
        private sealed partial class DataFlowAnalyzer : AbstractDataFlowAnalyzer<BasicBlockAnalysisData>
        {
            private sealed class FlowGraphAnalysisData : AnalysisData
            {
                private readonly ImmutableArray<IParameterSymbol> _parameters;
                private readonly PooledDictionary<BasicBlock, BasicBlockAnalysisData> _reachingDefinitionsMap;
                private readonly PooledDictionary<CaptureId, PooledHashSet<(ISymbol, IOperation)>> _lValueFlowCapturesMap;

                private FlowGraphAnalysisData(
                    ControlFlowGraph controlFlowGraph,
                    ImmutableArray<IParameterSymbol> parameters,
                    PooledDictionary<BasicBlock, BasicBlockAnalysisData> reachingDefinitionsMap,
                    PooledDictionary<(ISymbol symbol, IOperation operation), bool> definitionUsageMap,
                    PooledHashSet<ISymbol> symbolsRead,
                    Func<IMethodSymbol, BasicBlockAnalysisData> analyzeLocalFunction,
                    Func<IOperation, BasicBlockAnalysisData> analyzeLambda,
                    PooledDictionary<IOperation, PooledHashSet<IOperation>> reachingDelegateCreationTargets)
                    : base(definitionUsageMap, symbolsRead,
                           analyzeLocalFunction, analyzeLambda,
                           reachingDelegateCreationTargets)
                {
                    ControlFlowGraph = controlFlowGraph;
                    _parameters = parameters;
                    _reachingDefinitionsMap = reachingDefinitionsMap;

                    _lValueFlowCapturesMap = PooledDictionary<CaptureId, PooledHashSet<(ISymbol, IOperation)>>.GetInstance();
                    LValueFlowCapturesInGraph = LValueFlowCapturesProvider.GetOrCreateLValueFlowCaptures(controlFlowGraph);
                }

                public static FlowGraphAnalysisData Create(
                    ControlFlowGraph cfg,
                    ISymbol owningSymbol,
                    Func<IMethodSymbol, BasicBlockAnalysisData> analyzeLocalFunction,
                    Func<IOperation, BasicBlockAnalysisData> analyzeLambda)
                {
                    Debug.Assert(cfg.Parent == null);

                    var parameters = owningSymbol.GetParameters();
                    return new FlowGraphAnalysisData(
                        cfg,
                        parameters,
                        reachingDefinitionsMap: CreateReachingDefinitionsMap(cfg),
                        definitionUsageMap: CreateDefinitionsUsageMap(parameters),
                        symbolsRead: PooledHashSet<ISymbol>.GetInstance(),
                        analyzeLocalFunction,
                        analyzeLambda,
                        reachingDelegateCreationTargets: PooledDictionary<IOperation, PooledHashSet<IOperation>>.GetInstance());
                }

                public static FlowGraphAnalysisData Create(
                    ControlFlowGraph cfg,
                    IMethodSymbol lambdaOrLocalFunction,
                    FlowGraphAnalysisData parentAnalysisData)
                {
                    Debug.Assert(cfg.Parent != null);
                    Debug.Assert(lambdaOrLocalFunction.IsAnonymousFunction() || lambdaOrLocalFunction.IsLocalFunction());
                    Debug.Assert(parentAnalysisData != null);

                    var parameters = lambdaOrLocalFunction.GetParameters();
                    return new FlowGraphAnalysisData(
                        cfg,
                        parameters,
                        reachingDefinitionsMap: CreateReachingDefinitionsMap(cfg),
                        definitionUsageMap: UpdateDefinitionsUsageMap(parentAnalysisData.DefinitionUsageMapBuilder, parameters),
                        symbolsRead: parentAnalysisData.SymbolsReadBuilder,
                        analyzeLocalFunction: parentAnalysisData.AnalyzeLocalFunction,
                        analyzeLambda: parentAnalysisData.AnalyzeLambda,
                        reachingDelegateCreationTargets: parentAnalysisData.ReachingDelegateCreationTargets);
                }

                private static PooledDictionary<BasicBlock, BasicBlockAnalysisData> CreateReachingDefinitionsMap(
                    ControlFlowGraph cfg)
                {
                    var builder = PooledDictionary<BasicBlock, BasicBlockAnalysisData>.GetInstance();
                    foreach (var block in cfg.Blocks)
                    {
                        builder.Add(block, null);
                    }

                    return builder;
                }

                public ControlFlowGraph ControlFlowGraph { get; }

                /// <summary>
                /// Flow captures for l-value or address captures.
                /// </summary>
                public ImmutableHashSet<CaptureId> LValueFlowCapturesInGraph { get; }

                public BasicBlockAnalysisData GetCurrentAnalysisData(BasicBlock basicBlock)
                    => _reachingDefinitionsMap[basicBlock];

                public BasicBlockAnalysisData GetOrCreateBlockData(BasicBlock basicBlock)
                {
                    if (_reachingDefinitionsMap[basicBlock] == null)
                    {
                        _reachingDefinitionsMap[basicBlock] = CreateBasicBlockAnalysisData();
                    }

                    return _reachingDefinitionsMap[basicBlock];
                }

                public void SetCurrentBlockAnalysisDataFrom(BasicBlock basicBlock)
                    => SetCurrentBlockAnalysisDataFrom(GetOrCreateBlockData(basicBlock));

                public void SetAnalysisDataOnEntryBlockStart()
                {
                    foreach (var parameter in _parameters)
                    {
                        DefinitionUsageMapBuilder[(parameter, null)] = false;
                        CurrentBlockAnalysisData.OnWriteReferenceFound(parameter, operation: null, maybeWritten: false);
                    }
                }

                public void SetBlockAnalysisData(BasicBlock basicBlock, BasicBlockAnalysisData data)
                    => _reachingDefinitionsMap[basicBlock] = data;

                public void SetBlockAnalysisDataFrom(BasicBlock basicBlock, BasicBlockAnalysisData data)
                    => GetOrCreateBlockData(basicBlock).SetAnalysisDataFrom(data);

                public void SetAnalysisDataOnExitBlockEnd()
                {
                    if (DefinitionUsageMapBuilder.Count == 0)
                    {
                        return;
                    }

                    // Mark all reachable definitions for ref/out parameters at end of exit block as used.
                    foreach (var parameter in _parameters)
                    {
                        if (parameter.RefKind == RefKind.Ref || parameter.RefKind == RefKind.Out)
                        {
                            var currentDefinitions = CurrentBlockAnalysisData.GetCurrentDefinitions(parameter);
                            foreach (var definition in currentDefinitions)
                            {
                                if (definition != null)
                                {
                                    DefinitionUsageMapBuilder[(parameter, definition)] = true;
                                }
                            }
                        }
                    }
                }

                public override bool IsLValueFlowCapture(CaptureId captureId)
                    => LValueFlowCapturesInGraph.Contains(captureId);

                public override void OnLValueCaptureFound(ISymbol symbol, IOperation operation, CaptureId captureId)
                {
                    if (!_lValueFlowCapturesMap.TryGetValue(captureId, out var captures))
                    {
                        captures = PooledHashSet<(ISymbol, IOperation)>.GetInstance();
                        _lValueFlowCapturesMap.Add(captureId, captures);
                    }

                    captures.Add((symbol, operation));
                }

                public override void OnLValueDereferenceFound(CaptureId captureId)
                {
                    var captures = _lValueFlowCapturesMap[captureId];
                    var mayBeWritten = captures.Count > 1;
                    foreach (var (symbol, definition) in captures)
                    {
                        OnWriteReferenceFound(symbol, definition, mayBeWritten);
                    }
                }

                protected override void DisposeCoreData()
                {
                    // We share the base analysis data structures between primary method's flow graph analysis
                    // and it's inner lambda/local function flow graph analysis.
                    // Dispose the base data structures only for primary method's flow analysis data.
                    if (ControlFlowGraph.Parent == null)
                    {
                        base.DisposeCoreData();
                    }

                    // Note the base type already disposes the BasicBlockAnalysisData values
                    // allocated by us, so we only need to free the map.
                    _reachingDefinitionsMap.Free();

                    foreach (var captures in _lValueFlowCapturesMap.Values)
                    {
                        captures.Free();
                    }
                    _lValueFlowCapturesMap.Free();
                }
            }
        }
    }
}
