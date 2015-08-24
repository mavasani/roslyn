// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal abstract partial class AbstractSuppressionCodeFixProvider : ISuppressionFixProvider
    {
        internal abstract class AbstractSuppressionCodeAction : CodeAction
        {
            private readonly AbstractSuppressionCodeFixProvider _fixer;
            private readonly string _title;
            private readonly string _workflowState;

            protected AbstractSuppressionCodeAction(AbstractSuppressionCodeFixProvider fixer, string title, string workflowState)
            {
                _fixer = fixer;
                _title = title;
                _workflowState = workflowState;
            }

            public sealed override string Title => _title;
            protected AbstractSuppressionCodeFixProvider Fixer => _fixer;
            protected string WorkflowState => _workflowState;
            protected const string SeparatorForEquivalenceKey = ";";
            protected abstract string DiagnosticIdForEquivalenceKey { get; }

            public sealed override string EquivalenceKey => Title + DiagnosticIdForEquivalenceKey +
                (_workflowState != null ? (SeparatorForEquivalenceKey + WorkflowState) : string.Empty);

            public static string GetWorkflowState(string equivalenceKey)
            {
                var separatorIndex = equivalenceKey.LastIndexOf(SeparatorForEquivalenceKey);
                var workflowStateIndex = separatorIndex + SeparatorForEquivalenceKey.Length;
                if (separatorIndex < 0 || workflowStateIndex >= equivalenceKey.Length)
                {
                    return null;
                }

                return equivalenceKey.Substring(workflowStateIndex);
            }
        }
    }
}
