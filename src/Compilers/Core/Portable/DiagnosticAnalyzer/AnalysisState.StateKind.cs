// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal partial class AnalysisState
    {
        /// <summary>
        /// State kind of per-analyzer <see cref="AnalyzerStateData"/> tracking an analyzer's partial analysis state.
        /// </summary>
        internal enum StateKind
        {
            /// <summary>
            /// Ready for processing.
            /// </summary>
            Ready,

            /// <summary>
            /// Currently being processed.
            /// </summary>
            InProcess
        }
    }
}
