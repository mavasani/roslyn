using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Implementation.FindReferences;
using Microsoft.CodeAnalysis.Editor.Navigation;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Navigation;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.CobyIntegration
{
    [ExportWorkspaceService(typeof(IFindReferencesServiceInternal), "CobyWorkspace"), Shared]
    internal class CobyFindReferencesService : IFindReferencesServiceInternal
    {
        public async Task<IEnumerable<INavigableItem>> FindReferencesAsync(Document document, int position, CancellationToken cancellationToken)
        {
            var workspace = document.Project.Solution.Workspace as CobyWorkspace;
            if (workspace == null)
            { 
                return null;
            }

            var fileEntity = await CobyServices.GetFileEntityAsync(document, cancellationToken).ConfigureAwait(false);
            if (fileEntity == null)
            {
                return null;
            }

            var annotation = CobyServices.GetMatchingAnnotation(document, position, fileEntity, cancellationToken);
            if (annotation == null)
            {
                return null;
            }

            var symbolDefinitionResponseTask = CobyServices.GetContentBySymbolIdAsync(Consts.Repo, annotation.symbolId, cancellationToken);
            var symbolReferencesResponseTask = CobyServices.GetEntityReferencesAsync(Consts.Repo, annotation.symbolId, cancellationToken);

            await Task.WhenAll(symbolDefinitionResponseTask, symbolReferencesResponseTask).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            var symbolDefinitionResponse = symbolDefinitionResponseTask.Result;
            var symbolReferencesResponse = symbolReferencesResponseTask.Result;
            if (symbolDefinitionResponse == null)
            {
                return null;
            }

            var glyph = GetGlyph(annotation);

            ImmutableArray<INavigableItem> childItems;
            if (symbolReferencesResponse != null)
            {
                var itemBuilder = ImmutableArray.CreateBuilder<INavigableItem>();
                foreach (var referencesByFile in symbolReferencesResponse.GroupBy(r => r.tref))
                {
                    var key = referencesByFile.Key;
                    if (key.StartsWith(fileEntity.repository))
                    {
                        key = key.Substring(fileEntity.repository.Length + 1);
                    }

                    if (key.StartsWith(fileEntity.version))
                    {
                        key = key.Substring(fileEntity.version.Length + 1);
                    }

                    var task = CobyServices.GetContentByFilePathAsync(Consts.Repo, fileEntity.version, key, cancellationToken);
                    foreach (var reference in referencesByFile)
                    {
                        var item = FindReferencesNavigableItem.Create(reference, workspace, glyph, task, symbolDefinitionResponse.displayName, cancellationToken);
                        itemBuilder.Add(item);
                    }
                }

                childItems = itemBuilder.ToImmutable();
            }
            else
            {
                childItems = ImmutableArray<INavigableItem>.Empty;
            }

            var definitionItem = FindReferencesNavigableItem.Create(symbolDefinitionResponse, workspace, glyph, childItems, document.Project.Name, cancellationToken);
            return SpecializedCollections.SingletonEnumerable(definitionItem);
        }

        private static Glyph GetGlyph(CobyServices.Annotation annotation)
        {
            // REVIEW: we dont have enough informatino to show right glyph. ex) private, internal, extension method and etc
            switch (annotation.symbolType.ToLower())
            {
                case "namespace":
                    return Glyph.Namespace;

                case "namedtype":
                    return Glyph.ClassPublic;

                case "method":
                    return Glyph.MethodPublic;

                case "property":
                    return Glyph.PropertyPublic;

                case "field":
                    return Glyph.FieldPublic;

                case "event":
                    return Glyph.EventPublic;

                case "parameter":
                    return Glyph.Parameter;

                case "typeparameter":
                    return Glyph.TypeParameter;

                case "local":
                    return Glyph.Local;

                default:
                    // Is this possible?
                    return Glyph.ClassPublic;
            }
        }

        private abstract class FindReferencesNavigableItem : INavigableItem
        {
            protected readonly CobyWorkspace _workspace;
            private readonly Glyph _glyph;
            private TextSpan? _lazySourceSpan;

            protected FindReferencesNavigableItem(CobyWorkspace workspace, Glyph glyph)
            {
                _workspace = workspace;
                _glyph = glyph;
            }

            public static INavigableItem Create(CobyServices.SymbolReference referenceResponse, CobyWorkspace workspace, Glyph glyph, Task<CobyServices.SourceResponse> fileResponseTask, string defaultDisplayString, CancellationToken cancellationToken)
            {
                return new ReferenceNavigableItem(referenceResponse, workspace, glyph, fileResponseTask, defaultDisplayString, cancellationToken);
            }

            public static INavigableItem Create(CobyServices.SourceResponse definitionResponse, CobyWorkspace workspace, Glyph glyph, ImmutableArray<INavigableItem> childItems, string projectName, CancellationToken cancellationToken)
            {
                return new DefinitionNavigableItem(definitionResponse, workspace, glyph, childItems, projectName, cancellationToken);
            }

            public abstract Document Document { get; }
            public abstract ImmutableArray<INavigableItem> ChildItems { get; }
            internal abstract TextSpan ComputeSourceSpan();
            internal abstract int StartLine { get; }

            public virtual bool DisplayFileLocation => true;
            
            public virtual string DisplayString
            {
                get
                {
                    var text = Document.GetTextAsync(CancellationToken.None).WaitAndGetResult(CancellationToken.None);
                    var startLine = StartLine - 1;
                    if (startLine >= text.Lines.Count || startLine < 0)
                    {
                        startLine = 0;
                    }

                    return text.Lines[startLine].ToString().TrimStart(' ').TrimEnd(' ');
                }
            }

            public Glyph Glyph => _glyph;

            private void EnsureSourceReponseAndSpan()
            {
                if (!_lazySourceSpan.HasValue)
                {
                    _lazySourceSpan = ComputeSourceSpan();
                }
            }

            public TextSpan SourceSpan
            {
                get
                {
                    EnsureSourceReponseAndSpan();
                    return _lazySourceSpan.Value;
                }
            }

            private class ReferenceNavigableItem : FindReferencesNavigableItem
            {
                private readonly CobyServices.SymbolReference _referenceResponse;
                private readonly Task<CobyServices.SourceResponse> _fileResponseTask;
                private readonly CancellationToken _cancellationToken;

                public ReferenceNavigableItem(CobyServices.SymbolReference referenceResponse, CobyWorkspace workspace, Glyph glyph, Task<CobyServices.SourceResponse> fileResponseTask, string defaultDisplayString, CancellationToken cancellationToken)
                    : base(workspace, glyph)
                {
                    _referenceResponse = referenceResponse;
                    _fileResponseTask = fileResponseTask;
                    _cancellationToken = cancellationToken;
                }

                // dynamically add document to the workspace
                public override Document Document => _workspace.GetOrCreateDocument(_fileResponseTask.WaitAndGetResult(_cancellationToken));

                public override bool DisplayFileLocation => false;

                public override string DisplayString
                {
                    get
                    {
                        return $"[GitHub] {_workspace.GetDisplayPath(Document)} - ({StartLine}, {StartColumn}) : {base.DisplayString}";
                    }
                }

                public override ImmutableArray<INavigableItem> ChildItems => ImmutableArray<INavigableItem>.Empty;

                internal override TextSpan ComputeSourceSpan()
                {
                    return CobyServices.GetSourceSpan(_referenceResponse, () => Document);
                }

                internal override int StartLine => Math.Max(0, _referenceResponse.trange.startLineNumber);
                internal int StartColumn => Math.Max(0, _referenceResponse.trange.startColumn);
            }

            private class DefinitionNavigableItem : FindReferencesNavigableItem
            {
                private readonly CobyServices.SourceResponse _definitionResponse;
                private readonly ImmutableArray<INavigableItem> _childItems;
                private readonly string _projectName;

                public DefinitionNavigableItem(CobyServices.SourceResponse definitionResponse, CobyWorkspace workspace, Glyph glyph, ImmutableArray<INavigableItem> childItems, string projectName, CancellationToken cancellationToken)
                    : base(workspace, glyph)
                {
                    _definitionResponse = definitionResponse;
                    _childItems = childItems;
                    _projectName = projectName;
                }

                // dynamically add document to the workspace
                public override Document Document => _workspace.GetOrCreateDocument(_definitionResponse);

                public override bool DisplayFileLocation => false;

                public override string DisplayString
                {
                    get
                    {
                        var referenceCountDisplay = _childItems.Length == 1
                            ? ServicesVSResources.ReferenceCountSingular
                            : string.Format(ServicesVSResources.ReferenceCountPlural, _childItems.Length);

                        return $"[{_projectName}] {base.DisplayString.TrimStart(' ').TrimEnd(' ')} ({referenceCountDisplay})";
                    }
                }

                public override ImmutableArray<INavigableItem> ChildItems => _childItems;

                internal override TextSpan ComputeSourceSpan()
                {
                    return CobyServices.GetSourceSpan(_definitionResponse, () => Document);
                }

                internal override int StartLine => Math.Max(0, _definitionResponse.range.startLineNumber);
            }
        }
    }
}
