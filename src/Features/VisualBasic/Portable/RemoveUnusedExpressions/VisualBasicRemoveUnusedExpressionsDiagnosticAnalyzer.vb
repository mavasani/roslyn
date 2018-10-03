' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.CodeAnalysis.Diagnostics
Imports Microsoft.CodeAnalysis.RemoveUnusedExpressions

Namespace Microsoft.CodeAnalysis.VisualBasic.RemoveUnusedExpressions

    <DiagnosticAnalyzer(LanguageNames.VisualBasic)>
    Friend NotInheritable Class VisualBasicRemoveUnusedExpressionsDiagnosticAnalyzer
        Inherits AbstractRemoveUnusedExpressionsDiagnosticAnalyzer
        Public Sub New()
            MyBase.New(supportsDiscard:=False)
        End Sub

        Protected Overrides Function GetDefinitionLocationToFade(unusedDefinition As IOperation) As Location
            Return unusedDefinition.Syntax.GetLocation()
        End Function
    End Class
End Namespace
