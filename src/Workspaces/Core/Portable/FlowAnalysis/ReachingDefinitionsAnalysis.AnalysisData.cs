// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal static partial class ReachingDefinitionsAnalysis
    {
        private abstract class AnalysisData : IDisposable
        {
            private readonly ArrayBuilder<BasicBlockAnalysisData> _allocatedBasicBlockAnalysisDatas;
            
            protected AnalysisData(
                PooledDictionary<(ISymbol symbol, IOperation operation), bool> definitionUsageMap,
                PooledHashSet<ISymbol> symbolsRead,
                Func<IMethodSymbol, BasicBlockAnalysisData> analyzeLocalFunction,
                Func<IOperation, BasicBlockAnalysisData> analyzeLambda,
                PooledDictionary<IOperation, PooledHashSet<IOperation>> reachingDelegateCreationTargets)
            {
                DefinitionUsageMapBuilder = definitionUsageMap;
                SymbolsReadBuilder = symbolsRead;
                AnalyzeLocalFunction = analyzeLocalFunction;
                AnalyzeLambda = analyzeLambda;
                ReachingDelegateCreationTargets = reachingDelegateCreationTargets;
                _allocatedBasicBlockAnalysisDatas = ArrayBuilder<BasicBlockAnalysisData>.GetInstance();
                CurrentBlockAnalysisData = CreateBasicBlockAnalysisData();
            }

            /// <summary>
            /// Map from each definition to a boolean indicating if the value assinged
            /// at definition is used/read on some control flow path.
            /// </summary>
            protected PooledDictionary<(ISymbol symbol, IOperation operation), bool> DefinitionUsageMapBuilder { get; }

            /// <summary>
            /// Set of locals/parameters that have at least one use/read for one of its definitions.
            /// </summary>
            protected PooledHashSet<ISymbol> SymbolsReadBuilder { get; }

            /// <summary>
            /// Current block analysis data.
            /// </summary>
            public BasicBlockAnalysisData CurrentBlockAnalysisData { get; }

            public Func<IMethodSymbol, BasicBlockAnalysisData> AnalyzeLocalFunction { get; }
            public Func<IOperation, BasicBlockAnalysisData> AnalyzeLambda { get; }
            protected PooledDictionary<IOperation, PooledHashSet<IOperation>> ReachingDelegateCreationTargets { get; }

            public abstract bool IsLValueFlowCapture(CaptureId captureId);
            public abstract void OnLValueCaptureFound(ISymbol symbol, IOperation operation, CaptureId captureId);
            public abstract void OnLValueDereferenceFound(CaptureId captureId);

            public DefinitionUsageResult ToResult()
                => new DefinitionUsageResult(DefinitionUsageMapBuilder.ToImmutableDictionary(),
                                             SymbolsReadBuilder.ToImmutableHashSet());

            public void SetReachingDelegateCreationTargetsFromSymbol(IOperation definition, ISymbol symbol)
            {
                var targetsBuilder = PooledHashSet<IOperation>.GetInstance();
                foreach (var symbolDefinition in CurrentBlockAnalysisData.GetCurrentDefinitions(symbol))
                {
                    if (symbolDefinition == null)
                    {
                        continue;
                    }

                    if (!ReachingDelegateCreationTargets.TryGetValue(symbolDefinition, out var targetsBuilderForDefinition))
                    {
                        // Unable to find delegate creation targets for this symbol definition.
                        // Bail out without setting targets.
                        targetsBuilder.Free();
                        return;
                    }
                    else
                    {
                        foreach (var target in targetsBuilderForDefinition)
                        {
                            targetsBuilder.Add(target);
                        }
                    }
                }

                ReachingDelegateCreationTargets[definition] = targetsBuilder;
            }

            public void SetReachingDelegateCreationTarget(IOperation definition, IOperation target)
            {
                var targetsBuilder = PooledHashSet<IOperation>.GetInstance();
                targetsBuilder.Add(target);
                ReachingDelegateCreationTargets[definition] = targetsBuilder;
            }

            public bool TryGetReachingDelegateCreationTargets(IOperation definition, out ImmutableHashSet<IOperation> targets)
            {
                if (ReachingDelegateCreationTargets.TryGetValue(definition, out var targetsBuilder))
                {
                    targets = targetsBuilder.ToImmutableHashSet();
                    return true;
                }

                targets = ImmutableHashSet<IOperation>.Empty;
                return false;
            }

            protected static PooledDictionary<(ISymbol Symbol, IOperation Definition), bool> CreateDefinitionsUsageMap(
                ImmutableArray<IParameterSymbol> parameters)
            {
                var definitionUsageMap = PooledDictionary<(ISymbol Symbol, IOperation Definition), bool>.GetInstance();
                return UpdateDefinitionsUsageMap(definitionUsageMap, parameters);
            }

            protected static PooledDictionary<(ISymbol Symbol, IOperation Definition), bool> UpdateDefinitionsUsageMap(
                PooledDictionary<(ISymbol Symbol, IOperation Definition), bool> definitionUsageMap,
                ImmutableArray<IParameterSymbol> parameters)
            {
                foreach (var parameter in parameters)
                {
                    (ISymbol, IOperation) key = (parameter, null);
                    if (!definitionUsageMap.ContainsKey(key))
                    {
                        definitionUsageMap.Add(key, false);
                    }
                }

                return definitionUsageMap;
            }

            public BasicBlockAnalysisData CreateBasicBlockAnalysisData()
            {
                var instance = BasicBlockAnalysisData.GetInstance();
                _allocatedBasicBlockAnalysisDatas.Add(instance);
                return instance;
            }

            public void OnReadReferenceFound(ISymbol symbol, IOperation operation)
            {
                if (symbol.Kind == SymbolKind.Discard)
                {
                    return;
                }

                if (DefinitionUsageMapBuilder.Count != 0)
                {
                    var currentDefinitions = CurrentBlockAnalysisData.GetCurrentDefinitions(symbol);
                    foreach (var definition in currentDefinitions)
                    {
                        DefinitionUsageMapBuilder[(symbol, definition)] = true;
                    }
                }

                SymbolsReadBuilder.Add(symbol);
            }

            public void OnWriteReferenceFound(ISymbol symbol, IOperation operation, bool maybeWritten)
            {
                var definition = (symbol, operation);
                if (symbol.Kind == SymbolKind.Discard)
                {
                    // Skip discard symbols and also for already processed writes (back edge from loops).
                    return;
                }

                CurrentBlockAnalysisData.OnWriteReferenceFound(symbol, operation, maybeWritten);

                // Only mark as unused definition if we are processing it for the first time (not from back edge for loops)
                if (!DefinitionUsageMapBuilder.ContainsKey(definition) &&
                    !maybeWritten)
                {
                    DefinitionUsageMapBuilder.Add((symbol, operation), false);
                }
            }

            public void ResetState(IOperation operation)
            {
                foreach (var symbol in DefinitionUsageMapBuilder.Keys.Select(d => d.symbol).ToArray())
                {
                    OnReadReferenceFound(symbol, operation);
                }
            }

            public void SetCurrentBlockAnalysisDataFrom(BasicBlockAnalysisData newBlockAnalysisData)
            {
                Debug.Assert(newBlockAnalysisData != null);
                CurrentBlockAnalysisData.SetAnalysisDataFrom(newBlockAnalysisData);
            }

            public void Dispose()
            {
                DisposeAllocatedBasicBlockAnalysisData();
                DisposeCoreData();
            }

            protected virtual void DisposeCoreData()
            {
                DefinitionUsageMapBuilder.Free();
                SymbolsReadBuilder.Free();
                foreach (var creations in ReachingDelegateCreationTargets.Values)
                {
                    creations.Free();
                }
                ReachingDelegateCreationTargets.Free();
            }

            protected void DisposeAllocatedBasicBlockAnalysisData()
            {
                foreach (var instance in _allocatedBasicBlockAnalysisDatas)
                {
                    instance.Free();
                }

                _allocatedBasicBlockAnalysisDatas.Free();
            }
        }
    }
}
