// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal static partial class ReachingDefinitionsAnalysis
    {
        private sealed partial class DataFlowAnalyzer : AbstractDataFlowAnalyzer<BasicBlockAnalysisData>
        {
            private readonly FlowGraphAnalysisData _analysisData;
            private readonly CancellationToken _cancellationToken;

            private DataFlowAnalyzer(ControlFlowGraph cfg, ISymbol owningSymbol, CancellationToken cancellationToken)
            {
                _analysisData = FlowGraphAnalysisData.Create(cfg, owningSymbol, AnalyzeLocalFunction, AnalyzeLambda);
                _cancellationToken = cancellationToken;
            }

            private DataFlowAnalyzer(
                ControlFlowGraph cfg,
                IMethodSymbol lambdaOrLocalFunction,
                FlowGraphAnalysisData parentAnalysisData,
                CancellationToken cancellationToken)
            {
                _analysisData = FlowGraphAnalysisData.Create(cfg, lambdaOrLocalFunction, parentAnalysisData);
                _cancellationToken = cancellationToken;

                var entryBlockAnalysisData = GetEmptyAnalysisData();
                entryBlockAnalysisData.SetAnalysisDataFrom(parentAnalysisData.CurrentBlockAnalysisData);
                _analysisData.SetBlockAnalysisData(cfg.EntryBlock(), entryBlockAnalysisData);
            }

            public static DefinitionUsageResult RunAnalysis(ControlFlowGraph cfg, ISymbol owningSymbol, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var analyzer = new DataFlowAnalyzer(cfg, owningSymbol, cancellationToken))
                {
                    _ = CustomDataFlowAnalysis<DataFlowAnalyzer, BasicBlockAnalysisData>.Run(cfg.Blocks, analyzer, cancellationToken);
                    return analyzer._analysisData.ToResult();
                }
            }

            protected override void Dispose(bool disposing)
                => _analysisData.Dispose();

            private BasicBlockAnalysisData AnalyzeLocalFunction(IMethodSymbol localFunction)
            {
                Debug.Assert(localFunction.IsLocalFunction());

                _cancellationToken.ThrowIfCancellationRequested();

                var cfg = _analysisData.ControlFlowGraph.GetLocalFunctionControlFlowGraphInScope(localFunction, _cancellationToken);
                _cancellationToken.ThrowIfCancellationRequested();
                using (var analyzer = new DataFlowAnalyzer(cfg, localFunction, _analysisData, _cancellationToken))
                {
                    return CustomDataFlowAnalysis<DataFlowAnalyzer, BasicBlockAnalysisData>.Run(cfg.Blocks, analyzer, _cancellationToken);
                }
            }

            private BasicBlockAnalysisData AnalyzeLambda(IOperation operation)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var flowAnonymousFunction = (IFlowAnonymousFunctionOperation)operation;
                var cfg = _analysisData.ControlFlowGraph.GetAnonymousFunctionControlFlowGraphInScope(flowAnonymousFunction, _cancellationToken);

                _cancellationToken.ThrowIfCancellationRequested();
                using (var analyzer = new DataFlowAnalyzer(cfg, flowAnonymousFunction.Symbol, _analysisData, _cancellationToken))
                {
                    return CustomDataFlowAnalysis<DataFlowAnalyzer, BasicBlockAnalysisData>.Run(cfg.Blocks, analyzer, _cancellationToken);
                }
            }

            // Don't analyze blocks which are unreachable, as any definition
            // in such a block which has a read outside will be marked redundant.
            // For example,
            //      int x;
            //      if (true)
            //          x = 0;
            //      else
            //          x = 1; // This will be marked redundant if "AnalyzeUnreachableBlocks = true"
            //      return x;
            protected override bool AnalyzeUnreachableBlocks => false;

            protected override BasicBlockAnalysisData AnalyzeBlock(BasicBlock basicBlock, CancellationToken cancellationToken)
            {
                BeforeBlockAnalysis();
                Walker.AnalyzeOperationsAndUpdateData(basicBlock.Operations, _analysisData, cancellationToken);
                AfterBlockAnalysis();
                return _analysisData.CurrentBlockAnalysisData;

                // Local functions.
                void BeforeBlockAnalysis()
                {
                    _analysisData.SetCurrentBlockAnalysisDataFrom(basicBlock);
                    if (basicBlock.Kind == BasicBlockKind.Entry)
                    {
                        _analysisData.SetAnalysisDataOnEntryBlockStart();
                    }
                }

                void AfterBlockAnalysis()
                {
                    if (basicBlock.Kind == BasicBlockKind.Exit)
                    {
                        _analysisData.SetAnalysisDataOnExitBlockEnd();
                    }
                }
            }

            protected override BasicBlockAnalysisData AnalyzeNonConditionalBranch(
                BasicBlock basicBlock,
                BasicBlockAnalysisData currentBlockAnalysisData,
                CancellationToken cancellationToken)
                => AnalyzeBranch(basicBlock, currentBlockAnalysisData, cancellationToken);

            protected override (BasicBlockAnalysisData fallThroughSuccessorData, BasicBlockAnalysisData conditionalSuccessorData) AnalyzeConditionalBranch(
                BasicBlock basicBlock,
                BasicBlockAnalysisData currentAnalysisData,
                CancellationToken cancellationToken)
            {
                var resultAnalysisData = AnalyzeBranch(basicBlock, currentAnalysisData, cancellationToken);
                return (resultAnalysisData, resultAnalysisData);
            }

            private BasicBlockAnalysisData AnalyzeBranch(
                BasicBlock basicBlock,
                BasicBlockAnalysisData currentBlockAnalysisData,
                CancellationToken cancellationToken)
            {
                _analysisData.SetCurrentBlockAnalysisDataFrom(currentBlockAnalysisData);
                var operations = SpecializedCollections.SingletonEnumerable(basicBlock.BranchValue);
                Walker.AnalyzeOperationsAndUpdateData(operations, _analysisData, cancellationToken);
                return _analysisData.CurrentBlockAnalysisData;
            }

            protected override BasicBlockAnalysisData GetCurrentAnalysisData(BasicBlock basicBlock)
                => _analysisData.GetCurrentAnalysisData(basicBlock);

            protected override BasicBlockAnalysisData GetEmptyAnalysisData()
                => _analysisData.CreateBasicBlockAnalysisData();

            protected override void SetCurrentAnalysisData(BasicBlock basicBlock, BasicBlockAnalysisData data)
                => _analysisData.SetBlockAnalysisDataFrom(basicBlock, data);

            protected override bool IsEqual(BasicBlockAnalysisData analysisData1, BasicBlockAnalysisData analysisData2)
                => analysisData1 == null ? analysisData2 == null : analysisData1.Equals(analysisData2);

            protected override BasicBlockAnalysisData Merge(
                BasicBlockAnalysisData analysisData1,
                BasicBlockAnalysisData analysisData2,
                CancellationToken cancellationToken)
                => BasicBlockAnalysisData.Merge(analysisData1, analysisData2, GetEmptyAnalysisData);
        }
    }
}
