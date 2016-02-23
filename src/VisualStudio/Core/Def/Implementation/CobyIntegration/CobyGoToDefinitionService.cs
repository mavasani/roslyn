using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Implementation.GoToDefinition;
using Microsoft.CodeAnalysis.Host.Mef;
using System.Composition;
using Microsoft.CodeAnalysis;
using System.Threading;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Navigation;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.CobyIntegration
{
    [ExportWorkspaceService(typeof(IGoToDefinitionServiceInternal), "CobyWorkspace"), Shared]
    internal class CobyGoToDefinitionService : IGoToDefinitionServiceInternal
    {
        public bool TryGoToDefinition(Document document, int position, CancellationToken cancellationToken)
        {
            var workspace = document.Project.Solution.Workspace as CobyWorkspace;
            if (workspace == null)
            {
                return false;
            }

            var annotation = CobyServices.GetMatchingAnnotation(document, position, cancellationToken);
            if (annotation == null)
            {
                return false;
            }

            var sourceResponse = CobyServices.GetContentBySymbolIdAsync(Consts.Repo, annotation.symbolId, cancellationToken).WaitAndGetResult(cancellationToken);
            if (sourceResponse == null)
            {
                return false;
            }

            var targetDocument = workspace.GetOrCreateDocument(sourceResponse);

            var targetText = targetDocument.GetTextAsync(cancellationToken).WaitAndGetResult(cancellationToken);
            var targetSpan = targetText.Lines.GetTextSpan(
                                    new LinePositionSpan(
                                        new LinePosition(Math.Max(sourceResponse.range.startLineNumber - 1, 0), Math.Max(sourceResponse.range.startColumn - 1, 0)),
                                        new LinePosition(Math.Max(sourceResponse.range.endLineNumber - 1, 0), Math.Max(sourceResponse.range.endColumn - 1, 0))));

            var navigationService = workspace.Services.GetService<IDocumentNavigationService>();

            return navigationService.TryNavigateToSpan(
                        workspace,
                        targetDocument.Id,
                        targetSpan,
                        options: workspace.Options.WithChangedOption(NavigationOptions.PreferProvisionalTab, true));
        }
    }
}
