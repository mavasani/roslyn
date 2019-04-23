// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.IntroduceUsingStatement
{
    internal abstract partial class AbstractIntroduceUsingStatementService<TStatementSyntax, TLocalDeclarationStatementSyntax, TAssignmentStatementSyntax>
        : IIntroduceUsingStatementService
        where TStatementSyntax : SyntaxNode
        where TLocalDeclarationStatementSyntax : TStatementSyntax
        where TAssignmentStatementSyntax : TStatementSyntax
    {
        protected abstract bool SupportsWrappingAssignmentStatementInUsing { get; }
        protected abstract bool CanRefactorToContainBlockStatements(SyntaxNode parent);
        protected abstract SyntaxList<TStatementSyntax> GetStatements(SyntaxNode parentOfStatementsToSurround);
        protected abstract SyntaxNode WithStatements(SyntaxNode parentOfStatementsToSurround, SyntaxList<TStatementSyntax> statements);

        protected abstract TStatementSyntax CreateUsingStatement(TStatementSyntax declarationOrAssignmentStatement, SyntaxTriviaList sameLineTrivia, SyntaxList<TStatementSyntax> statementsToSurround);

        public async Task<bool> CanIntroduceUsingStatementAsync(Document document, SyntaxNode disposableCreation, CancellationToken cancellationToken)
        {
            if (!CanRefactorToContainBlockStatements(disposableCreation.Parent))
            {
                return false;
            }

            if (!SupportsWrappingAssignmentStatementInUsing && disposableCreation is TAssignmentStatementSyntax)
            {
                return false;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            var disposableType = semanticModel.Compilation.GetSpecialType(SpecialType.System_IDisposable);
            if (disposableType is null)
            {
                return false;
            }

            var symbol = GetAssignedSymbol(semanticModel, disposableCreation, cancellationToken);
            var type = symbol?.GetSymbolType();
            return IsLegalUsingStatementType(semanticModel.Compilation, disposableType, type);
        }

        private static ISymbol GetAssignedSymbol(SemanticModel semanticModel, SyntaxNode disposableCreation, CancellationToken cancellationToken)
        {
            var operation = semanticModel.GetOperation(disposableCreation, cancellationToken);
            switch (operation)
            {
                case IVariableDeclarationGroupOperation variableDeclarationGroup:
                    return GetDeclaredLocal(variableDeclarationGroup);

                case IVariableDeclaratorOperation variableDeclarator:
                    return GetDeclaredLocal(variableDeclarator.Parent?.Parent as IVariableDeclarationGroupOperation);

                case ISimpleAssignmentOperation assignmentStatement:
                    switch (assignmentStatement.Target)
                    {
                        case IParameterReferenceOperation parameterReference:
                            return parameterReference.Parameter;

                        case ILocalReferenceOperation localReference:
                            return localReference.Local;
                    }

                    return null;

                default:
                    return null;
            }
        }

        private static ILocalSymbol GetDeclaredLocal(IVariableDeclarationGroupOperation variableDeclarationGroup)
        {
            if (variableDeclarationGroup?.Declarations.Length != 1)
            {
                return null;
            }

            var localDeclaration = variableDeclarationGroup.Declarations[0];
            if (localDeclaration.Declarators.Length != 1)
            {
                return null;
            }

            var declarator = localDeclaration.Declarators[0];
            var initializer = declarator.GetVariableInitializer();

            // Initializer kind is invalid when incomplete declaration syntax ends in an equals token.
            if (initializer is null || initializer.Kind == OperationKind.Invalid)
            {
                return null;
            }

            return declarator.Symbol;
        }

        /// <summary>
        /// Up to date with C# 7.3. Pattern-based disposal is likely to be added to C# 8.0,
        /// in which case accessible instance and extension methods will need to be detected.
        /// </summary>
        private static bool IsLegalUsingStatementType(Compilation compilation, ITypeSymbol disposableType, ITypeSymbol type)
        {
            if (disposableType == null || type == null)
            {
                return false;
            }

            // CS1674: type used in a using statement must be implicitly convertible to 'System.IDisposable'
            return compilation.ClassifyCommonConversion(type, disposableType).IsImplicit;
        }

        public async Task<Document> IntroduceUsingStatementAsync(Document document, SyntaxNode disposableCreation, CancellationToken cancellationToken)
        {
            Debug.Assert(disposableCreation is TLocalDeclarationStatementSyntax || disposableCreation is TAssignmentStatementSyntax);
            var disposableCreationStatement = (TStatementSyntax)disposableCreation;

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var assignedSymbol = GetAssignedSymbol(semanticModel, disposableCreation, cancellationToken);
            Debug.Assert(assignedSymbol != null);

            var syntaxFactsService = document.GetLanguageService<ISyntaxFactsService>();

            var statementsToSurround = GetStatementsToSurround(disposableCreationStatement, assignedSymbol, semanticModel, syntaxFactsService, cancellationToken);

            // Separate the newline from the trivia that is going on the using declaration line.
            var trailingTrivia = SplitTrailingTrivia(disposableCreation, syntaxFactsService);

            var usingStatement =
                CreateUsingStatement(
                    disposableCreationStatement,
                    trailingTrivia.sameLine,
                    statementsToSurround)
                    .WithLeadingTrivia(disposableCreation.GetLeadingTrivia())
                    .WithTrailingTrivia(trailingTrivia.endOfLine);

            if (statementsToSurround.Any())
            {
                var parentStatements = GetStatements(disposableCreationStatement.Parent);
                var declarationStatementIndex = parentStatements.IndexOf(disposableCreationStatement);

                var newParent = WithStatements(
                    disposableCreation.Parent,
                    new SyntaxList<TStatementSyntax>(parentStatements
                        .Take(declarationStatementIndex)
                        .Concat(usingStatement)
                        .Concat(parentStatements.Skip(declarationStatementIndex + 1 + statementsToSurround.Count))));

                return document.WithSyntaxRoot(root.ReplaceNode(
                    disposableCreationStatement.Parent,
                    newParent.WithAdditionalAnnotations(Formatter.Annotation)));
            }
            else
            {
                // Either the parent is not blocklike, meaning WithStatements can’t be used as in the other branch,
                // or there’s just no need to replace more than the statement itself because no following statements
                // will be surrounded.
                return document.WithSyntaxRoot(root.ReplaceNode(
                    disposableCreationStatement,
                    usingStatement.WithAdditionalAnnotations(Formatter.Annotation)));
            }
        }

        private SyntaxList<TStatementSyntax> GetStatementsToSurround(
            TStatementSyntax declarationOrAssignmentStatement,
            ISymbol symbol,
            SemanticModel semanticModel,
            ISyntaxFactsService syntaxFactsService,
            CancellationToken cancellationToken)
        {
            // Find the minimal number of statements to move into the using block
            // in order to not break existing references to the local.
            var lastUsageStatement = FindSiblingStatementContainingLastUsage(
                declarationOrAssignmentStatement,
                symbol,
                semanticModel,
                syntaxFactsService,
                cancellationToken);

            if (lastUsageStatement == null)
            {
                return default;
            }

            var parentStatements = GetStatements(declarationOrAssignmentStatement.Parent);
            var declarationStatementIndex = parentStatements.IndexOf(declarationOrAssignmentStatement);
            var lastUsageStatementIndex = parentStatements.IndexOf(lastUsageStatement, declarationStatementIndex + 1);

            return new SyntaxList<TStatementSyntax>(parentStatements
                .Take(lastUsageStatementIndex + 1)
                .Skip(declarationStatementIndex + 1));
        }

        private static (SyntaxTriviaList sameLine, SyntaxTriviaList endOfLine) SplitTrailingTrivia(SyntaxNode node, ISyntaxFactsService syntaxFactsService)
        {
            var trailingTrivia = node.GetTrailingTrivia();
            var lastIndex = trailingTrivia.Count - 1;

            return lastIndex != -1 && syntaxFactsService.IsEndOfLineTrivia(trailingTrivia[lastIndex])
                ? (sameLine: trailingTrivia.RemoveAt(lastIndex), endOfLine: new SyntaxTriviaList(trailingTrivia[lastIndex]))
                : (sameLine: trailingTrivia, endOfLine: SyntaxTriviaList.Empty);
        }

        private static TStatementSyntax FindSiblingStatementContainingLastUsage(
            TStatementSyntax declarationOrAssignmentStatement,
            ISymbol symbol,
            SemanticModel semanticModel,
            ISyntaxFactsService syntaxFactsService,
            CancellationToken cancellationToken)
        {
            foreach (var nodeOrToken in declarationOrAssignmentStatement.Parent.ChildNodesAndTokens().Reverse())
            {
                var node = (TStatementSyntax)nodeOrToken.AsNode();
                if (node is null)
                {
                    continue;
                }

                if (node == declarationOrAssignmentStatement)
                {
                    break; // Ignore the declaration and usages prior to the declaration
                }

                if (ContainsReference(node, symbol, semanticModel, syntaxFactsService, cancellationToken))
                {
                    return node;
                }
            }

            return null;
        }

        private static bool ContainsReference(
            SyntaxNode node,
            ISymbol symbol,
            SemanticModel semanticModel,
            ISyntaxFactsService syntaxFactsService,
            CancellationToken cancellationToken)
        {
            if (syntaxFactsService.IsIdentifierName(node))
            {
                var identifierName = syntaxFactsService.GetIdentifierOfSimpleName(node).ValueText;

                return syntaxFactsService.StringComparer.Equals(symbol.Name, identifierName) &&
                    symbol.Equals(semanticModel.GetSymbolInfo(node).Symbol);
            }

            foreach (var nodeOrToken in node.ChildNodesAndTokens())
            {
                var childNode = nodeOrToken.AsNode();
                if (childNode is null)
                {
                    continue;
                }

                if (ContainsReference(childNode, symbol, semanticModel, syntaxFactsService, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
