// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal partial class AnalysisState
    {
        /// <summary>
        /// Stores the partial analysis state for a specific symbol declaration for a specific analyzer.
        /// </summary>
        internal sealed class DeclarationAnalyzerStateData : SyntaxNodeAnalyzerStateData
        {
            /// <summary>
            /// Partial analysis state for code block actions executed on the declaration.
            /// </summary>
            public CodeBlockAnalyzerStateData CodeBlockAnalysisState { get; set; }

            public DeclarationAnalyzerStateData()
                : this(StateKind.Ready, new HashSet<AnalyzerAction>(), null, new HashSet<SyntaxNode>(), new CodeBlockAnalyzerStateData())
            {
            }

            private DeclarationAnalyzerStateData(StateKind stateKind, HashSet<AnalyzerAction> processedActions, SyntaxNode currentNode, HashSet<SyntaxNode> processedNodes, CodeBlockAnalyzerStateData codeBlockAnalysisState)
                : base(stateKind, processedActions, currentNode, processedNodes)
            {
                CurrentNode = currentNode;
                ProcessedNodes = processedNodes;
                CodeBlockAnalysisState = codeBlockAnalysisState;
            }

            public override AnalyzerStateData WithStateKind(StateKind stateKind)
            {
                Debug.Assert(stateKind != this.StateKind);
                var newCodeBlockState = (CodeBlockAnalyzerStateData)CodeBlockAnalysisState.WithStateKind(stateKind);
                return new DeclarationAnalyzerStateData(stateKind, ProcessedActions, CurrentNode, ProcessedNodes, newCodeBlockState);
            }
            
            public override void ResetToReadyState()
            {
                base.ResetToReadyState();
                this.CodeBlockAnalysisState.ResetToReadyState();
            }
        }

        /// <summary>
        /// Stores the partial analysis state for syntax node actions executed on the declaration.
        /// </summary>
        internal class SyntaxNodeAnalyzerStateData : AnalyzerStateData
        {
            public HashSet<SyntaxNode> ProcessedNodes { get; set; }
            public SyntaxNode CurrentNode { get; set; }

            public SyntaxNodeAnalyzerStateData()
                : this(StateKind.Ready, new HashSet<AnalyzerAction>(), null, new HashSet<SyntaxNode>())
            {
            }

            protected SyntaxNodeAnalyzerStateData(StateKind stateKind, HashSet<AnalyzerAction> processedActions, SyntaxNode currentNode, HashSet<SyntaxNode> processedNodes)
                : base(stateKind, processedActions)
            {
                CurrentNode = currentNode;
                ProcessedNodes = processedNodes;
            }

            public override AnalyzerStateData WithStateKind(StateKind stateKind)
            {
                Debug.Assert(stateKind != this.StateKind);
                return new SyntaxNodeAnalyzerStateData(stateKind, ProcessedActions, CurrentNode, ProcessedNodes);
            }

            public void ClearNodeAnalysisState()
            {
                CurrentNode = null;
                ProcessedActions.Clear();
            }
        }

        /// <summary>
        /// Stores the partial analysis state for code block actions executed on the declaration.
        /// </summary>
        internal sealed class CodeBlockAnalyzerStateData : AnalyzerStateData
        {
            public SyntaxNodeAnalyzerStateData ExecutableNodesAnalysisState { get; private set; }

            public ImmutableHashSet<AnalyzerAction> CurrentCodeBlockEndActions { get; set; }
            public ImmutableHashSet<AnalyzerAction> CurrentCodeBlockNodeActions { get; set; }

            public CodeBlockAnalyzerStateData()
                : this(StateKind.Ready, new HashSet<AnalyzerAction>(), null, null, new SyntaxNodeAnalyzerStateData())
            {
            }

            private CodeBlockAnalyzerStateData(StateKind stateKind, HashSet<AnalyzerAction> processedActions, ImmutableHashSet<AnalyzerAction> currentCodeBlockEndActions, ImmutableHashSet<AnalyzerAction> currentCodeBlockNodeActions, SyntaxNodeAnalyzerStateData executableNodesAnalysisState)
                : base(stateKind, processedActions)
            {
                ExecutableNodesAnalysisState = executableNodesAnalysisState;
                CurrentCodeBlockEndActions = currentCodeBlockEndActions;
                CurrentCodeBlockNodeActions = currentCodeBlockNodeActions;
            }

            public override AnalyzerStateData WithStateKind(StateKind stateKind)
            {
                Debug.Assert(stateKind != this.StateKind);
                var newExecutableNodesState = (SyntaxNodeAnalyzerStateData)ExecutableNodesAnalysisState.WithStateKind(stateKind);
                return new CodeBlockAnalyzerStateData(stateKind, ProcessedActions, CurrentCodeBlockEndActions, CurrentCodeBlockNodeActions, newExecutableNodesState);
            }

            public override void ResetToReadyState()
            {
                base.ResetToReadyState();
                this.ExecutableNodesAnalysisState.ResetToReadyState();
            }
        }
    }
}
