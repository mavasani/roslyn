// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
    internal interface IDataFlowAnalyzer<TBlockAnalysisData, TBasicBlock, TControlFlowBranch>
    {
        TBlockAnalysisData GetInitialAnalysisData();
        TBlockAnalysisData GetCurrentAnalysisData(TBasicBlock basicBlock);
        void SetCurrentAnalysisData(TBasicBlock basicBlock, TBlockAnalysisData data);
        TBlockAnalysisData AnalyzeBlock(TBasicBlock basicBlock);
        (TBlockAnalysisData fallThroughSuccessorData, TBlockAnalysisData conditionalSuccessorData) SplitForConditionalBranch(TBasicBlock basicBlock, TBlockAnalysisData currentAnalysisData);
        TBlockAnalysisData Merge(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2);
        bool IsEqual(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2);
        bool AnalyzeUnreachableBlocks { get; }
    }
}

