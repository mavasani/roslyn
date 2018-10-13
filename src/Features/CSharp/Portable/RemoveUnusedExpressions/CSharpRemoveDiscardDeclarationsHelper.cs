// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.RemoveUnusedExpressions
{
    internal static class CSharpRemoveDiscardDeclarationsHelper
    {
        public static void RemoveDiscardDeclarations(
            SyntaxNode memberDeclaration,
            SyntaxEditor editor,
            CancellationToken cancellationToken)
        {
            foreach (var child in memberDeclaration.DescendantNodes())
            {
                if (child is LocalDeclarationStatementSyntax localDeclarationStatement
                    && localDeclarationStatement.Declaration.Variables.Any(v => v.Identifier.Text == "_"))
                {
                    ProcessVariableDeclarationWithDiscard(localDeclarationStatement);
                }
            }

            return;

            void ProcessVariableDeclarationWithDiscard(LocalDeclarationStatementSyntax localDeclarationStatement)
            {
                var statementsBuilder = ArrayBuilder<StatementSyntax>.GetInstance();
                var variableDeclaration = localDeclarationStatement.Declaration;
                var currentNonDiscardVariables = new SeparatedSyntaxList<VariableDeclaratorSyntax>();

                try
                {
                    foreach (var variable in variableDeclaration.Variables)
                    {
                        if (variable.Identifier.Text != "_")
                        {
                            currentNonDiscardVariables = currentNonDiscardVariables.Add(variable);
                        }
                        else
                        {
                            ProcessCurrentNonDiscardVariables();
                            ProcessDiscardVariable(variable);
                        }
                    }

                    ProcessCurrentNonDiscardVariables();

                    if (statementsBuilder.Count == 0)
                    {
                        return;
                    }

                    var leadingTrivia = localDeclarationStatement.GetLeadingTrivia()
                                        .Concat(variableDeclaration.Type.GetLeadingTrivia())
                                        .Concat(variableDeclaration.Type.GetTrailingTrivia());
                    statementsBuilder[0] = statementsBuilder[0].WithLeadingTrivia(leadingTrivia);

                    var last = statementsBuilder.Count - 1;
                    var trailingTrivia = localDeclarationStatement.SemicolonToken.GetAllTrivia();
                    statementsBuilder[last] = statementsBuilder[last].WithTrailingTrivia(trailingTrivia);

                    if (localDeclarationStatement.Parent is BlockSyntax)
                    {
                        editor.InsertAfter(localDeclarationStatement, statementsBuilder.Skip(1));
                        editor.ReplaceNode(localDeclarationStatement, statementsBuilder[0]);
                    }
                    else
                    {
                        editor.ReplaceNode(localDeclarationStatement, SyntaxFactory.Block(statementsBuilder));
                    }
                }
                finally
                {
                    statementsBuilder.Free();
                }

                return;

                // Local functions.
                void ProcessCurrentNonDiscardVariables()
                {
                    if (currentNonDiscardVariables.Count > 0)
                    {
                        var newVariableDeclaration = SyntaxFactory.VariableDeclaration(variableDeclaration.Type.WithoutTrivia(), currentNonDiscardVariables);
                        statementsBuilder.Add(SyntaxFactory.LocalDeclarationStatement(newVariableDeclaration));
                        currentNonDiscardVariables = new SeparatedSyntaxList<VariableDeclaratorSyntax>();
                    }
                }

                void ProcessDiscardVariable(VariableDeclaratorSyntax variable)
                {
                    if (variable.Initializer != null)
                    {
                        statementsBuilder.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                kind: SyntaxKind.SimpleAssignmentExpression,
                                left: SyntaxFactory.IdentifierName(variable.Identifier),
                                operatorToken: variable.Initializer.EqualsToken,
                                right: variable.Initializer.Value)));
                    }
                }
            }
        }
    }
}
