﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.CodeAnalysis.Editor
{
    internal interface INavigationBarPresenter
    {
        void Disconnect();

        void PresentItems(
            ImmutableArray<NavigationBarProjectItem> projects,
            NavigationBarProjectItem? selectedProject,
            ImmutableArray<NavigationBarItem> typesWithMembers,
            NavigationBarItem? selectedType,
            NavigationBarItem? selectedMember);

        ITextView TryGetCurrentView();

        event EventHandler<EventArgs> ViewFocused;
        event EventHandler<CaretPositionChangedEventArgs> CaretMoved;

        event EventHandler DropDownFocused;
        event EventHandler<NavigationBarItemSelectedEventArgs> ItemSelected;
    }
}
