// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CodeActions.CodeAction;

namespace Microsoft.CodeAnalysis.IntroduceUsingStatement
{
    internal abstract class AbstractIntroduceUsingStatementCodeRefactoringProvider<TStatementSyntax, TLocalDeclarationSyntax> : CodeRefactoringProvider
        where TStatementSyntax : SyntaxNode
        where TLocalDeclarationSyntax : TStatementSyntax
    {
        protected abstract string CodeActionTitle { get; }

        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var document = context.Document;
            var span = context.Span;

            var (declarationSyntax, _) =
                await FindDisposableLocalDeclaration(document, span, context.CancellationToken).ConfigureAwait(false);

            if (declarationSyntax != null)
            {
                context.RegisterRefactoring(new MyCodeAction(
                    CodeActionTitle,
                    cancellationToken => IntroduceUsingStatementAsync(document, span, cancellationToken)));
            }
        }

        private async Task<(TLocalDeclarationSyntax, ILocalSymbol)> FindDisposableLocalDeclaration(Document document, TextSpan selection, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            var declarationSyntax =
                root.FindNode(selection)?.GetAncestorOrThis<TLocalDeclarationSyntax>()
                ?? root.FindTokenOnLeftOfPosition(selection.End).GetAncestor<TLocalDeclarationSyntax>();

            if (declarationSyntax is null)
            {
                return default;
            }

            var introduceUsingStatementService = document.GetLanguageService<IIntroduceUsingStatementService>();
            if (!await introduceUsingStatementService.CanIntroduceUsingStatementAsync(document, declarationSyntax, cancellationToken).ConfigureAwait(false))
            {
                return default;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var operation = semanticModel.GetOperation(declarationSyntax, cancellationToken) as IVariableDeclarationGroupOperation;
            if (operation?.Declarations.Length != 1)
            {
                return default;
            }

            var localDeclaration = operation.Declarations[0];
            if (localDeclaration.Declarators.Length != 1)
            {
                return default;
            }

            var declarator = localDeclaration.Declarators[0];

            var localType = declarator.Symbol?.Type;
            if (localType is null)
            {
                return default;
            }

            var initializer = (localDeclaration.Initializer ?? declarator.Initializer)?.Value;

            // Initializer kind is invalid when incomplete declaration syntax ends in an equals token.
            if (initializer is null || initializer.Kind == OperationKind.Invalid)
            {
                return default;
            }

            // Infer the intent of the selection. Offer the refactoring only if the selection
            // appears to be aimed at the declaration statement but not at its initializer expression.
            var isValidSelection = await CodeRefactoringHelpers.RefactoringSelectionIsValidAsync(
                document,
                selection,
                node: declarationSyntax,
                holes: ImmutableArray.Create(initializer.Syntax),
                cancellationToken).ConfigureAwait(false);

            if (!isValidSelection)
            {
                return default;
            }

            return (declarationSyntax, declarator.Symbol);
        }

        private async Task<Document> IntroduceUsingStatementAsync(
            Document document,
            TextSpan span,
            CancellationToken cancellationToken)
        {
            var (declarationStatement, _) = await FindDisposableLocalDeclaration(document, span, cancellationToken).ConfigureAwait(false);

            var introduceUsingStatementService = document.GetLanguageService<IIntroduceUsingStatementService>();
            return await introduceUsingStatementService.IntroduceUsingStatementAsync(document, declarationStatement, cancellationToken).ConfigureAwait(false);
        }

        private sealed class MyCodeAction : DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument)
                : base(title, createChangedDocument)
            {
            }
        }
    }
}
