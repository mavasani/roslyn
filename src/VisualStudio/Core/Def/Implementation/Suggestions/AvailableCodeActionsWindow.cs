// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.AvailableCodeActions;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Suggestions
{
    [Guid(GuidString)]
    internal class AvailableCodeActionsWindow : ToolWindowPane
    {
        public const string GuidString = "206e277d-c1a1-4d2b-880e-112b85a962df";

        /// <summary>
        /// Standard constructor for the tool window.
        /// </summary>
        public AvailableCodeActionsWindow(object context)
        {
            // Set the window title reading it from the resources.
            this.Caption = ServicesVSResources.Available_Code_Actions;

#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
            _ = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
#pragma warning restore VSTHRD010 // Invoke single-threaded types on Main thread

            base.Content = new AvailableCodeActionsControl();
        }

        public bool IsVisible() => (this.Frame as IVsWindowFrame)?.IsVisible() == VSConstants.S_OK;

        public void Show() => (this.Frame as IVsWindowFrame)?.Show();

        public void Initialize(IAvailableCodeActionsService availableCodeActionsService)
            => ((AvailableCodeActionsControl)base.Content).Initialize(availableCodeActionsService);

        public void Refresh(SyntaxNode root, SortedDictionary<int, List<(TextSpan span, CodeAction action)>> availableActions, Dictionary<string, int> codeActionTitleToKeyMap, Action<TextSpan> onGotFocus)
        {
            ((AvailableCodeActionsControl)this.Content).Refresh(root, availableActions, codeActionTitleToKeyMap, onGotFocus);
        }
    }
}
