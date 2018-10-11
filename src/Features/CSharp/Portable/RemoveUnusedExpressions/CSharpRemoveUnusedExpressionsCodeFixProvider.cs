﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.RemoveUnusedExpressions;

namespace Microsoft.CodeAnalysis.CSharp.RemoveUnusedExpressions
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = PredefinedCodeFixProviderNames.RemoveUnusedExpressions), Shared]
    internal class CSharpRemoveUnusedExpressionsCodeFixProvider:
        AbstractRemoveUnusedExpressionsCodeFixProvider<ExpressionStatementSyntax, ExpressionSyntax>
    {
        protected override ExpressionSyntax GetExpression(ExpressionStatementSyntax expressionStatement)
            => expressionStatement.Expression;
    }
}
