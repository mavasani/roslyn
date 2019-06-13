// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.AvailableCodeActions
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, LanguageNames.VisualBasic, Name = nameof(ViewAvailableCodeActionsCodeRefactoringProvider)), Shared]
    internal sealed class ViewAvailableCodeActionsCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var availableCodeActionsService = context.Document.Project.Solution.Workspace.Services.GetService<IAvailableCodeActionsService>();
            if (availableCodeActionsService != null &&
                availableCodeActionsService.CanShowAvailableCodeActionsWindow &&
                !availableCodeActionsService.IsShowingAvailableCodeActionsWindow)
            {
                context.RegisterRefactoring(
                    new MyCodeAction(FeaturesResources.View_all_actions_in_this_document,
                        c => ApplyCodeActionAsync(context.Document, context.Span, availableCodeActionsService, context.CancellationToken)));
            }

            return Task.CompletedTask;
        }

        private static Task<Document> ApplyCodeActionAsync(Document document, TextSpan span, IAvailableCodeActionsService availableCodeActionsService, CancellationToken cancellationToken)
        {
            Task.Run(() => availableCodeActionsService.UpdateAvailableCodeActionsAsync(document, span, cancellationToken));
            return Task.FromResult(document);
        }

        private class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument) :
                base(title, createChangedDocument)
            {
            }

            internal override CodeActionPriority Priority => CodeActionPriority.None;

            protected override Task<IEnumerable<CodeActionOperation>> ComputePreviewOperationsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(SpecializedCollections.EmptyEnumerable<CodeActionOperation>());
            }
        }
    }
}
