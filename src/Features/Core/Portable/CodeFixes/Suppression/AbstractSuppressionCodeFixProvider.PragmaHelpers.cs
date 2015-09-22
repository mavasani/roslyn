// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal partial class AbstractSuppressionCodeFixProvider
    {
        private static class PragmaHelpers
        {
            internal async static Task<Document> GetChangeDocumentWithPragmaAdjustedAsync(
                Document document,
                SuppressionTargetInfo suppressionTargetInfo,
                Func<SyntaxToken, SyntaxToken> getNewStartToken,
                Func<SyntaxToken, SyntaxToken> getNewEndToken,
                CancellationToken cancellationToken)
            {
                var startToken = suppressionTargetInfo.StartToken;
                var endToken = suppressionTargetInfo.EndToken;
                var nodeWithTokens = suppressionTargetInfo.NodeWithTokens;

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

                // Insert the #pragma disable directive just before the diagnostic location, but after all trivia.
                int index;
                var spanStart = diagnostic.Location.SourceSpan.Start;
                SyntaxTrivia insertAfterTrivia = trivia.LastOrDefault(t => t.FullSpan.End <= spanStart);
                if (insertAfterTrivia == default(SyntaxTrivia))
                {
                    index = trivia.Length;
                }
                else
                {
                    index = trivia.IndexOf(insertAfterTrivia) + 1;
                }

                bool needsLeadingEOL;
                if (index > 0)
                {
                    needsLeadingEOL = !fixer.IsEndOfLine(insertAfterTrivia);
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
                    fixer.CreatePragmaDisableDirectiveTrivia(diagnostic, needsLeadingEOL, needsTrailingEndOfLine: true) :
                    fixer.CreatePragmaRestoreDirectiveTrivia(diagnostic, needsLeadingEOL, needsTrailingEndOfLine: true);

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

                var spanEnd = diagnostic.Location.SourceSpan.End;
                SyntaxTrivia insertBeforeTrivia = trivia.FirstOrDefault(t => t.FullSpan.Start >= spanEnd);

                // Insert the #pragma disable directive just after the diagnostic location, but before all trivia.
                int index;
                if (insertBeforeTrivia == default(SyntaxTrivia))
                {
                    index = 0;
                }
                else
                {
                    index = trivia.IndexOf(insertBeforeTrivia);
                }

                bool needsTrailingEOL;
                if (index < trivia.Length)
                {
                    needsTrailingEOL = !fixer.IsEndOfLine(insertBeforeTrivia);
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
                    fixer.CreatePragmaRestoreDirectiveTrivia(diagnostic, needsLeadingEndOfLine: true, needsTrailingEndOfLine: needsTrailingEOL) :
                    fixer.CreatePragmaDisableDirectiveTrivia(diagnostic, needsLeadingEndOfLine: true, needsTrailingEndOfLine: needsTrailingEOL);

                if (isEOF)
                {
                    return endToken.WithLeadingTrivia(trivia.InsertRange(index, pragmaTrivia));
                }
                else
                {
                    return endToken.WithTrailingTrivia(trivia.InsertRange(index, pragmaTrivia));
                }
            }

            internal static void ResolveFixAllMergeConflictForPragmaAdd(List<TextChange> cumulativeChanges, int indexOfCurrentCumulativeChange, TextChange conflictingChange, bool isAddPragmaWarningSuppression)
            {
                // If there are multiple diagnostics with different IDs on the same line, we want to retain all the added pragmas.
                var cumulativeChange = cumulativeChanges[indexOfCurrentCumulativeChange];
                var mergedChange = ResolveFixAllMergeConflictForPragmaAdd(cumulativeChange, conflictingChange, isAddPragmaWarningSuppression: false);
                cumulativeChanges[indexOfCurrentCumulativeChange] = mergedChange;
            }

            private static TextChange ResolveFixAllMergeConflictForPragmaAdd(TextChange cumulativeChange, TextChange conflictingChange, bool isAddPragmaWarningSuppression)
            {
                // If one of the change is a removal, just return the other one.
                if (string.IsNullOrEmpty(cumulativeChange.NewText))
                {
                    return conflictingChange;
                }
                else if (string.IsNullOrEmpty(conflictingChange.NewText))
                {
                    return cumulativeChange;
                }
                
                // We have 2 code actions trying to add a pragma directive at the same location.
                // If these are different IDs, then the order doesn't really matter.
                // However, if these are disable and enable directives with same ID, then order does matter.
                // We won't to make sure that for add suppression case, the restore precedes the enable and for remove suppression case, it is vice versa.
                // We get the right ordering by sorting the pragma directive text.
                string newText = cumulativeChange.NewText + conflictingChange.NewText;
                var conflictChangeLexicallySmaller = string.Compare(conflictingChange.NewText, cumulativeChange.NewText, StringComparison.OrdinalIgnoreCase) < 0;
                if ((isAddPragmaWarningSuppression && !conflictChangeLexicallySmaller) ||
                    (!isAddPragmaWarningSuppression && conflictChangeLexicallySmaller))
                {
                    newText = conflictingChange.NewText + cumulativeChange.NewText;
                }

                var newSpan = new TextSpan(cumulativeChange.Span.Start, cumulativeChange.Span.Length);
                return new TextChange(newSpan, newText);
            }
        }
    }
}
