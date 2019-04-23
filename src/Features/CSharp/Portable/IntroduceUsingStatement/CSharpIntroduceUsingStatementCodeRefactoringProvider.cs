// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.IntroduceUsingStatement;

namespace Microsoft.CodeAnalysis.CSharp.IntroduceUsingStatement
{
    [ExtensionOrder(Before = PredefinedCodeRefactoringProviderNames.IntroduceVariable)]
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = PredefinedCodeRefactoringProviderNames.IntroduceUsingStatement), Shared]
    internal sealed class CSharpIntroduceUsingStatementCodeRefactoringProvider
        : AbstractIntroduceUsingStatementCodeRefactoringProvider<StatementSyntax, LocalDeclarationStatementSyntax>
    {
        protected override string CodeActionTitle => CSharpFeaturesResources.Introduce_using_statement;
    }
}
