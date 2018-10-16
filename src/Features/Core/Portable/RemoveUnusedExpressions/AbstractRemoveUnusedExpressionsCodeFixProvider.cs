// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.CodeAnalysis.RemoveUnusedExpressions
{
    internal abstract class AbstractRemoveUnusedExpressionsCodeFixProvider<TExpressionStatementSyntax, TExpressionSyntax> : AbstractRemoveUnusedExpressionsOrAssignmentsCodeFixProvider
        where TExpressionStatementSyntax : SyntaxNode
        where TExpressionSyntax : SyntaxNode
    {
        protected sealed override string FixableDiagnosticId => IDEDiagnosticIds.ExpressionValueIsUnusedDiagnosticId;
        protected sealed override bool NeedsToMoveNewLocalDeclarationsNearReference => false;

        protected abstract TExpressionSyntax GetExpression(TExpressionStatementSyntax expressionStatement);

        protected sealed override Task FixAllAsync(
            IOrderedEnumerable<Diagnostic> diagnostics,
            SemanticModel semanticModel,
            SyntaxNode root,
            UnusedExpressionAssignmentPreference preference,
            Func<SyntaxNode, string> generateUniqueNameAtSpanStart,
            SyntaxEditor editor,
            CancellationToken cancellationToken)
        {
            foreach (var diagnostic in diagnostics)
            {
                var expressionStatement = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TExpressionStatementSyntax>();
                if (expressionStatement == null)
                {
                    continue;
                }

                var expression = GetExpression(expressionStatement);
                switch (preference)
                {
                    case UnusedExpressionAssignmentPreference.DiscardVariable:
                        Debug.Assert(semanticModel.Language != LanguageNames.VisualBasic);
                        var discardAssignmentExpression = (TExpressionSyntax)editor.Generator.AssignmentStatement(
                                left: editor.Generator.IdentifierName("_"), right: expression)
                            .WithTriviaFrom(expression)
                            .WithAdditionalAnnotations(Simplifier.Annotation);
                        editor.ReplaceNode(expression, discardAssignmentExpression);
                        break;

                    case UnusedExpressionAssignmentPreference.UnusedLocalVariable:
                        // Add Simplifier annotation so that 'var'/explicit type is correctly added based on user options.
                        var localDecl = editor.Generator.LocalDeclarationStatement(
                                name: generateUniqueNameAtSpanStart(expressionStatement), initializer: expression)
                            .WithTriviaFrom(expressionStatement)
                            .WithAdditionalAnnotations(Simplifier.Annotation);
                        editor.ReplaceNode(expressionStatement, localDecl);
                        break;
                }
            }

            return Task.CompletedTask;
        }
    }
}
