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

            var url = CobyWorkspace.GetCompoundUrl(document);
            var fileResult = CobyServices.GetFileEntityAsync(Consts.CodeBase, url.fileUid, CancellationToken.None).WaitAndGetResult(cancellationToken);
            if (fileResult == null)
            {
                return false;
            }

            var text = document.GetTextAsync(cancellationToken).WaitAndGetResult(cancellationToken);

            var zeroBasedPosition = text.Lines.GetLinePosition(position);
            var oneBasedPosition = new LinePosition(zeroBasedPosition.Line + 1, zeroBasedPosition.Character + 1);

            var annotation = fileResult.referenceAnnotation.FirstOrDefault(a => new LinePosition(a.range.startLineNumber, a.range.startColumn) <= oneBasedPosition && oneBasedPosition < new LinePosition(a.range.endLineNumber, a.range.endColumn));
            if (annotation == null)
            {
                return false;
            }

            var symbolResult = CobyServices.GetSymbolEntityAsync(Consts.CodeBase, annotation.symbolId, cancellationToken).WaitAndGetResult(cancellationToken);
            var targetDocument = workspace.GetOrCreateDocument(new CobyServices.CompoundUrl() { fileUid = symbolResult.fileUid }, symbolResult.name);

            var targetText = targetDocument.GetTextAsync(cancellationToken).WaitAndGetResult(cancellationToken);
            var targetSpan = targetText.Lines.GetTextSpan(
                                    new LinePositionSpan(
                                        new LinePosition(Math.Max(symbolResult.lineStart - 1, 0), Math.Max(symbolResult.columnStart - 1, 0)),
                                        new LinePosition(Math.Max(symbolResult.lineEnd - 1, 0), Math.Max(symbolResult.columnEnd - 1, 0))));

            var navigationService = workspace.Services.GetService<IDocumentNavigationService>();

            return navigationService.TryNavigateToSpan(
                        workspace,
                        targetDocument.Id,
                        targetSpan,
                        options: workspace.Options.WithChangedOption(NavigationOptions.PreferProvisionalTab, true));
        }
    }
}
