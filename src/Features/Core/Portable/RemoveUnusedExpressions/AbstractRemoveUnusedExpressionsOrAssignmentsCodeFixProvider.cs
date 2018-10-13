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

        protected abstract void RemoveDiscardDeclarations(
            SyntaxNode memberDeclaration,
            SyntaxEditor editor,
            CancellationToken cancellationToken);

        protected sealed override async Task FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, SyntaxEditor editor, CancellationToken cancellationToken)
        {
            var preference = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostics[0]);
            if (preference == UnusedExpressionAssignmentPreference.None)
            {
                return;
            }

            var diagnosticsByMethod = diagnostics.Where(d => preference == AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(d))
                                                 .GroupBy(d => AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetOwningMemberSymbolName(d));

            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();
            var memberAnnotation = new SyntaxAnnotation();

            var originalRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var root = originalRoot;
            foreach (var diagnosticsToFix in diagnosticsByMethod)
            {
                var memberDecl = syntaxFacts.GetContainingMemberDeclaration(root, diagnosticsToFix.First().Location.SourceSpan.Start);
                Contract.ThrowIfNull(memberDecl);
                root = root.ReplaceNode(memberDecl, memberDecl.WithAdditionalAnnotations(memberAnnotation));
            }

            editor.ReplaceNode(originalRoot, root);
            document = document.WithSyntaxRoot(root);
            root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            var usedNames = PooledHashSet<string>.GetInstance();
            try
            {
                foreach (var diagnosticsToFix in diagnosticsByMethod)
                {
                    var orderedDiagnostics = diagnosticsToFix.OrderBy(d => d.Location.SourceSpan.Start);
                    await FixAllAsync(orderedDiagnostics, semanticModel, root, preference, GenerateUniqueNameAtSpanStart, editor, cancellationToken).ConfigureAwait(false);
                    usedNames.Clear();
                }

                if (preference == UnusedExpressionAssignmentPreference.DiscardVariable)
                {
                    var newRoot = editor.GetChangedRoot();
                    var newEditor = new SyntaxEditor(newRoot, editor.Generator);
                    foreach (var memberDecl in newRoot.DescendantNodes().Where(n => n.HasAnnotation(memberAnnotation)))
                    {
                        RemoveDiscardDeclarations(memberDecl, newEditor, cancellationToken);
                    }

                    editor.ReplaceNode(root, newEditor.GetChangedRoot());
                }
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
