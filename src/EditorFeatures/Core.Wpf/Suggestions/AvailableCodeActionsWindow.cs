// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.CodeAnalysis.Editor.Suggestions
{
    [Guid(GuidString)]
    internal class AvailableCodeActionsWindow : ToolWindowPane
    {
        public const string GuidString = "206e277d-c1a1-4d2b-880e-112b85a962df";

        /// <summary>
        /// Standard constructor for the tool window.
        /// </summary>
        public AvailableCodeActionsWindow(object context) :
            base(null)
        {
            // Set the window title reading it from the resources.
            this.Caption = EditorFeaturesWpfResources.Available_Code_Actions;

#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
            var componentModel = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
#pragma warning restore VSTHRD010 // Invoke single-threaded types on Main thread

            base.Content = new AvailableCodeActionsControl();
        }

        public void Refresh(SortedDictionary<int, List<(SyntaxNode, CodeAction)>> availableActions, Dictionary<string, int> codeActionTitleToKeyMap)
        {
            ((AvailableCodeActionsControl)this.Content).Refresh(availableActions, codeActionTitleToKeyMap);
        }
    }
}
