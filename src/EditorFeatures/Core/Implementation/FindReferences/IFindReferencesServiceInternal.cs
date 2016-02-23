using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Navigation;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Host;

namespace Microsoft.CodeAnalysis.Editor.Implementation.FindReferences
{
    // hack to bypass layering issue.
    internal interface IFindReferencesServiceInternal : IWorkspaceService
    {
        Task<IEnumerable<INavigableItem>> FindReferencesAsync(Document document, int position, CancellationToken cancellationToken);
    }
}
