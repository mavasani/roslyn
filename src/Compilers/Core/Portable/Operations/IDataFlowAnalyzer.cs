// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
#if WORKSPACE
    using TBasicBlock = BasicBlock;
#else
    using TBasicBlock = BasicBlockBuilder;
#endif

    internal interface IDataFlowAnalyzer<TBlockAnalysisData> : IDisposable
    {
        TBlockAnalysisData GetEmptyAnalysisData();
        TBlockAnalysisData GetCurrentAnalysisData(TBasicBlock basicBlock);
        void SetCurrentAnalysisData(TBasicBlock basicBlock, TBlockAnalysisData data);
        TBlockAnalysisData AnalyzeBlock(TBasicBlock basicBlock, CancellationToken cancellationToken);
        TBlockAnalysisData AnalyzeNonConditionalBranch(TBasicBlock basicBlock, TBlockAnalysisData currentAnalysisData, CancellationToken cancellationToken);
        (TBlockAnalysisData fallThroughSuccessorData, TBlockAnalysisData conditionalSuccessorData) AnalyzeConditionalBranch(
            TBasicBlock basicBlock,
            TBlockAnalysisData currentAnalysisData,
            CancellationToken cancellationToken);
        TBlockAnalysisData Merge(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2, CancellationToken cancellationToken);
        bool IsEqual(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2);
        bool AnalyzeUnreachableBlocks { get; }
    }
}

