﻿' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Composition
Imports System.Threading
Imports Microsoft.CodeAnalysis.CodeFixes
Imports Microsoft.CodeAnalysis.Editing
Imports Microsoft.CodeAnalysis.RemoveUnusedExpressions
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.RemoveUnusedExpressions
    <ExportCodeFixProvider(LanguageNames.VisualBasic, Name:=PredefinedCodeFixProviderNames.RemoveUnusedExpressions), [Shared]>
    Friend Class VisualBasicRemoveUnusedExpressionsCodeFixProvider
        Inherits AbstractRemoveUnusedExpressionsCodeFixProvider(Of ExpressionSyntax, StatementSyntax, StatementSyntax,
                                                                   ExpressionStatementSyntax, LocalDeclarationStatementSyntax,
                                                                   VariableDeclaratorSyntax, ForEachBlockSyntax)

        Protected Overrides Function GenerateBlock(statements As IEnumerable(Of StatementSyntax)) As StatementSyntax
            Throw ExceptionUtilities.Unreachable
        End Function

        Protected Overrides Function UpdateNameForFlaggedNode(node As SyntaxNode, newName As SyntaxToken) As SyntaxNode
            Dim modifiedIdentifier = TryCast(node, ModifiedIdentifierSyntax)
            If modifiedIdentifier IsNot Nothing Then
                Return modifiedIdentifier.WithIdentifier(newName).WithTriviaFrom(node)
            End If

            Dim identifier = TryCast(node, IdentifierNameSyntax)
            If identifier IsNot Nothing Then
                Return identifier.WithIdentifier(newName).WithTriviaFrom(node)
            End If

            Return node
        End Function

        Protected Overrides Function GetSingleDeclaredLocal(localDeclaration As LocalDeclarationStatementSyntax, semanticModel As SemanticModel, cancellationToken As CancellationToken) As ILocalSymbol
            Contract.ThrowIfFalse(localDeclaration.Declarators.Count = 1)
            Contract.ThrowIfFalse(localDeclaration.Declarators(0).Names.Count = 1)
            Return DirectCast(semanticModel.GetDeclaredSymbol(localDeclaration.Declarators(0).Names(0), cancellationToken), ILocalSymbol)
        End Function

        Protected Overrides Function RemoveDiscardDeclarationsAsync(memberDeclaration As SyntaxNode, editor As SyntaxEditor, document As Document, cancellationToken As CancellationToken) As Task
            Throw ExceptionUtilities.Unreachable
        End Function

        Protected Overrides Function GetForEachStatementIdentifier(node As ForEachBlockSyntax) As SyntaxToken
            Throw ExceptionUtilities.Unreachable
        End Function
    End Class
End Namespace
