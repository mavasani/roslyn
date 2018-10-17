// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.RemoveUnusedExpressions;

namespace Microsoft.CodeAnalysis.CSharp.RemoveUnusedExpressions
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class CSharpRemoveUnusedExpressionsDiagnosticAnalyzer : AbstractRemoveUnusedExpressionsDiagnosticAnalyzer
    {
        public CSharpRemoveUnusedExpressionsDiagnosticAnalyzer()
            : base(supportsDiscard: true)
        {
        }

        protected override Location GetDefinitionLocationToFade(IOperation unusedDefinition)
        {
            if (unusedDefinition.Syntax is VariableDeclaratorSyntax variableDeclartor)
            {
                return variableDeclartor.Identifier.GetLocation();
            }
            else if (unusedDefinition.Syntax?.Parent is ForEachStatementSyntax forEachStatement &&
                     forEachStatement.Type == unusedDefinition.Syntax)
            {
                return forEachStatement.Identifier.GetLocation();
            }

            return unusedDefinition.Syntax.GetLocation();
        }
    }
}
