// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
    internal abstract class AbstractDataFlowAnalyzer<TBlockAnalysisData> : IDataFlowAnalyzer<TBlockAnalysisData>
    {
        protected abstract TBlockAnalysisData AnalyzeBlock(BasicBlock basicBlock);
        protected abstract TBlockAnalysisData AnalyzeNonConditionalBranch(BasicBlock basicBlock, TBlockAnalysisData currentAnalysisData);
        protected abstract (TBlockAnalysisData fallThroughSuccessorData, TBlockAnalysisData conditionalSuccessorData) AnalyzeConditionalBranch(BasicBlock basicBlock, TBlockAnalysisData currentAnalysisData);
        protected abstract TBlockAnalysisData GetCurrentAnalysisData(BasicBlock basicBlock);
        protected abstract TBlockAnalysisData GetInitialAnalysisData();
        protected abstract bool IsEqual(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2);
        protected abstract TBlockAnalysisData Merge(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2);
        protected abstract void SetCurrentAnalysisData(BasicBlock basicBlock, TBlockAnalysisData data);

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        void System.IDisposable.Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion

        #region IDataFlowAnalyzer implementation
        // We always analyze unreachable blocks.
        bool IDataFlowAnalyzer<TBlockAnalysisData>.AnalyzeUnreachableBlocks => true;

        TBlockAnalysisData IDataFlowAnalyzer<TBlockAnalysisData>.AnalyzeBlock(BasicBlock basicBlock)
            => this.AnalyzeBlock(basicBlock);

        TBlockAnalysisData IDataFlowAnalyzer<TBlockAnalysisData>.AnalyzeNonConditionalBranch(BasicBlock basicBlock, TBlockAnalysisData currentAnalysisData)
            => this.AnalyzeNonConditionalBranch(basicBlock, currentAnalysisData);

        (TBlockAnalysisData fallThroughSuccessorData, TBlockAnalysisData conditionalSuccessorData) IDataFlowAnalyzer<TBlockAnalysisData>.AnalyzeConditionalBranch(BasicBlock basicBlock, TBlockAnalysisData currentAnalysisData)
            => this.AnalyzeConditionalBranch(basicBlock, currentAnalysisData);

        TBlockAnalysisData IDataFlowAnalyzer<TBlockAnalysisData>.GetCurrentAnalysisData(BasicBlock basicBlock)
            => this.GetCurrentAnalysisData(basicBlock);

        TBlockAnalysisData IDataFlowAnalyzer<TBlockAnalysisData>.GetInitialAnalysisData()
            => this.GetInitialAnalysisData();

        bool IDataFlowAnalyzer<TBlockAnalysisData>.IsEqual(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2)
            => this.IsEqual(analysisData1, analysisData2);

        TBlockAnalysisData IDataFlowAnalyzer<TBlockAnalysisData>.Merge(TBlockAnalysisData analysisData1, TBlockAnalysisData analysisData2)
            => this.Merge(analysisData1, analysisData2);

        void IDataFlowAnalyzer<TBlockAnalysisData>.SetCurrentAnalysisData(BasicBlock basicBlock, TBlockAnalysisData data)
            => this.SetCurrentAnalysisData(basicBlock, data);
        #endregion
    }
}
