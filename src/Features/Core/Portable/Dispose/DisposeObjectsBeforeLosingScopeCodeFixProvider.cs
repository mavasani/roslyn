// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Analyzer.Utilities.PooledObjects;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.IntroduceUsingStatement;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.DisposeAnalysis
{
    [ExportCodeFixProvider(LanguageNames.CSharp, LanguageNames.VisualBasic, Name = PredefinedCodeFixProviderNames.WrapUsingStatement), Shared]
    internal sealed class DisposeObjectsBeforeLosingScopeCodeFixProvider
        : SyntaxEditorBasedCodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(IDEDiagnosticIds.DisposeObjectsBeforeLosingScopeDiagnosticId);

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            if ((await GetTopmostNodeToFixAsync(context.Span, context.Document, context.CancellationToken).ConfigureAwait(false)) == null)
            {
                return;
            }

            context.RegisterCodeFix(new MyCodeAction(
                c => FixAsync(context.Document, context.Diagnostics[0], c)),
                context.Diagnostics);
        }

        protected override bool IncludeDiagnosticDuringFixAll(FixAllState fixAllState, Diagnostic diagnostic, CancellationToken cancellationToken)
            => GetTopmostNodeToFixAsync(diagnostic.Location.SourceSpan, fixAllState.Document, cancellationToken).GetAwaiter().GetResult() != null;

        private static async Task<SyntaxNode> GetTopmostNodeToFixAsync(
            TextSpan diagnosticSpan,
            Document document,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var node = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);
            if (node == null)
            {
                return null;
            }

            var operation = semanticModel.GetOperation(node, cancellationToken)?.Parent;
            while (operation != null)
            {
                switch (operation)
                {
                    case IConversionOperation _:
                    case IParenthesizedOperation _:
                    case IVariableInitializerOperation _:
                    case IVariableDeclaratorOperation _:
                    case IVariableDeclarationOperation _:
                        operation = operation.Parent;
                        continue;

                    case IVariableDeclarationGroupOperation _:
                    case IAssignmentOperation _:
                        break;

                    default:
                        return null;
                }

                break;
            }

            if (operation == null)
            {
                return null;
            }

            var introduceUsingStatementService = document.GetLanguageService<IIntroduceUsingStatementService>();
            if (!await introduceUsingStatementService.CanIntroduceUsingStatementAsync(document, operation.Syntax, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return operation.Syntax;
        }

        protected override async Task FixAllAsync(
            Document document,
            ImmutableArray<Diagnostic> diagnostics,
            SyntaxEditor editor,
            CancellationToken cancellationToken)
        {
            var nodes = ArrayBuilder<SyntaxNode>.GetInstance(diagnostics.Length);

            foreach (var diagnostic in diagnostics)
            {
                var node = await GetTopmostNodeToFixAsync(diagnostic.Location.SourceSpan, document, cancellationToken).ConfigureAwait(false);
                if (node != null)
                {
                    nodes.Add(node);
                }
            }

            await editor.ApplyMethodBodySemanticEditsAsync(
                document,
                nodes.ToImmutableAndFree(),
                canReplace: (model, node) => true,
                updateRootAsync: (model, currentRoot, node) => IntroduceUsingStatementAsync(currentRoot, node),
                cancellationToken).ConfigureAwait(true);

            // Local functions.
            async Task<SyntaxNode> IntroduceUsingStatementAsync(SyntaxNode currentRoot, SyntaxNode node)
            {
                var introduceUsingStatementService = document.GetLanguageService<IIntroduceUsingStatementService>();
                currentRoot = currentRoot.TrackNodes(node);
                var currentDocument = document.WithSyntaxRoot(currentRoot);
                currentRoot = await currentDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                node = currentRoot.GetCurrentNode(node);
                Debug.Assert(await introduceUsingStatementService.CanIntroduceUsingStatementAsync(currentDocument, node, cancellationToken).ConfigureAwait(false));

                currentDocument = await introduceUsingStatementService.IntroduceUsingStatementAsync(currentDocument, node, cancellationToken).ConfigureAwait(false);
                return await currentDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(Func<CancellationToken, Task<Document>> createChangedDocument) :
                base(FeaturesResources.Wrap_inside_using_statement, createChangedDocument)
            {
            }
        }
    }
}
