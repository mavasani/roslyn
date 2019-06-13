// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.AvailableCodeActions;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Elfie.Serialization;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Navigation;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.LanguageServices.Implementation.Suggestions;
using Microsoft.VisualStudio.LanguageServices.Setup;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Suggestions
{
    [ExportWorkspaceServiceFactory(typeof(IAvailableCodeActionsService), ServiceLayer.Host), Shared]
    internal sealed partial class AvailableCodeActionsService : IAvailableCodeActionsService, IWorkspaceServiceFactory
    {
        private readonly VisualStudioWorkspace _workspace;
        private readonly IThreadingContext _threadingContext;
        private readonly IWaitIndicator _waitIndicator;
        private readonly IDocumentTrackingService _documentTrackingService;
        private readonly IDocumentNavigationService _documentNavigationService;
        private readonly ICodeFixService _codeFixService;
        private readonly ICodeRefactoringService _codeRefactoringService;
        private AvailableCodeActionsWindow _availableCodeActionsWindow;

        [ImportingConstructor]
        public AvailableCodeActionsService(
            VisualStudioWorkspace workspace,
            IThreadingContext threadingContext,
            IWaitIndicator waitIndicator,
            ICodeFixService codeFixService,
            ICodeRefactoringService codeRefactoringService)
        {
            _workspace = workspace;
            _threadingContext = threadingContext;
            _waitIndicator = waitIndicator;
            _codeFixService = codeFixService;
            _codeRefactoringService = codeRefactoringService;

            _documentTrackingService = workspace.Services.GetService<IDocumentTrackingService>();
            _documentNavigationService = workspace.Services.GetService<IDocumentNavigationService>();
        }

        public IWorkspaceService CreateService(HostWorkspaceServices workspaceServices)
        {
            return this;
        }

        internal void SetAvailableCodeActionsWindow(AvailableCodeActionsWindow availableCodeActionsWindow)
        {
            availableCodeActionsWindow.Initialize(this);
            _availableCodeActionsWindow = availableCodeActionsWindow;
        }

        public bool CanShowAvailableCodeActionsWindow => _availableCodeActionsWindow != null;

        public bool IsShowingAvailableCodeActionsWindow => _availableCodeActionsWindow?.IsVisible() == true;

        public async Task UpdateAvailableCodeActionsForActiveDocumentAsync(CancellationToken cancellationToken)
        {
            var activeDocument = _documentTrackingService.GetActiveDocument(_workspace.CurrentSolution);
            if (activeDocument?.SupportsSyntaxTree == true)
            {
                var root = await activeDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                await UpdateAvailableCodeActionsAsync(activeDocument, root, root, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task UpdateAvailableCodeActionsAsync(
            Document document,
            TextSpan span,
            CancellationToken cancellationToken)
        {
            if (!document.SupportsSyntaxTree)
            {
                return;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var nodeForCaretOrSelection = root.FindNode(span, findInsideTrivia: true, getInnermostNodeForTie: true) ?? root;
            await UpdateAvailableCodeActionsAsync(document, nodeForCaretOrSelection, root, cancellationToken).ConfigureAwait(false);
        }

        private async Task UpdateAvailableCodeActionsAsync(
            Document document,
            SyntaxNode nodeForCaretOrSelection,
            SyntaxNode root,
            CancellationToken cancellationToken)
        {
            Debug.Assert(document.SupportsSyntaxTree);

            if (_availableCodeActionsWindow == null)
            {
                return;
            }

            _availableCodeActionsWindow.Show();

            SortedDictionary<int, List<(TextSpan span, CodeAction action)>> availableActions = null;
            Dictionary<string, int> codeActionTitleToKeyMap = null;
            var result = _waitIndicator.Wait(
                $"Available actions for '{document.Name}'",
                "Computing actions...",
                allowCancel: true,
                showProgress: true,
                context => (availableActions, codeActionTitleToKeyMap) = AvailableActionsComputer.Compute(nodeForCaretOrSelection, root, document, _codeFixService, _codeRefactoringService, cancellationToken));

            if (result == WaitIndicatorResult.Completed)
            {
                await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                _availableCodeActionsWindow.Refresh(root, availableActions, codeActionTitleToKeyMap, onGotFocus: span => OnFocus(document, span, cancellationToken));
            }
        }

        private void OnFocus(
            Document document,
            TextSpan span,
            CancellationToken cancellationToken)
        {
            var root = document.GetSyntaxRootSynchronously(cancellationToken);
            if (root.FullSpan.Contains(span))
            {
                _documentNavigationService?.TryNavigateToSpan(_workspace, document.Id, span);
            }
        }
    }
}
