using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;

namespace Microsoft.CodeAnalysis.Editor.Implementation.GoToDefinition
{
    // hack to bypass layering issue.
    internal interface IGoToDefinitionServiceInternal : IWorkspaceService
    {
        bool TryGoToDefinition(Document document, int position, CancellationToken cancellationToken);
    }
}
