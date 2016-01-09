// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Implementation.NavigateTo;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.NavigateTo.Interfaces;
using Roslyn.Utilities;
using System.Linq;
using Microsoft.CodeAnalysis.Editor.Navigation;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.CobyIntegration
{
    [Export(typeof(INavigateToItemProviderFactory))]
    [Shared]
    internal class CobyNavigateToItemProviderFactory : INavigateToItemProviderFactory
    {
        private readonly IGlyphService _glyphService;
        private readonly IAsynchronousOperationListener _asyncListener;

        [ImportingConstructor]
        public CobyNavigateToItemProviderFactory(
            IGlyphService glyphService,
            [ImportMany] IEnumerable<Lazy<IAsynchronousOperationListener, FeatureMetadata>> asyncListeners)
        {
            if (glyphService == null)
            {
                throw new ArgumentNullException(nameof(glyphService));
            }

            if (asyncListeners == null)
            {
                throw new ArgumentNullException(nameof(asyncListeners));
            }

            _glyphService = glyphService;
            _asyncListener = new AggregateAsynchronousOperationListener(asyncListeners, FeatureAttribute.CobyNavigateTo);
        }

        public bool TryCreateNavigateToItemProvider(IServiceProvider serviceProvider, out INavigateToItemProvider provider)
        {
            if (PrimaryWorkspace.Workspace == null)
            {
                // when Roslyn is not loaded, workspace is null, and so we don't want to 
                // participate in this Navigate To session. See bug 756800
                provider = null;
                return false;
            }

            provider = new CobyNavigateToItemProvider(CobyWorkspace.Instance, _glyphService, _asyncListener);
            return true;
        }

        // REVIEW: since Roslyn is built onto of Workspace model and Coby is flattened model I can't use Roslyn's navigate to as it is.
        //         removed anything related to solution from roslyn implementation.
        private partial class CobyNavigateToItemProvider : NavigateToItemProvider
        {
            public CobyNavigateToItemProvider(Workspace workspace, IGlyphService glyphService, IAsynchronousOperationListener asyncListener) :
                base(workspace, glyphService, asyncListener)
            {
            }

            public override void StartSearch(INavigateToCallback callback, string searchValue)
            {
                this.StopSearch();

                if (string.IsNullOrWhiteSpace(searchValue))
                {
                    callback.Done();
                    return;
                }

                var searcher = new Searcher(
                    (CobyWorkspace)_workspace,
                    _asyncListener,
                    _displayFactory,
                    callback,
                    searchValue,
                    _cancellationTokenSource.Token);

                searcher.Search();
            }

            private class Searcher
            {
                private readonly CobyWorkspace _workspace;
                private readonly ItemDisplayFactory _displayFactory;
                private readonly INavigateToCallback _callback;
                private readonly string _searchPattern;
                private readonly ProgressTracker _progress;
                private readonly IAsynchronousOperationListener _asyncListener;
                private readonly CancellationToken _cancellationToken;

                public Searcher(
                    CobyWorkspace workspace,
                    IAsynchronousOperationListener asyncListener,
                    ItemDisplayFactory displayFactory,
                    INavigateToCallback callback,
                    string searchPattern,
                    CancellationToken cancellationToken)
                {
                    _workspace = workspace;
                    _displayFactory = displayFactory;
                    _callback = callback;
                    _searchPattern = searchPattern;
                    _cancellationToken = cancellationToken;
                    _progress = new ProgressTracker(callback.ReportProgress);
                    _asyncListener = asyncListener;
                }

                internal void Search()
                {
                    var navigateToSearch = Logger.LogBlock(FunctionId.CobyNavigateTo_Search, _cancellationToken);
                    var asyncToken = _asyncListener.BeginAsyncOperation(GetType() + ".Search");

                    // 1 for file, 1 for symbol.
                    _progress.AddItems(2);

                    // make sure we run actual search from other thread. and let this thread return to caller as soon as possible.
                    var dummy = Task.Run(() => Search(navigateToSearch, asyncToken), _cancellationToken);
                }

                private void Search(IDisposable navigateToSearch, IAsyncToken asyncToken)
                {
                    // REVIEW: searching semantic between roslyn and coby are different.
                    var fileSearchTask = CobyServices.SearchAsync(Consts.CodeBase, CobyServices.EntityTypes.File, _searchPattern, _cancellationToken).SafeContinueWith(p =>
                    {
                        // no result
                        if (p.Result == null)
                        {
                            return;
                        }

                        var patternMatcher = new PatternMatcher(_searchPattern);
                        foreach (var result in p.Result)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            if (result.url?.StartsWith("$MAS") == true)
                            {
                                // don't put MAS result
                                continue;
                            }

                            var matches = patternMatcher.GetMatches(result.name, result.url);
                            if (matches != null)
                            {
                                ReportMatchResult(result, matches);
                            }
                        }

                        _progress.ItemCompleted();
                    },
                    _cancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                    var symbolSearchTask = CobyServices.SearchAsync(Consts.CodeBase, CobyServices.EntityTypes.Symbol, _searchPattern, _cancellationToken).SafeContinueWith(p =>
                    {
                        if (p.Result == null)
                        {
                            return;
                        }

                        var patternMatcher = new PatternMatcher(_searchPattern);
                        foreach (var result in p.Result)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            if (result.resultType == "local" || result.resultType == "parameter")
                            {
                                // REVIEW: local symbol information is pulluting results, also making result really big.
                                //         search should have a way to ask specific set of symbol types.
                                //         also, in case there is really big dataset for search, it should provide a way to get data in chunk (paging)
                                //
                                //         also, I can't figure out whether symbol is from metadata as source or not without actually get information about the file.
                                //         so symbol from metadata as source will be included here as well.
                                continue;
                            }

                            var matches = patternMatcher.GetMatches(result.name, result.fullName);
                            if (matches != null)
                            {
                                ReportMatchResult(result, matches);
                            }
                        }

                        _progress.ItemCompleted();
                    },
                    _cancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                    Task.WhenAll(fileSearchTask, symbolSearchTask).SafeContinueWith(_ =>
                    {
                        _callback.Done();
                        navigateToSearch.Dispose();
                        asyncToken.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                }

                private void ReportMatchResult(CobyServices.SearchResult result, IEnumerable<PatternMatch> matches)
                {
                    var matchKind = GetNavigateToMatchKind(matches);

                    // REVIEW: coby currently doesnt have any information whether a symbol is from csharp or vb.
                    //         so it is not possible without actually download all file information whether a symbol is from a file or not.
                    //         so for now, I put "csharp" for everything. any symbol from vb will be completely broken.
                    //
                    //         data inconsitency between Coby and Roslyn is fine as long as it has enough data to convert between them.
                    //         currently, coby provide so little data, so not such it can do. 
                    //         ex) no fully qualified name, contaiing assembly, symbol modifiers and etc
                    var navigateToItem = new NavigateToItem(
                        result.name,
                        result.resultType,
                        "csharp", // completely wrong for vb but there is no way to know whether a symbol in search result is from vb or csharp.
                        result.url,
                        new SearchResult(result, matchKind, new NavigableItem(_workspace, result)),
                        matchKind,
                        result.resultType == "file" ? false : true,
                        _displayFactory);

                    _callback.AddItem(navigateToItem);
                }

                private static MatchKind GetNavigateToMatchKind(IEnumerable<PatternMatch> matchResult)
                {
                    // We may have matched the target string in multiple ways, but we'll answer with the
                    // "most optimistic" answer
                    if (matchResult.Any(r => r.Kind == PatternMatchKind.Exact))
                    {
                        return MatchKind.Exact;
                    }

                    if (matchResult.Any(r => r.Kind == PatternMatchKind.Prefix))
                    {
                        return MatchKind.Prefix;
                    }

                    if (matchResult.Any(r => r.Kind == PatternMatchKind.Substring))
                    {
                        return MatchKind.Substring;
                    }

                    return MatchKind.Regular;
                }

                private class NavigableItem : INavigableItem
                {
                    private readonly CobyWorkspace _workspace;
                    private readonly CobyServices.SearchResult _result;

                    private TextSpan? _sourceSpan;

                    public NavigableItem(CobyWorkspace workspace, CobyServices.SearchResult result)
                    {
                        _workspace = workspace;
                        _result = result;
                    }

                    public bool DisplayFileLocation => true;

                    public string DisplayString => _result.name;

                    // REVIEW: we dont have enough informatino to show right glyph. ex) private, internal, extension method and etc
                    public Glyph Glyph => _result.resultType == "file" ? Glyph.CSharpFile : Glyph.ClassPublic;

                    // dynamically add document to the workspace
                    public Document Document => _workspace.GetOrCreateDocument(_result.compoundUrl, _result.compoundUrl.filePath ?? _result.fileName ?? _result.name);

                    public TextSpan SourceSpan
                    {
                        get
                        {
                            // REVIEW: this is expensive, but there is no other way in current coby design. we need stream based point as well as linecolumn based range.
                            if (_sourceSpan.HasValue)
                            {
                                return _sourceSpan.Value;
                            }

                            if (_result.range.Equals(default(CobyServices.Range)))
                            {
                                _sourceSpan = new TextSpan(0, 0);
                            }
                            else
                            {
                                // REVIEW: this is bad.
                                var text = Document.State.GetText(CancellationToken.None);
                                if (text?.Length == 0)
                                {
                                    return new TextSpan(0, 0);
                                }

                                // Coby is 1 based. Roslyn is 0 based.
                                _sourceSpan = text.Lines.GetTextSpan(
                                    new LinePositionSpan(
                                        new LinePosition(Math.Max(_result.range.startLineNumber - 1, 0), Math.Max(_result.range.startColumn - 1, 0)),
                                        new LinePosition(Math.Max(_result.range.endLineNumber - 1, 0), Math.Max(_result.range.endColumn - 1, 0))));
                            }

                            return _sourceSpan.Value;
                        }
                    }

                    public ImmutableArray<INavigableItem> ChildItems => ImmutableArray<INavigableItem>.Empty;
                }

                private class SearchResult : INavigateToSearchResult
                {
                    public string AdditionalInformation => _result.resultType + " " + (_result.fullName ?? _result.url);
                    public string Name => _result.name;
                    public string Summary => string.Empty;
                    public string Kind => _result.resultType;
                    public string SecondarySort => _result.url;
                    public bool IsCaseSensitive => _result.resultType == "file" ? false : true;

                    public MatchKind MatchKind { get; }
                    public INavigableItem NavigableItem { get; }

                    private readonly CobyServices.SearchResult _result;

                    public SearchResult(CobyServices.SearchResult result, MatchKind matchKind, INavigableItem navigableItem)
                    {
                        _result = result;
                        MatchKind = matchKind;
                        NavigableItem = navigableItem;
                    }
                }
            }
        }
    }
}
