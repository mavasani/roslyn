// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal abstract partial class AbstractSuppressionCodeFixProvider : ISuppressionFixProvider
    {
        internal sealed class RemoveSuppressionCodeAction : AbstractSuppressionCodeAction
        {
            private readonly SyntaxToken _startToken;
            private readonly SyntaxToken _endToken;
            private readonly SyntaxNode _nodeWithTokens;
            private readonly Document _document;
            private readonly Diagnostic _diagnostic;
            private readonly bool _forFixMultipleContext;

            public RemoveSuppressionCodeAction(
                AbstractSuppressionCodeFixProvider fixer,
                SyntaxToken startToken,
                SyntaxToken endToken,
                SyntaxNode nodeWithTokens,
                Document document,
                Diagnostic diagnostic,
                bool forFixMultipleContext = false)
                : base(fixer, title: string.Format(FeaturesResources.RemoveSuppressionForId, diagnostic.Id))
            {
                NormalizeTriviaOnTokens(ref document, ref startToken, ref endToken, ref nodeWithTokens, fixer);
                _startToken = startToken;
                _endToken = endToken;
                _nodeWithTokens = nodeWithTokens;
                _document = document;
                _diagnostic = diagnostic;
                _forFixMultipleContext = forFixMultipleContext;
            }

            private void NormalizeTriviaOnTokens(
                ref Document document,
                ref SyntaxToken startToken,
                ref SyntaxToken endToken,
                ref SyntaxNode nodeWithTokens,
                AbstractSuppressionCodeFixProvider fixer)
            {
                var previousOfStart = startToken.GetPreviousToken();
                var nextOfEnd = endToken.GetNextToken();
                if (!previousOfStart.HasTrailingTrivia && !nextOfEnd.HasLeadingTrivia)
                {
                    return;
                }

                var root = nodeWithTokens.SyntaxTree.GetRoot();
                var subtreeRoot = root.FindNode(new TextSpan(previousOfStart.FullSpan.Start, nextOfEnd.FullSpan.End - previousOfStart.FullSpan.Start));

                var currentStartToken = startToken;
                var currentEndToken = endToken;
                var newStartToken = startToken.WithLeadingTrivia(previousOfStart.TrailingTrivia.Concat(startToken.LeadingTrivia));
                var newEndToken = endToken.WithTrailingTrivia(endToken.TrailingTrivia.Concat(nextOfEnd.LeadingTrivia));
                var newPreviousOfStart = previousOfStart.WithTrailingTrivia();
                var newNextOfEnd = nextOfEnd.WithLeadingTrivia();

                var newSubtreeRoot = subtreeRoot.ReplaceTokens(new[] { startToken, previousOfStart, endToken, nextOfEnd },
                    (o, n) =>
                    {
                        if (o == currentStartToken)
                        {
                            return newStartToken;
                        }
                        else if (o == previousOfStart)
                        {
                            return newPreviousOfStart;
                        }
                        else if (o == currentEndToken)
                        {
                            return newEndToken;
                        }
                        else if (o == nextOfEnd)
                        {
                            return newNextOfEnd;
                        }
                        else
                        {
                            return n;
                        }
                    });

                root = root.ReplaceNode(subtreeRoot, newSubtreeRoot);
                startToken = root.FindToken(startToken.SpanStart);
                endToken = root.FindToken(endToken.SpanStart);
                nodeWithTokens = fixer.GetNodeWithTokens(startToken, endToken, root);
                document = document.WithSyntaxRoot(root);
            }

            public RemoveSuppressionCodeAction CloneForFixMultipleContext()
            {
                return new RemoveSuppressionCodeAction(Fixer, _startToken, _endToken, _nodeWithTokens, _document, _diagnostic, forFixMultipleContext: true);
            }

            public override string EquivalenceKey => FeaturesResources.RemoveSuppressionEquivalenceKeyPrefix + DiagnosticIdForEquivalenceKey;
            protected override string DiagnosticIdForEquivalenceKey =>
                _forFixMultipleContext ? string.Empty : _diagnostic.Id;

            protected async override Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
            {
                Func<SyntaxToken, SyntaxToken> getNewStartToken;
                Func<SyntaxToken, SyntaxToken> getNewEndToken;

                int indexOfLeadingPragmaDisableToRemove = -1, indexOfTrailingPragmaEnableToRemove = -1;
                if (CanRemovePragmaTrivia(_startToken, _diagnostic, Fixer, leading: true, indexOfTriviaToRemove: out indexOfLeadingPragmaDisableToRemove) &&
                    CanRemovePragmaTrivia(_endToken, _diagnostic, Fixer, leading: false, indexOfTriviaToRemove: out indexOfTrailingPragmaEnableToRemove))
                {
                    // Verify if there is no other trivia before the start token would again cause this diagnostic to be suppressed.
                    // If invalidated, then we just toggle existing pragma enable and disable directives before and start of the line.
                    // If not, then we just remove the existing pragma trivia surrounding the line.
                    var toggle = await IsDiagnosticSuppressedBeforeLeadingPragmaAsync(indexOfLeadingPragmaDisableToRemove, cancellationToken).ConfigureAwait(false);

                    getNewStartToken = startToken =>
                        GetNewTokenWithPragmaUnsuppress(startToken, indexOfLeadingPragmaDisableToRemove, _diagnostic, Fixer, leading: true, toggle: toggle);
                    getNewEndToken = endToken =>
                        GetNewTokenWithPragmaUnsuppress(endToken, indexOfTrailingPragmaEnableToRemove, _diagnostic, Fixer, leading: false, toggle: toggle);
                }
                else
                {
                    // Otherwise, just add a pragma enable before the start token and a pragma restore after it.
                    getNewStartToken = startToken =>
                        PragmaHelpers.GetNewStartTokenWithAddedPragma(startToken, _diagnostic, Fixer, isRemoveSuppression: true);
                    getNewEndToken = endToken =>
                        PragmaHelpers.GetNewEndTokenWithAddedPragma(endToken, _diagnostic, Fixer, isRemoveSuppression: true);
                }

                return await PragmaHelpers.GetChangeDocumentWithPragmaAdjustedAsync(
                    _document,
                    _startToken,
                    _endToken,
                    _nodeWithTokens,
                    getNewStartToken,
                    getNewEndToken,
                    cancellationToken).ConfigureAwait(false);
            }

            private static bool CanRemovePragmaTrivia(SyntaxToken token, Diagnostic diagnostic, AbstractSuppressionCodeFixProvider fixer, bool leading, out int indexOfTriviaToRemove)
            {
                indexOfTriviaToRemove = -1;
                
                // Handle only simple cases where we have a single pragma directive with single ID matching ours in the trivia.
                var triviaList = leading ? token.LeadingTrivia :
                    (fixer.IsEndOfFileToken(token) ? token.LeadingTrivia : token.TrailingTrivia);
                var seenAnyPragma = false;
                for (int i = 0; i < triviaList.Count; i++)
                {
                    var trivia = triviaList[i];

                    bool isEnableDirective, hasMultipleIds;
                    if (fixer.IsAnyPragmaDirectiveForId(trivia, diagnostic.Id, out isEnableDirective, out hasMultipleIds))
                    {
                        if (seenAnyPragma || hasMultipleIds)
                        {
                            indexOfTriviaToRemove = -1;
                            return false;
                        }

                        seenAnyPragma = true;

                        // We want to look for leading disable directive and trailing enable directive.
                        if ((leading && !isEnableDirective) ||
                            (!leading && isEnableDirective))
                        {
                            indexOfTriviaToRemove = i;
                        }
                    }
                }

                return indexOfTriviaToRemove >= 0;
            }

            private static SyntaxToken GetNewTokenWithPragmaUnsuppress(SyntaxToken token, int indexOfTriviaToRemoveOrToggle, Diagnostic diagnostic, AbstractSuppressionCodeFixProvider fixer, bool leading, bool toggle)
            {
                Contract.ThrowIfFalse(indexOfTriviaToRemoveOrToggle >= 0);

                var triviaList = leading ? token.LeadingTrivia : token.TrailingTrivia;
                
                if (toggle)
                {
                    var triviaToToggle = triviaList.ElementAt(indexOfTriviaToRemoveOrToggle);
                    Contract.ThrowIfFalse(triviaToToggle != default(SyntaxTrivia));
                    var toggledTrivia = fixer.TogglePragmaDirective(triviaToToggle);
                    triviaList = triviaList.Replace(triviaToToggle, toggledTrivia);
                }
                else
                {
                    triviaList = triviaList.RemoveAt(indexOfTriviaToRemoveOrToggle);
                }

                return leading ? token.WithLeadingTrivia(triviaList) : token.WithTrailingTrivia(triviaList);
            }

            private static IEnumerable<SyntaxTrivia> GetNewTriviaListWithPragmaRemoved(SyntaxToken token, SyntaxTrivia pragmaTrivia, bool leading, AbstractSuppressionCodeFixProvider fixer)
            {
                var triviaList = leading ? token.LeadingTrivia : token.TrailingTrivia;
                var removing = false;
                foreach (var trivia in triviaList)
                {
                    if (removing)
                    {
                        if (fixer.IsEndOfLine(trivia))
                        {
                            removing = false;
                        }
                    }
                    else if (trivia == pragmaTrivia)
                    {
                        removing = true;
                    }
                    else
                    {
                        yield return trivia;
                    }
                }
            }

            private async Task<bool> IsDiagnosticSuppressedBeforeLeadingPragmaAsync(int indexOfPragma, CancellationToken cancellationToken)
            {
                var model = await _document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var tree = model.SyntaxTree;

                // get the warning state of this diagnostic ID at the start of the pragma
                var trivia = _startToken.LeadingTrivia.ElementAt(indexOfPragma);
                var spanToCheck = new TextSpan(
                    start: Math.Max(0, trivia.Span.Start - 1),
                    length: 1);
                var locationToCheck = Location.Create(tree, spanToCheck);
                var dummyDiagnosticWithLocationToCheck = Diagnostic.Create(_diagnostic.Descriptor, locationToCheck);
                var effectiveDiagnostic = CompilationWithAnalyzers.GetEffectiveDiagnostics(new[] { dummyDiagnosticWithLocationToCheck }, model.Compilation).FirstOrDefault();
                return effectiveDiagnostic == null || effectiveDiagnostic.IsSuppressed;
            }

            public SyntaxToken StartToken_TestOnly => _startToken;
            public SyntaxToken EndToken_TestOnly => _endToken;
        }
    }
}
