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
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedAssignmentsCodeFixProvider<TStatementSyntax, TBlockSyntax> : AbstractRemoveUnusedExpressionsOrAssignmentsCodeFixProvider
        where TStatementSyntax : SyntaxNode
        where TBlockSyntax : TStatementSyntax
    {
        protected sealed override string FixableDiagnosticId => IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId;

        protected abstract SyntaxNode UpdateNameForFlaggedNode(SyntaxNode node, SyntaxToken newName);
        protected abstract TBlockSyntax GenerateBlock(IEnumerable<TStatementSyntax> statements);

        protected sealed override Task FixAllAsync(
            IOrderedEnumerable<Diagnostic> diagnostics,
            SemanticModel semanticModel,
            SyntaxNode root,
            UnusedExpressionAssignmentPreference preference,
            Func<SyntaxNode, string> generateUniqueNameAtSpanStart,
            SyntaxEditor editor,
            CancellationToken cancellationToken)
        {
            IEnumerable<(SyntaxNode node, bool isUnusedSymbolReference)> nodesToFix = diagnostics.Select(
                d => (root.FindNode(d.Location.SourceSpan), AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.IsUnusedLocalDiagnostic(d)));

            var declarationStatementsBuilder = ArrayBuilder<TStatementSyntax>.GetInstance();
            var nameReplacementsMap = PooledDictionary<SyntaxNode, SyntaxNode>.GetInstance();

            try
            {
                // Note this fixer only operates on code blocks which have no syntax errors (see "HasSyntaxErrors" usage in AbstractRemoveUnusedExpressionsDiagnosticAnalyzer).
                // Hence, we can assume that each node to fix is parented by a StatementSyntax node.
                foreach (var nodesByStatement in nodesToFix.GroupBy(n => n.node.FirstAncestorOrSelf<TStatementSyntax>()))
                {
                    var statement = nodesByStatement.Key;
                    declarationStatementsBuilder.Clear();
                    nameReplacementsMap.Clear();

                    foreach (var (node, isUnusedSymbolReference) in nodesByStatement)
                    {
                        var newName = preference == UnusedExpressionAssignmentPreference.DiscardVariable ? "_" : generateUniqueNameAtSpanStart(node);
                        var newNameToken = editor.Generator.Identifier(newName);
                        nameReplacementsMap.Add(node, UpdateNameForFlaggedNode(node, newNameToken));

                        var declaredLocal = semanticModel.GetDeclaredSymbol(node, cancellationToken) as ILocalSymbol;
                        if (declaredLocal != null)
                        {
                            // We have a dead initialization for a local declaration.
                            if (!isUnusedSymbolReference)
                            {
                                declarationStatementsBuilder.Add((TStatementSyntax)editor.Generator.LocalDeclarationStatement(declaredLocal.Type, declaredLocal.Name));
                            }
                            else
                            {
                                Debug.Assert(preference == UnusedExpressionAssignmentPreference.DiscardVariable);
                            }
                        }
                        else
                        {
                            // We have a dead assignment to a local/parameter.
                            if (preference == UnusedExpressionAssignmentPreference.UnusedLocalVariable)
                            {
                                var type = semanticModel.GetTypeInfo(node, cancellationToken).Type;
                                Contract.ThrowIfNull(type);
                                declarationStatementsBuilder.Add((TStatementSyntax)editor.Generator.LocalDeclarationStatement(type, generateUniqueNameAtSpanStart(node)));
                            }
                        }
                    }

                    var newStatement = statement.ReplaceNodes(nameReplacementsMap.Keys, computeReplacementNode: (n, _) => nameReplacementsMap[n]);
                    if (declarationStatementsBuilder.Count > 0)
                    {
                        // If parent is a block, just insert new declarations before the current statement.
                        if (statement.Parent is TBlockSyntax)
                        {
                            editor.InsertBefore(statement, declarationStatementsBuilder.ToImmutable());
                        }
                        else
                        {
                            // Otherwise, wrap the declaration statements and the current statement with a new block.
                            declarationStatementsBuilder.Add(newStatement);
                            newStatement = GenerateBlock(declarationStatementsBuilder);
                        }
                    }

                    editor.ReplaceNode(statement, newStatement);
                }
            }
            finally
            {
                declarationStatementsBuilder.Free();
                nameReplacementsMap.Free();
            }

            return Task.CompletedTask;
        }
    }
}
