// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Composition;
using System.Threading;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.RemoveUnusedExpressions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.RemoveUnusedExpressions
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = PredefinedCodeFixProviderNames.RemoveUnusedAssignments), Shared]
    internal class CSharpRemoveUnusedAssignmentsCodeFixProvider : AbstractRemoveUnusedAssignmentsCodeFixProvider<StatementSyntax, BlockSyntax>
    {
        protected override BlockSyntax GenerateBlock(IEnumerable<StatementSyntax> statements) => SyntaxFactory.Block(statements);

        protected override SyntaxNode UpdateNameForFlaggedNode(SyntaxNode node, SyntaxToken newName)
        {
            switch (node.Kind())
            {
                case SyntaxKind.IdentifierName:
                    var identifierName = (IdentifierNameSyntax)node;
                    return identifierName.WithIdentifier(newName.WithTriviaFrom(identifierName.Identifier));

                case SyntaxKind.VariableDeclarator:
                    var variableDeclarator = (VariableDeclaratorSyntax)node;
                    return variableDeclarator.WithIdentifier(newName.WithTriviaFrom(variableDeclarator.Identifier));

                case SyntaxKind.SingleVariableDesignation:
                    return SyntaxFactory.SingleVariableDesignation(newName).WithTriviaFrom(node);

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        protected override void RemoveDiscardDeclarations(
            SyntaxNode memberDeclaration,
            SyntaxEditor editor,
            CancellationToken cancellationToken)
            => CSharpRemoveDiscardDeclarationsHelper.RemoveDiscardDeclarations(memberDeclaration, editor, cancellationToken);
    }
}
