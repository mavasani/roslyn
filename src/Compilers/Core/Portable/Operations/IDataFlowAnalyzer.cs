// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
#if WORKSPACE
    using TBasicBlock = BasicBlock;
#else
    using TBasicBlock = BasicBlockBuilder;
#endif

    internal interface IDataFlowAnalyzer<TBlockAnalysisData> : IDisposable
    {
        TBlockAnalysisData GetInitialAnalysisData();
        TBlockAnalysisData GetCurrentAnalysisData(TBasicBlock basicBlock);
        void SetCurrentAnalysisData(TBasicBlock basicBlock, TBlockAnalysisData data);
        TBlockAnalysisData AnalyzeBlock(TBasicBlock basicBlock);
        TBlockAnalysisData AnalyzeNonConditionalBranch(TBasicBlock basicBlock, TBlockAnalysisData currentAnalysisData);
        (TBlockAnalysisData fallThroughSuccessorData, TBlockAnalysisData conditionalSuccessorData) AnalyzeConditionalBranch(TBasicBlock basicBlock, TBlockAnalysisData currentAnalysisData);
        TBlockAnalysisData Merge(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2);
        bool IsEqual(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2);
        bool AnalyzeUnreachableBlocks { get; }
    }
}

