' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Composition
Imports Microsoft.CodeAnalysis.Host.Mef
Imports Microsoft.CodeAnalysis.IntroduceUsingStatement
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.IntroduceUsingStatement

    <ExportLanguageService(GetType(IIntroduceUsingStatementService), LanguageNames.VisualBasic), [Shared]>
    Friend NotInheritable Class VisualBasicIntroduceUsingStatementService
        Inherits AbstractIntroduceUsingStatementService(Of StatementSyntax, LocalDeclarationStatementSyntax, AssignmentStatementSyntax)

        Protected Overrides ReadOnly Property SupportsWrappingAssignmentStatementInUsing As Boolean
            Get
                ' VB does not support wrapping assignments in a Using statement
                Return False
            End Get
        End Property

        Protected Overrides Function CanRefactorToContainBlockStatements(parent As SyntaxNode) As Boolean
            ' We don’t care enough about declarations in single-line If, Else, lambdas, etc, to support them.
            Return parent.IsMultiLineExecutableBlock()
        End Function

        Protected Overrides Function GetStatements(parentOfStatementsToSurround As SyntaxNode) As SyntaxList(Of StatementSyntax)
            Return parentOfStatementsToSurround.GetExecutableBlockStatements()
        End Function

        Protected Overrides Function WithStatements(parentOfStatementsToSurround As SyntaxNode, statements As SyntaxList(Of StatementSyntax)) As SyntaxNode
            Return parentOfStatementsToSurround.ReplaceStatements(statements)
        End Function

        Protected Overrides Function CreateUsingStatement(declarationOrAssignmentStatement As StatementSyntax, sameLineTrivia As SyntaxTriviaList, statementsToSurround As SyntaxList(Of StatementSyntax)) As StatementSyntax
            Dim usingStatement =
                SyntaxFactory.UsingStatement(
                    expression:=Nothing,
                    variables:=DirectCast(declarationOrAssignmentStatement, LocalDeclarationStatementSyntax).Declarators)

            If sameLineTrivia.Any Then
                usingStatement = usingStatement.WithTrailingTrivia(sameLineTrivia)
            End If

            Return SyntaxFactory.UsingBlock(usingStatement, statementsToSurround)
        End Function
    End Class
End Namespace
