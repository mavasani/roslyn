// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MoveDeclarationNearReference;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Simplification;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedAssignmentsCodeFixProvider<TStatementSyntax, TBlockSyntax, TLocalDeclarationStatementSyntax>
        : AbstractRemoveUnusedExpressionsOrAssignmentsCodeFixProvider
        where TStatementSyntax : SyntaxNode
        where TBlockSyntax : TStatementSyntax
        where TLocalDeclarationStatementSyntax: TStatementSyntax
    {
        private static readonly SyntaxAnnotation s_newLocalDeclarationStatementAnnotation = new SyntaxAnnotation();
        private static readonly SyntaxAnnotation s_unusedLocalDeclarationAnnotation = new SyntaxAnnotation();

        protected sealed override string FixableDiagnosticId => IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId;

        protected abstract SyntaxNode UpdateNameForFlaggedNode(SyntaxNode node, SyntaxToken newName);
        protected abstract TBlockSyntax GenerateBlock(IEnumerable<TStatementSyntax> statements);
        protected abstract ILocalSymbol GetSingleDeclaredLocal(TLocalDeclarationStatementSyntax localDeclaration, SemanticModel semanticModel, CancellationToken cancellationToken);

        protected sealed override Task FixAllAsync(
            IOrderedEnumerable<Diagnostic> diagnostics,
            SemanticModel semanticModel,
            SyntaxNode root,
            UnusedExpressionAssignmentPreference preference,
            Func<SyntaxNode, string> generateUniqueNameAtSpanStart,
            SyntaxEditor editor,
            CancellationToken cancellationToken)
        {
            var declarationStatementsBuilder = ArrayBuilder<TLocalDeclarationStatementSyntax>.GetInstance();
            var nameReplacementsMap = PooledDictionary<SyntaxNode, SyntaxNode>.GetInstance();

            try
            {
                var nodesToFix = GetNodesToFix();

                // Note this fixer only operates on code blocks which have no syntax errors (see "HasSyntaxErrors" usage in AbstractRemoveUnusedExpressionsDiagnosticAnalyzer).
                // Hence, we can assume that each node to fix is parented by a StatementSyntax node.
                foreach (var nodesByStatement in nodesToFix.GroupBy(n => n.node.FirstAncestorOrSelf<TStatementSyntax>()))
                {
                    var statement = nodesByStatement.Key;
                    declarationStatementsBuilder.Clear();
                    nameReplacementsMap.Clear();

                    foreach (var (node, isUnusedLocal, isConstantValueAssigned) in nodesByStatement)
                    {
                        var newName = preference == UnusedExpressionAssignmentPreference.DiscardVariable ? "_" : generateUniqueNameAtSpanStart(node);
                        var newNameToken = editor.Generator.Identifier(newName);
                        nameReplacementsMap.Add(node, UpdateNameForFlaggedNode(node, newNameToken));

                        var declaredLocal = semanticModel.GetDeclaredSymbol(node, cancellationToken) as ILocalSymbol;
                        if (declaredLocal != null)
                        {
                            // We have a dead initialization for a local declaration.
                            var localDeclaration = CreateLocalDeclarationStatement(declaredLocal.Type, declaredLocal.Name);
                            if (isUnusedSymbolReference)
                            {
                                localDeclaration = localDeclaration.WithAdditionalAnnotations(s_unusedLocalDeclarationAnnotation);
                            }

                            declarationStatementsBuilder.Add(localDeclaration);
                        }
                        else
                        {
                            // We have a dead assignment to a local/parameter.
                            if (preference == UnusedExpressionAssignmentPreference.UnusedLocalVariable)
                            {
                                var type = semanticModel.GetTypeInfo(node, cancellationToken).Type;
                                Contract.ThrowIfNull(type);
                                declarationStatementsBuilder.Add(CreateLocalDeclarationStatement(type, newName));
                            }
                        }
                    }

                    var newStatement = statement.ReplaceNodes(nameReplacementsMap.Keys, computeReplacementNode: (n, _) => nameReplacementsMap[n]);
                    if (declarationStatementsBuilder.Count > 0)
                    {
                        // If parent is a block, just insert new declarations before the current statement.
                        if (statement.Parent is TBlockSyntax)
                        {
                            editor.InsertBefore(statement, declarationStatementsBuilder);
                        }
                        else
                        {
                            // Otherwise, wrap the declaration statements and the current statement with a new block.
                            newStatement = GenerateBlock(declarationStatementsBuilder.Concat(newStatement));
                        }
                    }

                    editor.ReplaceNode(statement, newStatement.WithAdditionalAnnotations(Formatter.Annotation));
                }
            }
            finally
            {
                declarationStatementsBuilder.Free();
                nameReplacementsMap.Free();
            }

            return Task.CompletedTask;

            // Local functions.
            IEnumerable<(SyntaxNode node, bool isUnusedLocal, bool isConstantValueAssigned)> GetNodesToFix()
            {
                foreach (var diagnostic in diagnostics)
                {
                    var node = root.FindNode(diagnostic.Location.SourceSpan);
                    var (isUnusedLocal, isConstantValueAssigned) = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetAdditionalPropertiesForDiagnostic(diagnostic);
                    yield return (node, isUnusedLocal, isConstantValueAssigned);
                }
            }

            // Mark generated local declaration statement with:
            //  1. "s_newLocalDeclarationAnnotation" for post processing in "MoveNewLocalDeclarationsNearReference" below.
            //  2. Simplifier annotation so that 'var'/explicit type is correctly added based on user options.
            TLocalDeclarationStatementSyntax CreateLocalDeclarationStatement(ITypeSymbol type, string name)
                => (TLocalDeclarationStatementSyntax)editor.Generator.LocalDeclarationStatement(type, name)
                   .WithLeadingTrivia(editor.Generator.EndOfLine(Environment.NewLine))
                   .WithAdditionalAnnotations(s_newLocalDeclarationStatementAnnotation, Simplifier.Annotation);
        }

        protected sealed override bool NeedsToMoveNewLocalDeclarationsNearReference => true;
        protected sealed override async Task MoveNewLocalDeclarationsNearReferenceAsync(
            SyntaxNode memberDeclaration,
            SyntaxEditor editor,
            Document document,
            CancellationToken cancellationToken)
        {
            var service = document.GetLanguageService<IMoveDeclarationNearReferenceService>();
            foreach (TLocalDeclarationStatementSyntax newDecl in memberDeclaration.DescendantNodes().Where(n => n.HasAnnotation(s_newLocalDeclarationStatementAnnotation)))
            {
                // Check if the new variable declaration is unused after all the fixes, and hence can be removed.
                if (await TryRemoveUnusedLocalAsync(newDecl).ConfigureAwait(false))
                {
                    continue;
                }

                // Otherwise, move the declaration closer to the first reference.
                await service.MoveDeclarationNearReferenceAsync(newDecl, document, editor, cancellationToken).ConfigureAwait(false);
            }

            // Local functions.
            async Task<bool> TryRemoveUnusedLocalAsync(TLocalDeclarationStatementSyntax newDecl)
            {
                if (newDecl.HasAnnotation(s_unusedLocalDeclarationAnnotation))
                {
                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                    var local = GetSingleDeclaredLocal(newDecl, semanticModel, cancellationToken);
                    Contract.ThrowIfNull(local);

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
    }
}
