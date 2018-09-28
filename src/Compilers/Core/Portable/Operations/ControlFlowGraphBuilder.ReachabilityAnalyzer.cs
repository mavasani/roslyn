// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
    internal sealed partial class ControlFlowGraphBuilder
    {
        internal sealed class ReachabilityAnalyzer : IDataFlowAnalyzer<bool, BasicBlockBuilder, BasicBlockBuilder.Branch>
        {
            private BitVector _visited = BitVector.Empty;
            private ReachabilityAnalyzer() { }

            public static void Run(ArrayBuilder<BasicBlockBuilder> blocks)
                => CustomDataFlowAnalysis<BasicBlockBuilder, BasicBlockBuilder.Branch, ReachabilityAnalyzer, bool>.Run(blocks, new ReachabilityAnalyzer());

            public bool AnalyzeUnreachableBlocks => false;

            public bool AnalyzeBlock(BasicBlockBuilder basicBlock)
            {
                SetCurrentAnalysisData(basicBlock, isReachable: true);
                return true;
            }

            public void SetCurrentAnalysisData(BasicBlockBuilder basicBlock, bool isReachable)
            {
                _visited[basicBlock.Ordinal] = isReachable;
                basicBlock.IsReachable = isReachable;
            }

            public bool GetCurrentAnalysisData(BasicBlockBuilder basicBlock) => _visited[basicBlock.Ordinal];

            public bool GetInitialAnalysisData() => false;

            public bool Merge(bool analysisData1, bool analysisData2) => analysisData1 || analysisData2;

            public bool IsEqual(bool analysisData1, bool analysisData2) => analysisData1 == analysisData2;

            public (bool fallThroughSuccessorData, bool conditionalSuccessorData) SplitForConditionalBranch(BasicBlockBuilder basicBlock, bool currentAnalysisData)
                => (currentAnalysisData, currentAnalysisData);
        }
    }
}
