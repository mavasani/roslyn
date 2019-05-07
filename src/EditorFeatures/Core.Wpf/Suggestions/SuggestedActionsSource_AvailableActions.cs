// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Editor.Shared;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Editor.Suggestions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell.Interop;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Suggestions
{
    internal partial class SuggestedActionsSourceProvider
    {
        private sealed partial class SuggestedActionsSource : ForegroundThreadAffinitizedObject, ISuggestedActionsSource2
        {
            private void UpdateAvailableActions(
                ITextBufferSupportsFeatureService supportsFeatureService,
                ISuggestedActionCategorySet requestedActionCategories,
                Workspace workspace,
                Document document,
                TextSpan? selectionOpt,
                CancellationToken cancellationToken)
            {
                Task.Run(() => UpdateAvailableActionsAsync(supportsFeatureService, requestedActionCategories,
                    workspace, document, selectionOpt, cancellationToken));
            }

            private async Task UpdateAvailableActionsAsync(
                ITextBufferSupportsFeatureService supportsFeatureService,
                ISuggestedActionCategorySet requestedActionCategories,
                Workspace workspace,
                Document document,
                TextSpan? selectionOpt,
                CancellationToken cancellationToken)
            {
                if (!SupportsRefactoring(supportsFeatureService, requestedActionCategories, workspace))
                {
                    return;
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (!selectionOpt.HasValue)
                {
                    var caretPointOpt = _textView.GetCaretPoint(_subjectBuffer);
                    if (!caretPointOpt.HasValue)
                    {
                        return;
                    }

                    selectionOpt = new TextSpan(caretPointOpt.Value.Position, caretPointOpt.Value.Position);
                }

                var node = root.FindNode(selectionOpt.Value, findInsideTrivia: true, getInnermostNodeForTie: true);
                if (node == null)
                {
                    return;
                }

                var (availableActions, codeActionTitleToKeyMap) = AvailableActionsComputer.Compute(node, root, document, _owner._codeRefactoringService, cancellationToken);

                await ThreadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                var toolWindowGuid = new Guid(AvailableCodeActionsWindow.GuidString);
                int hr = _uiShell.FindToolWindowEx((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref toolWindowGuid, 0, out var window);
                if (hr == VSConstants.S_OK && window is AvailableCodeActionsWindow availableCodeActionsWindow)
                {
                    availableCodeActionsWindow.Refresh(availableActions, codeActionTitleToKeyMap);
                }
            }

            private sealed class AvailableActionsComputer : SyntaxWalker
            {
                private readonly Document _document;
                private readonly ICodeRefactoringService _codeRefactoringService;
                private readonly CancellationToken _cancellationToken;

                private readonly HashSet<SyntaxNode> _visitedNodes;
                private readonly Dictionary<string, int> _titleToKeyMap;
                private readonly SortedDictionary<int, List<(SyntaxNode, CodeAction)>> _actionsBuilder;

                private AvailableActionsComputer(
                    Document document,
                    ICodeRefactoringService codeRefactoringService,
                    CancellationToken cancellationToken)
                {
                    _document = document;
                    _codeRefactoringService = codeRefactoringService;
                    _cancellationToken = cancellationToken;

                    _visitedNodes = new HashSet<SyntaxNode>();
                    _titleToKeyMap = new Dictionary<string, int>();
                    _actionsBuilder = new SortedDictionary<int, List<(SyntaxNode, CodeAction)>>();
                }

                public static (SortedDictionary<int, List<(SyntaxNode, CodeAction)>> availableActions, Dictionary<string, int> codeActionTitleToKeyMap) Compute(
                    SyntaxNode node,
                    SyntaxNode root,
                    Document document,
                    ICodeRefactoringService codeRefactoringService,
                    CancellationToken cancellationToken)
                {
                    var computer = new AvailableActionsComputer(document, codeRefactoringService, cancellationToken);

                    var currentNode = node;
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

                    AddRefactorings(node);
                    _visitedNodes.Add(node);

                    base.Visit(node);
                }

                private void AddRefactorings(SyntaxNode node)
                {
                    var refactorings = _codeRefactoringService.GetRefactoringsAsync(_document, node, _cancellationToken).WaitAndGetResult_CanCallOnBackground(_cancellationToken);
                    foreach (var refactoring in refactorings)
                    {
                        foreach (var action in refactoring.Actions)
                        {
                            var title = action.Title;
                            if (!_titleToKeyMap.TryGetValue(title, out var key))
                            {
                                key = _titleToKeyMap.Count + 1;
                                _titleToKeyMap.Add(title, key);
                            }

                            if (!_actionsBuilder.TryGetValue(key, out var list))
                            {
                                list = new List<(SyntaxNode, CodeAction)>();
                                _actionsBuilder.Add(key, list);
                            }

                            list.Add((node, action));
                        }
                    }
                }
            }
        }
    }
}
