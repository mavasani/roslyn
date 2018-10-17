﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.MoveDeclarationNearReference;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Simplification;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedExpressionsCodeFixProvider<TExpressionSyntax, TStatementSyntax, TBlockSyntax,
                                                                           TExpressionStatementSyntax, TLocalDeclarationStatementSyntax,
                                                                           TVariableDeclaratorSyntax, TForEachStatementSyntax>
        : SyntaxEditorBasedCodeFixProvider
        where TExpressionSyntax : SyntaxNode
        where TStatementSyntax : SyntaxNode
        where TBlockSyntax : TStatementSyntax
        where TExpressionStatementSyntax : TStatementSyntax
        where TLocalDeclarationStatementSyntax : TStatementSyntax
        where TVariableDeclaratorSyntax : SyntaxNode
        where TForEachStatementSyntax: TStatementSyntax
    {
        private static readonly SyntaxAnnotation s_memberAnnotation = new SyntaxAnnotation();
        private static readonly SyntaxAnnotation s_newLocalDeclarationStatementAnnotation = new SyntaxAnnotation();
        private static readonly SyntaxAnnotation s_unusedLocalDeclarationAnnotation = new SyntaxAnnotation();

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(IDEDiagnosticIds.ExpressionValueIsUnusedDiagnosticId,
                                                                                                    IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId);

        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics[0];
            var preference = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostic);
            var isRemovableAssignment = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetIsRemovableAssignmentDiagnostic(diagnostic);

            string title;
            if (isRemovableAssignment)
            {
                // Recommend removing the redundant constant value assignment.
                title = FeaturesResources.Remove_redundant_assignment;
            }
            else
            {
                // Recommend using discard/unused local for redundant non-constant assignment.
                switch (preference)
                {
                    case UnusedExpressionAssignmentPreference.DiscardVariable:
                        if (IsForEachIterationVariableDiagnostic(diagnostic, context.Document))
                        {
                            // Do not offer a fix to replace unused foreach iteration variable with discard.
                            // User should probably replace it with a for loop based on the collection length.
                            return Task.CompletedTask;
                        }

                        title = FeaturesResources.Use_discard_underscore;
                        break;

                    case UnusedExpressionAssignmentPreference.UnusedLocalVariable:
                        title = FeaturesResources.Use_discarded_local;
                        break;

                    default:
                        return Task.CompletedTask;
                }
            }

            context.RegisterCodeFix(
                new MyCodeAction(
                    title,
                    c => FixAsync(context.Document, diagnostic, context.CancellationToken),
                    equivalenceKey: GetEquivalenceKey(preference, isRemovableAssignment)),
                diagnostic);

            return Task.CompletedTask;

            // Local Functions.
        }

        private static bool IsForEachIterationVariableDiagnostic(Diagnostic diagnostic, Document document)
        {
            // Do not offer a fix to replace unused foreach iteration variable with discard.
            // User should probably replace it with a for loop based on the collection length.
            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();
            var root = diagnostic.Location.SourceTree.GetRoot();
            return syntaxFacts.IsForEachStatement(root.FindNode(diagnostic.Location.SourceSpan));
        }

        private static string GetEquivalenceKey(UnusedExpressionAssignmentPreference preference, bool isRemovableAssignment)
            => preference.ToString() + isRemovableAssignment.ToString();

        private static string GetEquivalenceKey(Diagnostic diagnostic)
        {
            var preference = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostic);
            var isRemovableAssignment = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetIsRemovableAssignmentDiagnostic(diagnostic);
            return GetEquivalenceKey(preference, isRemovableAssignment);
        }

        protected abstract SyntaxNode UpdateNameForFlaggedNode(SyntaxNode node, SyntaxToken newName);
        protected abstract SyntaxToken GetForEachStatementIdentifier(TForEachStatementSyntax node);
        protected abstract TBlockSyntax GenerateBlock(IEnumerable<TStatementSyntax> statements);
        protected abstract ILocalSymbol GetSingleDeclaredLocal(TLocalDeclarationStatementSyntax localDeclaration, SemanticModel semanticModel, CancellationToken cancellationToken);
        private bool NeedsToMoveNewLocalDeclarationsNearReference(string diagnosticId) => diagnosticId == IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId;

        protected override bool IncludeDiagnosticDuringFixAll(FixAllState fixAllState, Diagnostic diagnostic)
        {
            return fixAllState.CodeActionEquivalenceKey == GetEquivalenceKey(diagnostic) &&
                !IsForEachIterationVariableDiagnostic(diagnostic, fixAllState.Document);
        }

        private IEnumerable<IGrouping<SyntaxNode, Diagnostic>> GetDiagnosticsGroupedByMember(
            ImmutableArray<Diagnostic> diagnostics,
            ISyntaxFactsService syntaxFacts,
            SyntaxNode root,
            out string diagnosticId,
            out UnusedExpressionAssignmentPreference preference,
            out bool removeAssignments)
        {
            diagnosticId = diagnostics[0].Id;
            preference = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostics[0]);
            removeAssignments = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetIsRemovableAssignmentDiagnostic(diagnostics[0]);
#if DEBUG
            foreach (var diagnostic in diagnostics)
            {
                Debug.Assert(diagnosticId == diagnostic.Id);
                Debug.Assert(preference == AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostic));
                Debug.Assert(removeAssignments == AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetIsRemovableAssignmentDiagnostic(diagnostic));
            }
#endif

            return GetDiagnosticsGroupedByMember(diagnostics, syntaxFacts, root);
        }

        private IEnumerable<IGrouping<SyntaxNode, Diagnostic>> GetDiagnosticsGroupedByMember(
            ImmutableArray<Diagnostic> diagnostics,
            ISyntaxFactsService syntaxFacts,
            SyntaxNode root)
            => diagnostics.GroupBy(d => syntaxFacts.GetContainingMemberDeclaration(root, d.Location.SourceSpan.Start));

        protected override async Task<Document> FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            // Track all the member declaration nodes that have diagnostics.
            // We will post process all these tracked nodes after applying the fix (see "PostProcessDocumentAsync" below in this source file).

            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var memberDeclarations = GetDiagnosticsGroupedByMember(diagnostics, syntaxFacts, root).Select(g => g.Key);
            root = root.ReplaceNodes(memberDeclarations, computeReplacementNode: (_, n) => n.WithAdditionalAnnotations(s_memberAnnotation));
            document = document.WithSyntaxRoot(root);
            return await base.FixAllAsync(document, diagnostics, cancellationToken).ConfigureAwait(false);
        }

        protected sealed override async Task FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, SyntaxEditor editor, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();
            var diagnosticsGroupedByMember = GetDiagnosticsGroupedByMember(diagnostics, syntaxFacts, root,
                out var diagnosticId, out var preference, out var removeAssignments);
            if (preference == UnusedExpressionAssignmentPreference.None)
            {
                return;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var usedNames = PooledHashSet<string>.GetInstance();
            try
            {
                foreach (var diagnosticsToFix in diagnosticsGroupedByMember)
                {
                    var orderedDiagnostics = diagnosticsToFix.OrderBy(d => d.Location.SourceSpan.Start);
                    FixAll(diagnosticId, orderedDiagnostics, semanticModel, root, preference,
                        removeAssignments, GenerateUniqueNameAtSpanStart, editor, syntaxFacts, cancellationToken);
                    usedNames.Clear();
                }

                var currentRoot = editor.GetChangedRoot();
                var newRoot = await PostProcessDocumentAsync(document, currentRoot,
                    editor.Generator, diagnosticId, preference, cancellationToken).ConfigureAwait(false);
                if (currentRoot != newRoot)
                {
                    editor.ReplaceNode(root, newRoot);
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

        private void FixAll(
            string diagnosticId,
            IOrderedEnumerable<Diagnostic> diagnostics,
            SemanticModel semanticModel,
            SyntaxNode root,
            UnusedExpressionAssignmentPreference preference,
            bool removeAssignments,
            Func<SyntaxNode, string> generateUniqueNameAtSpanStart,
            SyntaxEditor editor,
            ISyntaxFactsService syntaxFacts,
            CancellationToken cancellationToken)
        {
            switch (diagnosticId)
            {
                case IDEDiagnosticIds.ExpressionValueIsUnusedDiagnosticId:
                    FixAllExpressionValueIsUnusedDiagnostics(diagnostics, semanticModel, root,
                        preference, generateUniqueNameAtSpanStart, editor, syntaxFacts, cancellationToken);
                    break;

                case IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId:
                    FixAllValueAssignedIsUnusedDiagnostics(diagnostics, semanticModel, root,
                        preference, removeAssignments, generateUniqueNameAtSpanStart, editor, syntaxFacts, cancellationToken);
                    break;

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        private void FixAllExpressionValueIsUnusedDiagnostics(
            IOrderedEnumerable<Diagnostic> diagnostics,
            SemanticModel semanticModel,
            SyntaxNode root,
            UnusedExpressionAssignmentPreference preference,
            Func<SyntaxNode, string> generateUniqueNameAtSpanStart,
            SyntaxEditor editor,
            ISyntaxFactsService syntaxFacts,
            CancellationToken cancellationToken)
        {
            foreach (var diagnostic in diagnostics)
            {
                var expressionStatement = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TExpressionStatementSyntax>();
                if (expressionStatement == null)
                {
                    continue;
                }

                var expression = syntaxFacts.GetExpressionOfExpressionStatement(expressionStatement);
                switch (preference)
                {
                    case UnusedExpressionAssignmentPreference.DiscardVariable:
                        Debug.Assert(semanticModel.Language != LanguageNames.VisualBasic);
                        var discardAssignmentExpression = (TExpressionSyntax)editor.Generator.AssignmentStatement(
                                left: editor.Generator.IdentifierName("_"), right: expression.WithoutTrivia())
                            .WithTriviaFrom(expression)
                            .WithAdditionalAnnotations(Simplifier.Annotation);
                        editor.ReplaceNode(expression, discardAssignmentExpression);
                        break;

                    case UnusedExpressionAssignmentPreference.UnusedLocalVariable:
                        // Add Simplifier annotation so that 'var'/explicit type is correctly added based on user options.
                        var localDecl = editor.Generator.LocalDeclarationStatement(
                                name: generateUniqueNameAtSpanStart(expressionStatement), initializer: expression)
                            .WithTriviaFrom(expressionStatement)
                            .WithAdditionalAnnotations(Simplifier.Annotation);
                        editor.ReplaceNode(expressionStatement, localDecl);
                        break;
                }
            }
        }

        private void FixAllValueAssignedIsUnusedDiagnostics(
            IOrderedEnumerable<Diagnostic> diagnostics,
            SemanticModel semanticModel,
            SyntaxNode root,
            UnusedExpressionAssignmentPreference preference,
            bool removeAssignments,
            Func<SyntaxNode, string> generateUniqueNameAtSpanStart,
            SyntaxEditor editor,
            ISyntaxFactsService syntaxFacts,
            CancellationToken cancellationToken)
        {
            var declarationStatementsBuilder = ArrayBuilder<TLocalDeclarationStatementSyntax>.GetInstance();
            var nodeReplacementMap = PooledDictionary<SyntaxNode, SyntaxNode>.GetInstance();
            var nodesToRemove = PooledHashSet<SyntaxNode>.GetInstance();
            var declStatementCandidatesForRemoval = PooledHashSet<TLocalDeclarationStatementSyntax>.GetInstance();

            try
            {
                var nodesToFix = GetNodesToFix();

                // Note this fixer only operates on code blocks which have no syntax errors (see "HasSyntaxErrors" usage in AbstractRemoveUnusedExpressionsDiagnosticAnalyzer).
                // Hence, we can assume that each node to fix is parented by a StatementSyntax node.
                foreach (var nodesByStatement in nodesToFix.GroupBy(n => n.node.FirstAncestorOrSelf<TStatementSyntax>()))
                {
                    declarationStatementsBuilder.Clear();
                    
                    var statement = nodesByStatement.Key;
                    foreach (var (node, isUnusedLocalAssignment) in nodesByStatement)
                    {
                        var declaredLocal = semanticModel.GetDeclaredSymbol(node, cancellationToken) as ILocalSymbol;

                        string newLocalNameOpt = null;
                        if (removeAssignments)
                        {
                            // Removable constant assignment or initialization.
                            if (declaredLocal != null)
                            {
                                // Constant value initialization.
                                // For example, "int a = 0;"
                                var variableDeclarator = node.FirstAncestorOrSelf<TVariableDeclaratorSyntax>();
                                Debug.Assert(variableDeclarator != null);
                                nodesToRemove.Add(variableDeclarator);

                                // Local declaration statement containing the declarator might be a candidate for removal if all its variables get marked for removal.
                                declStatementCandidatesForRemoval.Add(variableDeclarator.GetAncestor<TLocalDeclarationStatementSyntax>());
                            }
                            else
                            {
                                // Constant value assignment.
                                Debug.Assert(syntaxFacts.IsLeftSideOfAssignment(node));

                                if (node.Parent is TStatementSyntax)
                                {
                                    // For example, VB simple assignment statement "a = 0"
                                    nodesToRemove.Add(node.Parent);
                                }
                                else if (node.Parent is TExpressionSyntax && node.Parent.Parent is TExpressionStatementSyntax)
                                {
                                    // For example, C# simple assignment statement "a = 0;"
                                    nodesToRemove.Add(node.Parent.Parent);
                                }
                                else
                                {
                                    nodeReplacementMap.Add(node.Parent, syntaxFacts.GetRightHandSideOfAssignment(node.Parent));
                                    //Debug.Fail("Unhandled removable constant assignment case");
                                    //continue;
                                }
                            }
                        }
                        else
                        {
                            // Non-constant value initialization/assignment.
                            newLocalNameOpt = preference == UnusedExpressionAssignmentPreference.DiscardVariable ? "_" : generateUniqueNameAtSpanStart(node);
                            var newNameToken = editor.Generator.Identifier(newLocalNameOpt);
                            nodeReplacementMap.Add(node, UpdateNameForFlaggedNode(node, newNameToken));
                        }

                        if (declaredLocal != null)
                        {
                            // We have a dead initialization for a local declaration.
                            var localDeclaration = CreateLocalDeclarationStatement(declaredLocal.Type, declaredLocal.Name);
                            if (isUnusedLocalAssignment)
                            {
                                localDeclaration = localDeclaration.WithAdditionalAnnotations(s_unusedLocalDeclarationAnnotation);
                            }

                            declarationStatementsBuilder.Add(localDeclaration);
                        }
                        else
                        {
                            // We have a dead assignment to a local/parameter.
                            // If the assignment value is a non-constant expression, and user prefers unused local variables for unused value assignment,
                            // create a new local declaration for the unused local.
                            if (preference == UnusedExpressionAssignmentPreference.UnusedLocalVariable && !removeAssignments)
                            {
                                var type = semanticModel.GetTypeInfo(node, cancellationToken).Type;
                                Debug.Assert(type != null);
                                Debug.Assert(newLocalNameOpt != null);
                                declarationStatementsBuilder.Add(CreateLocalDeclarationStatement(type, newLocalNameOpt));
                            }
                        }
                    }

                    if (declarationStatementsBuilder.Count > 0)
                    {
                        editor.InsertBefore(statement.FirstAncestorOrSelf<TStatementSyntax>(n => n.Parent is TBlockSyntax), declarationStatementsBuilder);
                    }
                }

                foreach (var localDeclarationStatement in declStatementCandidatesForRemoval)
                {
                    if (ShouldRemoveStatement(localDeclarationStatement, out var variables))
                    {
                        nodesToRemove.Add(localDeclarationStatement);
                        nodesToRemove.RemoveRange(variables);
                    }
                }

                foreach (var node in nodesToRemove)
                {
                    editor.RemoveNode(node);
                }

                foreach (var kvp in nodeReplacementMap)
                {
                    editor.ReplaceNode(kvp.Key, kvp.Value.WithAdditionalAnnotations(Formatter.Annotation));
                }
            }
            finally
            {
                declarationStatementsBuilder.Free();
                nodeReplacementMap.Free();
                nodesToRemove.Free();
            }

            return;

            // Local functions.
            IEnumerable<(SyntaxNode node, bool isUnusedLocalAssignment)> GetNodesToFix()
            {
                foreach (var diagnostic in diagnostics)
                {
                    var node = root.FindNode(diagnostic.Location.SourceSpan);
                    var isUnusedLocalAssignment = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetIsUnusedLocalDiagnostic(diagnostic);
                    yield return (node, isUnusedLocalAssignment);
                }
            }

            // Mark generated local declaration statement with:
            //  1. "s_newLocalDeclarationAnnotation" for post processing in "MoveNewLocalDeclarationsNearReference" below.
            //  2. Simplifier annotation so that 'var'/explicit type is correctly added based on user options.
            TLocalDeclarationStatementSyntax CreateLocalDeclarationStatement(ITypeSymbol type, string name)
                => (TLocalDeclarationStatementSyntax)editor.Generator.LocalDeclarationStatement(type, name)
                   .WithLeadingTrivia(editor.Generator.EndOfLine(Environment.NewLine))
                   .WithAdditionalAnnotations(s_newLocalDeclarationStatementAnnotation, Simplifier.Annotation);

            bool ShouldRemoveStatement(TLocalDeclarationStatementSyntax localDeclarationStatement, out SeparatedSyntaxList<SyntaxNode> variables)
            {
                Debug.Assert(removeAssignments);

                // We should remove the entire local declaration statement if all its variables are marked for removal.
                variables = syntaxFacts.GetVariablesOfLocalDeclarationStatement(localDeclarationStatement);
                foreach (var variable in variables)
                {
                    if (!nodesToRemove.Contains(variable))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private async Task<SyntaxNode> PostProcessDocumentAsync(
            Document document,
            SyntaxNode currentRoot,
            SyntaxGenerator generator,
            string diagnosticId,
            UnusedExpressionAssignmentPreference preference,
            CancellationToken cancellationToken)
        {
            // If we added discard assignments, replace all discard variable declarations in
            // this method with discard assignments, i.e. "var _ = M();" is replaced with "_ = M();"
            // This is done to prevent compiler errors where the existing method has a discard
            // variable declaration at a line following the one we added a discard assignment in our fix.
            if (preference == UnusedExpressionAssignmentPreference.DiscardVariable)
            {
                currentRoot = await PostProcessDocumentCoreAsync(
                    RemoveDiscardDeclarationsAsync, currentRoot, document, generator, cancellationToken).ConfigureAwait(false);
            }

            // If we added new variable declaration statements, move these as close as possible to their
            // first reference site.
            if (NeedsToMoveNewLocalDeclarationsNearReference(diagnosticId))
            {
                currentRoot = await PostProcessDocumentCoreAsync(
                    MoveNewLocalDeclarationsNearReferenceAsync, currentRoot, document, generator, cancellationToken).ConfigureAwait(false);
            }

            return currentRoot;
        }

        private static async Task<SyntaxNode> PostProcessDocumentCoreAsync(
            Func<SyntaxNode, SyntaxEditor, Document, CancellationToken, Task> processMemberDeclarationAsync,
            SyntaxNode currentRoot,
            Document document,
            SyntaxGenerator generator,
            CancellationToken cancellationToken)
        {
            var newDocument = document.WithSyntaxRoot(currentRoot);
            var newRoot = await newDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var newEditor = new SyntaxEditor(newRoot, generator);
            foreach (var memberDecl in newRoot.DescendantNodes().Where(n => n.HasAnnotation(s_memberAnnotation)))
            {
                await processMemberDeclarationAsync(memberDecl, newEditor, newDocument, cancellationToken).ConfigureAwait(false);
            }

            return newEditor.GetChangedRoot();
        }

        protected abstract Task RemoveDiscardDeclarationsAsync(
            SyntaxNode memberDeclaration,
            SyntaxEditor editor,
            Document document,
            CancellationToken cancellationToken);

        private async Task MoveNewLocalDeclarationsNearReferenceAsync(
            SyntaxNode memberDeclaration,
            SyntaxEditor editor,
            Document document,
            CancellationToken cancellationToken)
        {
            var originalEditor = editor;
            var originalMemberDeclaration = memberDeclaration;

            var service = document.GetLanguageService<IMoveDeclarationNearReferenceService>();
            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();
            var originalDeclStatementsToMove = memberDeclaration.DescendantNodes()
                                                                .Where(n => n.HasAnnotation(s_newLocalDeclarationStatementAnnotation))
                                                                .ToImmutableArray();
            if (originalDeclStatementsToMove.IsEmpty)
            {
                return;
            }

            // Moving declarations closer to a reference can lead to conflicting edits.
            // So, we track all the declaration statements to be moved upfront, and update
            // the root, document, editor and memberDeclaration for every edit.
            // Finally, we apply replace the memberDeclaration in the originalEditor as a single edit.
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var rootWithTrackedNodes = root.TrackNodes(originalDeclStatementsToMove);
            await OnUpdatedRootAsync(rootWithTrackedNodes).ConfigureAwait(false);

            foreach (TLocalDeclarationStatementSyntax originalDeclStatement in originalDeclStatementsToMove)
            {
                // Get the current declaration statement.
                var declStatement = memberDeclaration.GetCurrentNode(originalDeclStatement);

                // Check if the new variable declaration is unused after all the fixes, and hence can be removed.
                if (!await TryRemoveUnusedLocalAsync(declStatement).ConfigureAwait(false))
                {
                    // Otherwise, move the declaration closer to the first reference.
                    await service.MoveDeclarationNearReferenceAsync(declStatement, document, editor, cancellationToken).ConfigureAwait(false);
                }

                await OnUpdatedRootAsync(editor.GetChangedRoot()).ConfigureAwait(false);
            }

            originalEditor.ReplaceNode(originalMemberDeclaration, memberDeclaration);
            return;

            // Local functions.
            async Task OnUpdatedRootAsync(SyntaxNode updatedRoot)
            {
                if (updatedRoot != root)
                {
                    document = document.WithSyntaxRoot(updatedRoot);
                    root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                    editor = new SyntaxEditor(root, editor.Generator);
                    memberDeclaration = syntaxFacts.GetContainingMemberDeclaration(root, memberDeclaration.SpanStart);
                }
            }

            async Task<bool> TryRemoveUnusedLocalAsync(TLocalDeclarationStatementSyntax newDecl)
            {
                if (newDecl.HasAnnotation(s_unusedLocalDeclarationAnnotation))
                {
                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                    var local = GetSingleDeclaredLocal(newDecl, semanticModel, cancellationToken);
                    Debug.Assert(local != null);

                    var referencedSymbols = await SymbolFinder.FindReferencesAsync(local, document.Project.Solution, cancellationToken).ConfigureAwait(false);
                    if (referencedSymbols.Count() == 1 &&
                        referencedSymbols.Single().Locations.IsEmpty())
                    {
                        editor.RemoveNode(newDecl);
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument, string equivalenceKey) :
                base(title, createChangedDocument, equivalenceKey)
            {
            }
        }
    }
}
