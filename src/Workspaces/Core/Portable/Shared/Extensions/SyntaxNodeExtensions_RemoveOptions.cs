// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.LanguageServices;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Shared.Extensions
{
    internal static partial class SyntaxNodeExtensions
    {
        public static SyntaxNode RemoveNodeRetainingComments(this SyntaxNode root, SyntaxNode node, ISyntaxFactsService syntaxFacts)
        {
            var removeOptions = node.GetSyntaxRemoveOptionsRetainingComments(syntaxFacts);
            var newNode = node.WithElasticWhitespaceTrivia(syntaxFacts);
            root = root.ReplaceNode(node, newNode);
            return root.RemoveNode(newNode, removeOptions);
        }

        public static SyntaxNode WithElasticWhitespaceTrivia(
            this SyntaxNode node, ISyntaxFactsService syntaxFacts)
        {
            var leadingTrivia = node.GetLeadingTrivia().Select(MakeElasticIfRequired);
            var trailingTrivia = node.GetTrailingTrivia().Select(MakeElasticIfRequired);
            return node.WithLeadingTrivia(leadingTrivia).WithTrailingTrivia(trailingTrivia);

            SyntaxTrivia MakeElasticIfRequired(SyntaxTrivia trivia)
                => syntaxFacts.IsWhitespaceTrivia(trivia) ? syntaxFacts.ElasticSpace : trivia;
        }

        public static SyntaxRemoveOptions GetSyntaxRemoveOptionsRetainingComments(
            this SyntaxNode node, ISyntaxFactsService syntaxFacts)
        {
            var removeOptions = SyntaxRemoveOptions.KeepUnbalancedDirectives;

            var trailingTrivia = node.GetTrailingTrivia();
            var hasMultipleTrailingEndOfLines = HasMultipleEndOfLines(
                list1: trailingTrivia,
                list2: node.GetLastToken().GetNextToken().LeadingTrivia,
                syntaxFacts);

            if (hasMultipleTrailingEndOfLines || HasRegularComments(trailingTrivia, syntaxFacts))
            {
                removeOptions |= SyntaxRemoveOptions.KeepTrailingTrivia;
            }

            var leadingTrivia = node.GetLeadingTrivia();
            if (HasRegularComments(leadingTrivia, syntaxFacts))
            {
                removeOptions |= SyntaxRemoveOptions.KeepLeadingTrivia;
            }
            else if (!hasMultipleTrailingEndOfLines)
            {
                var hasMultipleLeadingEndOfLines = HasMultipleEndOfLines(
                    list1: leadingTrivia,
                    list2: node.GetFirstToken().GetPreviousToken().TrailingTrivia,
                    syntaxFacts);
                if (hasMultipleLeadingEndOfLines)
                {
                    removeOptions |= SyntaxRemoveOptions.KeepLeadingTrivia;
                }
            }

            return removeOptions;
        }

        private static bool HasMultipleEndOfLines(SyntaxTriviaList list1, SyntaxTriviaList list2, ISyntaxFactsService syntaxFacts)
            => list1.AddRange(list2).Count(syntaxFacts.IsEndOfLineTrivia) > 1;

        private static bool HasRegularComments(SyntaxTriviaList list, ISyntaxFactsService syntaxFacts)
            => list.Any(syntaxFacts.IsRegularComment);
    }
}
