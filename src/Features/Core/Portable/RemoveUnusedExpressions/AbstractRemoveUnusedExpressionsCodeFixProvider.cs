// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedExpressionsCodeFixProvider<TFieldDeclarationSyntax> : CodeFixProvider
        where TFieldDeclarationSyntax : SyntaxNode
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(IDEDiagnosticIds.ExpressionValueIsUnusedDiagnosticId, IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId);

        public override FixAllProvider GetFixAllProvider()
        {
            return base.GetFixAllProvider();
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics[0];
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var preference = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostic);
            switch (diagnostic.Id)
            {
                case IDEDiagnosticIds.ExpressionValueIsUnusedDiagnosticId:
                    context.RegisterCodeFix(new MyCodeAction(
                        c => FixAsync(context.Document, node, c)),
                        diagnostic);
                    break;
            }
            //context.RegisterCodeFix(new MyCodeAction(
            //    c => FixAsync(context.Document, context.Diagnostics[0], c)),
            //    context.Diagnostics);
            return Task.CompletedTask;
        }

        private async Task AddDiscardForUnusedExpression(Document document, SyntaxNode node, CancellationToken cancellationToken)
        {
            SyntaxEd
        }

        private class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument) :
                base(title, createChangedDocument)
            {
            }
        }
    }
}
