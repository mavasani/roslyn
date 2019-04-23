// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.IntroduceUsingStatement;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.IntroduceUsingStatement
{
    [ExportLanguageService(typeof(IIntroduceUsingStatementService), LanguageNames.CSharp), Shared]
    internal sealed class CSharpIntroduceUsingStatementService
        : AbstractIntroduceUsingStatementService<StatementSyntax, LocalDeclarationStatementSyntax, ExpressionStatementSyntax>
    {
        protected override bool SupportsWrappingAssignmentStatementInUsing => true;

        protected override bool CanRefactorToContainBlockStatements(SyntaxNode parent)
        {
            return parent is BlockSyntax || parent is SwitchSectionSyntax || parent.IsEmbeddedStatementOwner();
        }

        protected override SyntaxList<StatementSyntax> GetStatements(SyntaxNode parentOfStatementsToSurround)
        {
            return
                parentOfStatementsToSurround is BlockSyntax block ? block.Statements :
                parentOfStatementsToSurround is SwitchSectionSyntax switchSection ? switchSection.Statements :
                throw ExceptionUtilities.UnexpectedValue(parentOfStatementsToSurround);
        }

        protected override SyntaxNode WithStatements(SyntaxNode parentOfStatementsToSurround, SyntaxList<StatementSyntax> statements)
        {
            return
                parentOfStatementsToSurround is BlockSyntax block ? block.WithStatements(statements) as SyntaxNode :
                parentOfStatementsToSurround is SwitchSectionSyntax switchSection ? switchSection.WithStatements(statements) :
                throw ExceptionUtilities.UnexpectedValue(parentOfStatementsToSurround);
        }

        protected override StatementSyntax CreateUsingStatement(StatementSyntax declarationOrAssignmentStatement, SyntaxTriviaList sameLineTrivia, SyntaxList<StatementSyntax> statementsToSurround)
        {
            VariableDeclarationSyntax declaration = null;
            ExpressionSyntax expression = null;
            switch (declarationOrAssignmentStatement)
            {
                case LocalDeclarationStatementSyntax declarationStatement:
                    declaration = declarationStatement.Declaration;
                    break;

                case ExpressionStatementSyntax expressionStatement:
                    expression = expressionStatement.Expression;
                    Debug.Assert(expression is AssignmentExpressionSyntax);
                    break;

                default:
                    throw ExceptionUtilities.Unreachable;
            }

            var usingStatement = SyntaxFactory.UsingStatement(
                declaration: declaration?.WithoutTrivia(), // Declaration already has equals token and expression
                expression: expression?.WithoutTrivia(), // Expression already has equals token and expression
                statement: SyntaxFactory.Block(statementsToSurround));

            return usingStatement
                .WithCloseParenToken(usingStatement.CloseParenToken
                    .WithTrailingTrivia(sameLineTrivia));
        }
    }
}
