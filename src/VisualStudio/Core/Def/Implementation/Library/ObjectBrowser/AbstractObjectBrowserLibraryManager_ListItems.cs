// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Library.ObjectBrowser
{
    internal abstract partial class AbstractObjectBrowserLibraryManager
    {
        internal void CollectMemberListItems(IAssemblySymbol assemblySymbol, Compilation compilation, ProjectId projectId, ImmutableArray<ObjectListItem>.Builder builder, string searchString)
            => GetListItemFactory().CollectMemberListItems(assemblySymbol, compilation, projectId, builder, searchString);

        internal void CollectNamespaceListItems(IAssemblySymbol assemblySymbol, ProjectId projectId, ImmutableArray<ObjectListItem>.Builder builder, string searchString)
            => GetListItemFactory().CollectNamespaceListItems(assemblySymbol, projectId, builder, searchString);

        internal void CollectTypeListItems(IAssemblySymbol assemblySymbol, Compilation compilation, ProjectId projectId, ImmutableArray<ObjectListItem>.Builder builder, string searchString)
            => GetListItemFactory().CollectTypeListItems(assemblySymbol, compilation, projectId, builder, searchString);

        internal static ImmutableHashSet<Tuple<ProjectId, IAssemblySymbol>> GetAssemblySet(Solution solution, string languageName, CancellationToken cancellationToken)
            => AbstractListItemFactory.GetAssemblySet(solution, languageName, cancellationToken);

        internal static ImmutableHashSet<Tuple<ProjectId, IAssemblySymbol>> GetAssemblySet(Project project, bool lookInReferences, CancellationToken cancellationToken)
            => AbstractListItemFactory.GetAssemblySet(project, lookInReferences, cancellationToken);

        internal ImmutableArray<ObjectListItem> GetBaseTypeListItems(ObjectListItem parentListItem, Compilation compilation)
            => GetListItemFactory().GetBaseTypeListItems(parentListItem, compilation);

        internal static ImmutableArray<ObjectListItem> GetFolderListItems(ObjectListItem parentListItem, Compilation compilation)
            => AbstractListItemFactory.GetFolderListItems(parentListItem, compilation);

        internal ImmutableArray<ObjectListItem> GetMemberListItems(ObjectListItem parentListItem, Compilation compilation)
            => GetListItemFactory().GetMemberListItems(parentListItem, compilation);

        internal ImmutableArray<ObjectListItem> GetNamespaceListItems(ObjectListItem parentListItem, Compilation compilation)
            => GetListItemFactory().GetNamespaceListItems(parentListItem, compilation);

        internal static ImmutableArray<ObjectListItem> GetProjectListItems(Solution solution, string languageName, uint listFlags)
            => AbstractListItemFactory.GetProjectListItems(solution, languageName, listFlags);

        internal static ImmutableArray<ObjectListItem> GetReferenceListItems(ObjectListItem parentListItem, Compilation compilation)
            => AbstractListItemFactory.GetReferenceListItems(parentListItem, compilation);

        internal ImmutableArray<ObjectListItem> GetTypeListItems(ObjectListItem parentListItem, Compilation compilation)
            => GetListItemFactory().GetTypeListItems(parentListItem, compilation);
    }
}
