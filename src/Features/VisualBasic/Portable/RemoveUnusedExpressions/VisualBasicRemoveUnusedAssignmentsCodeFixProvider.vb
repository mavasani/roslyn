' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Composition
Imports System.Threading
Imports Microsoft.CodeAnalysis.CodeFixes
Imports Microsoft.CodeAnalysis.Editing
Imports Microsoft.CodeAnalysis.RemoveUnusedExpressions
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.RemoveUnusedExpressions
    <ExportCodeFixProvider(LanguageNames.VisualBasic, Name:=PredefinedCodeFixProviderNames.RemoveUnusedExpressions), [Shared]>
    Friend Class VisualBasicRemoveUnusedExpressionsCodeFixProvider
        Inherits AbstractRemoveUnusedExpressionsCodeFixProvider(Of ExpressionStatementSyntax, ExpressionSyntax)

        Protected Overrides Sub RemoveDiscardDeclarations(memberDeclaration As SyntaxNode, editor As SyntaxEditor, cancellationToken As CancellationToken)
            Throw New NotImplementedException()
        End Sub

        Protected Overrides Function GetExpression(expressionStatement As ExpressionStatementSyntax) As ExpressionSyntax
            Return expressionStatement.Expression
        End Function
    End Class
End Namespace
