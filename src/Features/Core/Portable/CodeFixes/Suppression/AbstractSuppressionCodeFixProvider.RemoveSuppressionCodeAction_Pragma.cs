// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
        internal abstract partial class RemoveSuppressionCodeAction
        {
            private class PragmaRemoveAction : RemoveSuppressionCodeAction, IPragmaBasedCodeAction
            {
                private readonly SuppressionTargetInfo _suppressionTargetInfo;

                public static PragmaRemoveAction Create(
                    SuppressionTargetInfo suppressionTargetInfo,
                    Document document,
                    Diagnostic diagnostic,
                    AbstractSuppressionCodeFixProvider fixer)
                {
                    // We need to normalize the leading trivia on start token to account for
                    // the trailing trivia on its previous token (and similarly normalize trailing trivia for end token).
                    NormalizeTriviaOnTokens(fixer, ref document, ref suppressionTargetInfo);

                    return new PragmaRemoveAction(suppressionTargetInfo, document, diagnostic, fixer);
                }

                private PragmaRemoveAction(
                    SuppressionTargetInfo suppressionTargetInfo,
                    Document document,
                    Diagnostic diagnostic,
                    AbstractSuppressionCodeFixProvider fixer,
                    bool forFixMultipleContext = false)
                    : base(document, diagnostic, fixer, forFixMultipleContext)
                {
                    _suppressionTargetInfo = suppressionTargetInfo;
                }

                public override RemoveSuppressionCodeAction CloneForFixMultipleContext()
                {
                    return new PragmaRemoveAction(_suppressionTargetInfo, _document, _diagnostic, Fixer, forFixMultipleContext: true);
                }

                public override SyntaxTree SyntaxTreeToModify => _suppressionTargetInfo.StartToken.SyntaxTree;

                protected async override Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
                {
                    return await GetChangedDocumentAsync(includeStartTokenChange: true, includeEndTokenChange: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                public async Task<Document> GetChangedDocumentAsync(bool includeStartTokenChange, bool includeEndTokenChange, CancellationToken cancellationToken)
                {
                    Func<SyntaxToken, SyntaxToken> getNewStartToken;
                    Func<SyntaxToken, SyntaxToken> getNewEndToken;

                    bool add = false;
                    bool toggle = false;

                    int indexOfLeadingPragmaDisableToRemove = -1, indexOfTrailingPragmaEnableToRemove = -1;
                    if (CanRemovePragmaTrivia(_suppressionTargetInfo.StartToken, _diagnostic, Fixer, leading: true, indexOfTriviaToRemove: out indexOfLeadingPragmaDisableToRemove) &&
                        CanRemovePragmaTrivia(_suppressionTargetInfo.EndToken, _diagnostic, Fixer, leading: false, indexOfTriviaToRemove: out indexOfTrailingPragmaEnableToRemove))
                    {
                        // Verify if there is no other trivia before the start token would again cause this diagnostic to be suppressed.
                        // If invalidated, then we just toggle existing pragma enable and disable directives before and start of the line.
                        // If not, then we just remove the existing pragma trivia surrounding the line.
                        toggle = await IsDiagnosticSuppressedBeforeLeadingPragmaAsync(indexOfLeadingPragmaDisableToRemove, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // Otherwise, just add a pragma enable before the start token and a pragma restore after it.
                        add = true;
                    }

                    getNewStartToken = startToken => includeStartTokenChange ?
                        GetNewTokenWithModifiedPragma(startToken, add, toggle, indexOfLeadingPragmaDisableToRemove, isStartToken: true) :
                        startToken;

                    getNewEndToken = endToken => includeEndTokenChange ?
                        GetNewTokenWithModifiedPragma(endToken, add, toggle, indexOfTrailingPragmaEnableToRemove, isStartToken: false) :
                        endToken;

                    return await PragmaHelpers.GetChangeDocumentWithPragmaAdjustedAsync(
                        _document,
                        _suppressionTargetInfo,
                        getNewStartToken,
                        getNewEndToken,
                        cancellationToken).ConfigureAwait(false);
                }

                private static bool CanRemovePragmaTrivia(SyntaxToken token, Diagnostic diagnostic, AbstractSuppressionCodeFixProvider fixer, bool leading, out int indexOfTriviaToRemove)
                {
                    indexOfTriviaToRemove = -1;

                    var seenAnyPragmaForId = false;
                    var triviaList = leading ?
                        token.LeadingTrivia :
                        (fixer.IsEndOfFileToken(token) ? token.LeadingTrivia : token.TrailingTrivia);

                    var diagnosticSpan = diagnostic.Location.SourceSpan;
                    Func<SyntaxTrivia, bool> shouldIncludeTrivia = t => leading ? t.FullSpan.End <= diagnosticSpan.Start : t.FullSpan.Start >= diagnosticSpan.End;
                    var filteredTriviaList = triviaList.Where(shouldIncludeTrivia);
                    if (leading)
                    {
                        // Walk bottom up for leading trivia.
                        filteredTriviaList = filteredTriviaList.Reverse();
                    }

                    foreach (var trivia in filteredTriviaList)
                    {
                        bool isEnableDirective, hasMultipleIds;
                        if (fixer.IsAnyPragmaDirectiveForId(trivia, diagnostic.Id, out isEnableDirective, out hasMultipleIds))
                        {
                            if (seenAnyPragmaForId || hasMultipleIds)
                            {
                                // Handle only simple cases where we have a single pragma directive with single ID matching ours in the trivia.
                                return false;
                            }

                            seenAnyPragmaForId = true;

                            // We want to look for leading disable directive and trailing enable directive.
                            if ((leading && !isEnableDirective) ||
                                (!leading && isEnableDirective))
                            {
                                indexOfTriviaToRemove = triviaList.IndexOf(trivia);
                                return true;
                            }
                        }
                    }

                    return false;
                }

                private SyntaxToken GetNewTokenWithModifiedPragma(SyntaxToken token, bool add, bool toggle, int indexOfTriviaToRemoveOrToggle, bool isStartToken)
                {
                    return add ?
                        GetNewTokenWithAddedPragma(token, isStartToken) :
                        GetNewTokenWithRemovedOrToggledPragma(token, indexOfTriviaToRemoveOrToggle, isStartToken, toggle);
                }

                private SyntaxToken GetNewTokenWithAddedPragma(SyntaxToken token, bool isStartToken)
                {
                    if (isStartToken)
                    {
                        return PragmaHelpers.GetNewStartTokenWithAddedPragma(token, _diagnostic, Fixer, isRemoveSuppression: true);
                    }
                    else
                    {
                        return PragmaHelpers.GetNewEndTokenWithAddedPragma(token, _diagnostic, Fixer, isRemoveSuppression: true);
                    }
                }

                private SyntaxToken GetNewTokenWithRemovedOrToggledPragma(SyntaxToken token, int indexOfTriviaToRemoveOrToggle, bool isStartToken, bool toggle)
                {
                    if (isStartToken)
                    {
                        return GetNewTokenWithPragmaUnsuppress(token, indexOfTriviaToRemoveOrToggle, _diagnostic, Fixer, isStartToken, toggle);
                    }
                    else
                    {
                        return GetNewTokenWithPragmaUnsuppress(token, indexOfTriviaToRemoveOrToggle, _diagnostic, Fixer, isStartToken, toggle);
                    }
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

                private async Task<bool> IsDiagnosticSuppressedBeforeLeadingPragmaAsync(int indexOfPragma, CancellationToken cancellationToken)
                {
                    var model = await _document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                    var tree = model.SyntaxTree;

                    // get the warning state of this diagnostic ID at the start of the pragma
                    var trivia = _suppressionTargetInfo.StartToken.LeadingTrivia.ElementAt(indexOfPragma);
                    var spanToCheck = new TextSpan(
                        start: Math.Max(0, trivia.Span.Start - 1),
                        length: 1);
                    var locationToCheck = Location.Create(tree, spanToCheck);
                    var dummyDiagnosticWithLocationToCheck = Diagnostic.Create(_diagnostic.Descriptor, locationToCheck);
                    var effectiveDiagnostic = CompilationWithAnalyzers.GetEffectiveDiagnostics(new[] { dummyDiagnosticWithLocationToCheck }, model.Compilation).FirstOrDefault();
                    return effectiveDiagnostic == null || effectiveDiagnostic.IsSuppressed;
                }

                public SyntaxToken StartToken_TestOnly => _suppressionTargetInfo.StartToken;
                public SyntaxToken EndToken_TestOnly => _suppressionTargetInfo.EndToken;

                private static void NormalizeTriviaOnTokens(AbstractSuppressionCodeFixProvider fixer, ref Document document, ref SuppressionTargetInfo suppressionTargetInfo)
                {
                    var startToken = suppressionTargetInfo.StartToken;
                    var endToken = suppressionTargetInfo.EndToken;
                    var nodeWithTokens = suppressionTargetInfo.NodeWithTokens;

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
                    document = document.WithSyntaxRoot(root);
                    suppressionTargetInfo.StartToken = root.FindToken(startToken.SpanStart);
                    suppressionTargetInfo.EndToken = root.FindToken(endToken.SpanStart);
                    suppressionTargetInfo.NodeWithTokens = fixer.GetNodeWithTokens(suppressionTargetInfo.StartToken, suppressionTargetInfo.EndToken, root);
                }
            }
        }
    }
}