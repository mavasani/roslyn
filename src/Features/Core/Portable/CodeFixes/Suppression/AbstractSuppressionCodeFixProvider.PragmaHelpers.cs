// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal partial class AbstractSuppressionCodeFixProvider
    {
        private static class PragmaHelpers
        {
            internal async static Task<Document> GetChangeDocumentWithPragmaAdjustedAsync(
                Document document,
                SyntaxToken startToken,
                SyntaxToken endToken,
                SyntaxNode nodeWithTokens,
                Func<SyntaxToken, SyntaxToken> getNewStartToken,
                Func<SyntaxToken, SyntaxToken> getNewEndToken,
                CancellationToken cancellationToken)
            {
                var startAndEndTokenAreTheSame = startToken == endToken;
                SyntaxToken newStartToken = getNewStartToken(startToken);

                SyntaxToken newEndToken = endToken;
                if (startAndEndTokenAreTheSame)
                {
                    newEndToken = newStartToken;
                }

                newEndToken = getNewEndToken(newEndToken);

                SyntaxNode newNode;
                if (startAndEndTokenAreTheSame)
                {
                    newNode = nodeWithTokens.ReplaceToken(startToken, newEndToken);
                }
                else
                {
                    newNode = nodeWithTokens.ReplaceTokens(new[] { startToken, endToken }, (o, n) => o == startToken ? newStartToken : newEndToken);
                }

                var root = await nodeWithTokens.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var newRoot = root.ReplaceNode(nodeWithTokens, newNode);
                return document.WithSyntaxRoot(newRoot);
            }

            internal static SyntaxToken GetNewStartTokenWithAddedPragma(SyntaxToken startToken, Diagnostic diagnostic, AbstractSuppressionCodeFixProvider fixer, bool isRemoveSuppression = false)
            {
                var trivia = startToken.LeadingTrivia.ToImmutableArray();

                // Insert the #pragma disable directive after all leading new line trivia but before first trivia of any other kind.
                int index;
                SyntaxTrivia firstNonEOLTrivia = trivia.FirstOrDefault(t => !fixer.IsEndOfLine(t));
                if (firstNonEOLTrivia == default(SyntaxTrivia))
                {
                    index = trivia.Length;
                }
                else
                {
                    index = trivia.IndexOf(firstNonEOLTrivia);
                }

                bool needsLeadingEOL;
                if (index > 0)
                {
                    needsLeadingEOL = !fixer.IsEndOfLine(trivia[index - 1]);
                }
                else if (startToken.FullSpan.Start == 0)
                {
                    needsLeadingEOL = false;
                }
                else
                {
                    needsLeadingEOL = true;
                }

                var pragmaTrivia = !isRemoveSuppression ?
                    fixer.CreatePragmaDisableDirectiveTrivia(diagnostic, needsLeadingEOL) :
                    fixer.CreatePragmaRestoreDirectiveTrivia(diagnostic, needsLeadingEOL);

                return startToken.WithLeadingTrivia(trivia.InsertRange(index, pragmaTrivia));
            }

            internal static SyntaxToken GetNewEndTokenWithAddedPragma(SyntaxToken endToken, Diagnostic diagnostic, AbstractSuppressionCodeFixProvider fixer, bool isRemoveSuppression = false)
            {
                ImmutableArray<SyntaxTrivia> trivia;
                var isEOF = fixer.IsEndOfFileToken(endToken);
                if (isEOF)
                {
                    trivia = endToken.LeadingTrivia.ToImmutableArray();
                }
                else
                {
                    trivia = endToken.TrailingTrivia.ToImmutableArray();
                }

                SyntaxTrivia lastNonEOLTrivia = trivia.LastOrDefault(t => !fixer.IsEndOfLine(t));

                // Insert the #pragma restore directive after the last trailing trivia that is not a new line trivia.
                int index;
                if (lastNonEOLTrivia == default(SyntaxTrivia))
                {
                    index = 0;
                }
                else
                {
                    index = trivia.IndexOf(lastNonEOLTrivia) + 1;
                }

                bool needsTrailingEOL;
                if (index < trivia.Length)
                {
                    needsTrailingEOL = !fixer.IsEndOfLine(trivia[index]);
                }
                else if (isEOF)
                {
                    needsTrailingEOL = false;
                }
                else 
                {
                    needsTrailingEOL = true;
                }

                var pragmaTrivia = !isRemoveSuppression ?
                    fixer.CreatePragmaRestoreDirectiveTrivia(diagnostic, needsTrailingEOL) :
                    fixer.CreatePragmaDisableDirectiveTrivia(diagnostic, needsTrailingEOL);

                if (isEOF)
                {
                    return endToken.WithLeadingTrivia(trivia.InsertRange(index, pragmaTrivia));
                }
                else
                {
                    return endToken.WithTrailingTrivia(trivia.InsertRange(index, pragmaTrivia));
                }
            }
        }
    }
}
