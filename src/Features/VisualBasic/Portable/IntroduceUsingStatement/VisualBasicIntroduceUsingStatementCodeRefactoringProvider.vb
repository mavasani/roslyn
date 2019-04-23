' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Composition
Imports Microsoft.CodeAnalysis.CodeRefactorings
Imports Microsoft.CodeAnalysis.IntroduceUsingStatement
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.IntroduceUsingStatement

    <ExtensionOrder(Before:=PredefinedCodeRefactoringProviderNames.IntroduceVariable)>
    <ExportCodeRefactoringProvider(LanguageNames.VisualBasic, Name:=PredefinedCodeRefactoringProviderNames.IntroduceUsingStatement), [Shared]>
    Friend NotInheritable Class VisualBasicIntroduceUsingStatementCodeRefactoringProvider
        Inherits AbstractIntroduceUsingStatementCodeRefactoringProvider(Of StatementSyntax, LocalDeclarationStatementSyntax)

        Protected Overrides ReadOnly Property CodeActionTitle As String = VBFeaturesResources.Introduce_Using_statement
    End Class
End Namespace
