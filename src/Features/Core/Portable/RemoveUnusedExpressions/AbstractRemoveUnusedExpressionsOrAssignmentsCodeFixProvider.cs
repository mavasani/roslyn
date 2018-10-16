// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedExpressionsOrAssignmentsCodeFixProvider : SyntaxEditorBasedCodeFixProvider
    {
        private static readonly SyntaxAnnotation s_memberAnnotation = new SyntaxAnnotation();

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

        protected abstract Task FixAllAsync(
            IOrderedEnumerable<Diagnostic> diagnostics,
            SemanticModel semanticModel,
            SyntaxNode root,
            UnusedExpressionAssignmentPreference preference,
            Func<SyntaxNode, string> generateUniqueNameAtSpanStart,
            SyntaxEditor editor,
            CancellationToken cancellationToken);

        protected virtual Task RemoveDiscardDeclarationsAsync(
            SyntaxNode memberDeclaration,
            SyntaxEditor editor,
            Document document,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        protected abstract bool NeedsToMoveNewLocalDeclarationsNearReference { get; }
        protected virtual Task MoveNewLocalDeclarationsNearReferenceAsync(
            SyntaxNode memberDeclaration,
            SyntaxEditor editor,
            Document document,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        private (IEnumerable<IGrouping<string, Diagnostic>> diagnosticsGroupedByMember, UnusedExpressionAssignmentPreference preference) GetDiagnosticsGroupedByMember(
            ImmutableArray<Diagnostic> diagnostics)
        {
            var preference = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostics[0]);
            var diagnosticsGroupedByMember = diagnostics.Where(d => preference == AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(d))
                                                                    .GroupBy(d => AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetOwningMemberSymbolName(d));
            return (diagnosticsGroupedByMember, preference);
        }

        protected override async Task<Document> FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            // Annotate all the member declaration nodes that have diagnostics with "s_memberAnnotation".
            // We will post process all these annotated nodes after applying the fix (see "PostProcessDocumentAsync" below in this source file).

            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var (diagnosticsGroupedByMember, _) = GetDiagnosticsGroupedByMember(diagnostics);
            foreach (var diagnosticsToFix in diagnosticsGroupedByMember)
            {
                var memberDecl = syntaxFacts.GetContainingMemberDeclaration(root, diagnosticsToFix.First().Location.SourceSpan.Start);
                Contract.ThrowIfNull(memberDecl);
                root = root.ReplaceNode(memberDecl, memberDecl.WithAdditionalAnnotations(s_memberAnnotation));
            }

            return await base.FixAllAsync(document.WithSyntaxRoot(root), diagnostics, cancellationToken).ConfigureAwait(false);
        }

        protected sealed override async Task FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, SyntaxEditor editor, CancellationToken cancellationToken)
        {
            var (diagnosticsGroupedByMember, preference) = GetDiagnosticsGroupedByMember(diagnostics);
            if (preference == UnusedExpressionAssignmentPreference.None)
            {
                return;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var usedNames = PooledHashSet<string>.GetInstance();
            try
            {
                foreach (var diagnosticsToFix in diagnosticsGroupedByMember)
                {
                    var orderedDiagnostics = diagnosticsToFix.OrderBy(d => d.Location.SourceSpan.Start);
                    await FixAllAsync(orderedDiagnostics, semanticModel, root, preference, GenerateUniqueNameAtSpanStart, editor, cancellationToken).ConfigureAwait(false);
                    usedNames.Clear();
                }

                await PostProcessDocumentAsync().ConfigureAwait(false);
            }
            finally
            {
                usedNames.Free();
            }

            return;

            // Local functions
            string GenerateUniqueNameAtSpanStart(SyntaxNode node)
            {
                var name = NameGenerator.GenerateUniqueName("unused",
                    n => !usedNames.Contains(n) && semanticModel.LookupSymbols(node.SpanStart, name: n).IsEmpty);
                usedNames.Add(name);
                return name;
            }

            async Task PostProcessDocumentAsync()
            {
                var originalRoot = editor.GetChangedRoot();
                var currentRoot = originalRoot;

                try
                {
                    // If we added discard assignments, replace all discard variable declarations in
                    // this method with discard assignments, i.e. "var _ = M();" is replaced with "_ = M();"
                    // This is done to prevent compiler errors where the existing method has a discard
                    // variable declaration at a line following the one we added a discard assignment in our fix.
                    if (preference == UnusedExpressionAssignmentPreference.DiscardVariable)
                    {
                        currentRoot = await PostProcessDocumentCoreAsync(currentRoot, RemoveDiscardDeclarationsAsync).ConfigureAwait(false);
                    }

                    // If we added new variable declaration statements, move these as close as possible to their
                    // first reference site.
                    if (NeedsToMoveNewLocalDeclarationsNearReference)
                    {
                        currentRoot = await PostProcessDocumentCoreAsync(currentRoot, MoveNewLocalDeclarationsNearReferenceAsync).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (currentRoot != originalRoot)
                    {
                        editor.ReplaceNode(root, currentRoot);
                    }
                }
            }

            async Task<SyntaxNode> PostProcessDocumentCoreAsync(SyntaxNode currentRoot, Func<SyntaxNode, SyntaxEditor, Document, CancellationToken, Task> processMemberDeclarationAsync)
            {
                var newDocument = document.WithSyntaxRoot(currentRoot);
                var newRoot = await newDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var newEditor = new SyntaxEditor(newRoot, editor.Generator);
                foreach (var memberDecl in newRoot.DescendantNodes().Where(n => n.HasAnnotation(s_memberAnnotation)))
                {
                    await processMemberDeclarationAsync(memberDecl, newEditor, newDocument, cancellationToken).ConfigureAwait(false);
                }

                return newEditor.GetChangedRoot();
            }
        }

        private sealed class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument) :
                base(title, createChangedDocument, equivalenceKey: title)
            {
            }
        }
    }
}
