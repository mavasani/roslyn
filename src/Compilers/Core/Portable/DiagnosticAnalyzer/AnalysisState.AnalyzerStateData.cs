// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal partial class AnalysisState
    {
        /// <summary>
        /// Stores the partial analysis state for a specific event/symbol/tree for a specific analyzer.
        /// </summary>
        internal class AnalyzerStateData
        {
            /// <summary>
            /// Current state of analysis.
            /// </summary>
            public StateKind StateKind { get; private set; }

            /// <summary>
            /// Set of completed actions.
            /// </summary>
            public HashSet<AnalyzerAction> ProcessedActions { get; private set; }

            public AnalyzerStateData()
                : this(StateKind.Ready, new HashSet<AnalyzerAction>())
            {
            }

            protected AnalyzerStateData(StateKind stateKind, HashSet<AnalyzerAction> processedActions)
            {
                StateKind = stateKind;
                ProcessedActions = processedActions;
            }

            public virtual AnalyzerStateData WithStateKind(StateKind stateKind)
            {
                Debug.Assert(stateKind != this.StateKind);
                return new AnalyzerStateData(stateKind, this.ProcessedActions);
            }

            /// <summary>
            /// Resets the <see cref="StateKind"/> from <see cref="StateKind.InProcess"/> to <see cref="StateKind.Ready"/>.
            /// This method must be invoked after successful analysis completion AND on analysis cancellation.
            /// </summary>
            public virtual void ResetToReadyState()
            {
                Debug.Assert(StateKind == StateKind.InProcess);
                this.StateKind = StateKind.Ready;
            }
        }
    }
}
