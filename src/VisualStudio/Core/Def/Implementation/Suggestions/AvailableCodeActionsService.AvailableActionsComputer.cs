// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.AvailableCodeActions;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Suggestions
{
    internal sealed partial class AvailableCodeActionsService : IAvailableCodeActionsService, IWorkspaceServiceFactory
    {
        private sealed class AvailableActionsComputer : SyntaxWalker
        {
            private readonly Document _document;
            private readonly ICodeRefactoringService _codeRefactoringService;
            private readonly CancellationToken _cancellationToken;

            private readonly HashSet<SyntaxNode> _visitedNodes;
            private readonly List<CodeFixCollection> _pendingCodeFixCollections;
            private readonly Dictionary<string, int> _titleToKeyMap;
            private readonly SortedDictionary<int, List<(TextSpan span, CodeAction action)>> _actionsBuilder;

            private AvailableActionsComputer(
                Document document,
                ICodeRefactoringService codeRefactoringService,
                ImmutableArray<CodeFixCollection> codeFixCollectionsForDocument,
                CancellationToken cancellationToken)
            {
                _document = document;
                _codeRefactoringService = codeRefactoringService;
                _pendingCodeFixCollections = codeFixCollectionsForDocument.Where(fixCollection => fixCollection.FirstDiagnostic?.Location?.IsInSource == true).ToList();
                _cancellationToken = cancellationToken;

                _visitedNodes = new HashSet<SyntaxNode>();
                _titleToKeyMap = new Dictionary<string, int>();
                _actionsBuilder = new SortedDictionary<int, List<(TextSpan span, CodeAction action)>>();
            }

            public static (SortedDictionary<int, List<(TextSpan span, CodeAction action)>> availableActions, Dictionary<string, int> codeActionTitleToKeyMap) Compute(
                SyntaxNode nodeForCaretOrSelection,
                SyntaxNode root,
                Document document,
                ICodeFixService codeFixService,
                ICodeRefactoringService codeRefactoringService,
                CancellationToken cancellationToken)
            {
                var codeFixCollections = codeFixService.GetFixesAsync(document, root.FullSpan, includeSuppressionFixes: false, cancellationToken).WaitAndGetResult_CanCallOnBackground(cancellationToken);
                var computer = new AvailableActionsComputer(document, codeRefactoringService, codeFixCollections, cancellationToken);

                var currentNode = nodeForCaretOrSelection;
                while (currentNode != null)
                {
                    computer.Visit(currentNode);
                    currentNode = currentNode.Parent;
                }

                computer.Visit(root);
                return (computer._actionsBuilder, computer._titleToKeyMap);
            }

            public override void Visit(SyntaxNode node)
            {
                if (_visitedNodes.Contains(node))
                {
                    return;
                }

                AddCodeFixes(node);
                AddRefactorings(node);

                _visitedNodes.Add(node);

                base.Visit(node);
            }

            private void AddCodeFixes(SyntaxNode node)
            {
                var applicableFixCollections = _pendingCodeFixCollections.Where(fixCollection => node.Span.Contains(fixCollection.TextSpan)).ToImmutableArrayOrEmpty();
                foreach (var fixCollection in applicableFixCollections)
                {
                    var codeFixActions = fixCollection.Fixes.Select(fix => fix.Action);
                    AddCodeActions(codeFixActions, fixCollection.TextSpan);
                    _pendingCodeFixCollections.Remove(fixCollection);
                }
            }

            private void AddRefactorings(SyntaxNode node)
            {
                var refactoringsForNode = _codeRefactoringService.GetRefactoringsAsync(_document, node, useSpanStart: false, _cancellationToken).WaitAndGetResult_CanCallOnBackground(_cancellationToken);
                var refactoringActions = refactoringsForNode.SelectMany(r => r.Actions);
                AddCodeActions(refactoringActions, node.Span);
                var addedRefactoringTitles = refactoringActions.Select(a => a.Title).ToImmutableHashSet();

                var refactoringsForPosition = _codeRefactoringService.GetRefactoringsAsync(_document, node, useSpanStart: true, _cancellationToken).WaitAndGetResult_CanCallOnBackground(_cancellationToken);
                refactoringActions = refactoringsForPosition.SelectMany(r => r.Actions).Where(a => !addedRefactoringTitles.Contains(a.Title));
                AddCodeActions(refactoringActions, new TextSpan(node.SpanStart, 0));
            }

            private void AddCodeActions(IEnumerable<CodeAction> actions, TextSpan span)
            {
                foreach (var action in actions)
                {
                    var title = action.Title;
                    if (!_titleToKeyMap.TryGetValue(title, out var key))
                    {
                        key = _titleToKeyMap.Count + 1;
                        _titleToKeyMap.Add(title, key);
                    }

                    if (!_actionsBuilder.TryGetValue(key, out var list))
                    {
                        list = new List<(TextSpan span, CodeAction action)>();
                        _actionsBuilder.Add(key, list);
                    }

                    list.Add((span, action));
                }
            }
        }
    }
}
