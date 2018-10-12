// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeStyle;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedExpressionsOrAssignmentsCodeFixProvider : SyntaxEditorBasedCodeFixProvider
    {
        protected abstract string FixableDiagnosticId { get; }
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(FixableDiagnosticId);

        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics[0];
            var preference = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostic);
            string title;
            switch (preference)
            {
                case UnusedExpressionAssignmentPreference.DiscardVariable:
                    title = FeaturesResources.Use_discard_underscore;
                    break;

                case UnusedExpressionAssignmentPreference.UnusedLocalVariable:
                    title = FeaturesResources.Use_discarded_local;
                    break;

                default:
                    return Task.CompletedTask;
            }

            context.RegisterCodeFix(
                new MyCodeAction(
                    title,
                    c => FixAsync(context.Document, diagnostic, context.CancellationToken)),
                diagnostic);
            return Task.CompletedTask;
        }

        private sealed class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument) :
                base(title, createChangedDocument)
            {
            }
        }
    }
}
