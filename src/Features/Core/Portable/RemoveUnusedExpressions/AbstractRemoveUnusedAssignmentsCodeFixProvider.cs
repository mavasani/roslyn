// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedAssignmentsCodeFixProvider<TExpressionStatementSyntax, TExpressionSyntax> : AbstractRemoveUnusedExpressionsOrAssignmentsCodeFixProvider
        where TExpressionStatementSyntax : SyntaxNode
        where TExpressionSyntax : SyntaxNode
    {
        protected sealed override string FixableDiagnosticId => IDEDiagnosticIds.ValueAssignedIsUnusedDiagnosticId;

        protected abstract TExpressionSyntax GetExpression(TExpressionStatementSyntax expressionStatement);

        protected sealed override async Task FixAllAsync(
            Document document, ImmutableArray<Diagnostic> diagnostics,
            SyntaxEditor editor, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var usedNames = PooledHashSet<string>.GetInstance();

            try
            {
                foreach (var diagnostic in diagnostics.OrderBy(d => d.Location.SourceSpan.Start))
                {
                    var expressionStatement = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TExpressionStatementSyntax>();
                    if (expressionStatement == null)
                    {
                        continue;
                    }

                    var expression = GetExpression(expressionStatement);
                    var preference = AbstractRemoveUnusedExpressionsDiagnosticAnalyzer.GetUnusedExpressionAssignmentPreference(diagnostic);
                    switch (preference)
                    {
                        case UnusedExpressionAssignmentPreference.DiscardVariable:
                            Debug.Assert(semanticModel.Language != LanguageNames.VisualBasic);
                            var discardAssignmentExpression = GenerateDiscardAssignmentExpression(expression);
                            editor.ReplaceNode(expression, discardAssignmentExpression);
                            break;

                        case UnusedExpressionAssignmentPreference.UnusedLocalVariable:
                            var localDecl = GenerateNewLocalDeclarationStatement(expressionStatement, expression);
                            editor.ReplaceNode(expressionStatement, localDecl);
                            break;
                    }
                }
            }
            finally
            {
                usedNames.Free();
            }

            return;

            // Local functions.
            TExpressionSyntax GenerateDiscardAssignmentExpression(TExpressionSyntax expression)
            {
                var discardAssignemnt = (TExpressionSyntax)editor.Generator.AssignmentStatement(
                    left: editor.Generator.IdentifierName("_"),
                    right: expression)
                    .WithTriviaFrom(expression)
                    .WithAdditionalAnnotations(Simplifier.Annotation);

                // Check if we have an actual discard declaration, i.e. "var _ = ...",
                // below this expression which collides with the discard assignment "_ = ...",
                // and will cause a compiler error.
                // If so, display a warning annotation to the user.
                var discardSymbol = semanticModel.LookupSymbols(expression.SpanStart, name: "_").FirstOrDefault();
                if (discardSymbol != null && discardSymbol.Locations[0].SourceSpan.Start > discardAssignemnt.SpanStart)
                {
                    discardAssignemnt = discardAssignemnt.WithAdditionalAnnotations(
                        WarningAnnotation.Create(FeaturesResources.Warning_colon_Conflict_with_discard_variable_declaration_below_in_this_method));
                }

                return discardAssignemnt;
            }

            SyntaxNode GenerateNewLocalDeclarationStatement(TExpressionStatementSyntax expressionStatement, TExpressionSyntax expression)
            {
                var localName = GenerateUniqueName(expressionStatement);

                // Mark with simplifier annotation so that 'var'/explicit type is correctly
                // added based on user options.
                return editor.Generator.LocalDeclarationStatement(
                    name: localName,
                    initializer: expression)
                    .WithTriviaFrom(expressionStatement)
                    .WithAdditionalAnnotations(Simplifier.Annotation);
            }

            string GenerateUniqueName(SyntaxNode node)
            {
                var name = NameGenerator.GenerateUniqueName("unused",
                    n => !usedNames.Contains(n) && semanticModel.LookupSymbols(node.SpanStart, name: n).IsEmpty);
                usedNames.Add(name);
                return name;
            }
        }
    }
}
