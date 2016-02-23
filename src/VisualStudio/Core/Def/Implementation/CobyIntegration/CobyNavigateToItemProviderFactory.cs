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
                    var fileSearchTask = CobyServices.SearchAsync(Consts.Repo, CobyServices.SearchType.File, _searchPattern, _cancellationToken).SafeContinueWith(p =>
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
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                    TaskScheduler.Default);

                    //var symbolSearchTask = CobyServices.SearchAsync(Consts.Repo, CobyServices.SearchType.Symbol, _searchPattern, _cancellationToken).SafeContinueWith(p =>
                    //{
                    //    if (p.Result == null)
                    //    {
                    //        return;
                    //    }

                    //    var patternMatcher = new PatternMatcher(_searchPattern);
                    //    foreach (var result in p.Result)
                    //    {
                    //        _cancellationToken.ThrowIfCancellationRequested();
                    //        if (result.resultType == "local" || result.resultType == "parameter")
                    //        {
                    //            // REVIEW: local symbol information is pulluting results, also making result really big.
                    //            //         search should have a way to ask specific set of symbol types.
                    //            //         also, in case there is really big dataset for search, it should provide a way to get data in chunk (paging)
                    //            //
                    //            //         also, I can't figure out whether symbol is from metadata as source or not without actually get information about the file.
                    //            //         so symbol from metadata as source will be included here as well.
                    //            continue;
                    //        }

                    //        var matches = patternMatcher.GetMatches(result.name, result.fullName);
                    //        if (matches != null)
                    //        {
                    //            ReportMatchResult(result, matches);
                    //        }
                    //    }

                    //    _progress.ItemCompleted();
                    //},
                    //_cancellationToken,
                    //TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                    //TaskScheduler.Default);

                    //Task.WhenAll(fileSearchTask, symbolSearchTask).SafeContinueWith(_ =>
                    fileSearchTask.SafeContinueWith(_ =>
                    {
                        _callback.Done();
                        navigateToSearch.Dispose();
                        asyncToken.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                }

                private void ReportMatchResult(CobyServices.SearchResponse result, IEnumerable<PatternMatch> matches)
                {
                    var matchKind = GetNavigateToMatchKind(matches);
                    var language = CobyServices.IsVisualBasicProject(result.compoundUrl) ? LanguageNames.VisualBasic : LanguageNames.CSharp;
                    var searchResult = new SearchResult(result, matchKind, new NavigableItem(_workspace, result));
                    var isCaseSensitive = CobyServices.IsFileResult(result) ? false : true;

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
                        language,
                        result.url,
                        searchResult,
                        matchKind,
                        isCaseSensitive,
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
                    private readonly CobyServices.SearchResponse _result;
                    private readonly Task<CobyServices.SourceResponse> _sourceResponseTask;

                    private CobyServices.SourceResponse _lazySourceResponse;
                    private TextSpan? _lazySourceSpan;

                    public NavigableItem(CobyWorkspace workspace, CobyServices.SearchResponse result)
                    {
                        _workspace = workspace;
                        _result = result;
                        _sourceResponseTask = CobyServices.IsFileResult(result) ? null : CobyServices.GetContentBySymbolIdAsync(Consts.Repo, _result.id, CancellationToken.None);
                    }

                    public bool DisplayFileLocation => true;

                    public string DisplayString => _result.name;

                    // REVIEW: we dont have enough informatino to show right glyph. ex) private, internal, extension method and etc
                    public Glyph Glyph => CobyServices.IsFileResult(_result) ?
                        (CobyServices.IsVisualBasicProject(_result.compoundUrl) ? Glyph.BasicFile : Glyph.CSharpFile) :
                        Glyph.ClassPublic;

                    // dynamically add document to the workspace
                    public Document Document
                    {
                        get
                        {
                            EnsureSourceReponse();
                            return _sourceResponseTask != null ? _workspace.GetOrCreateDocument(_lazySourceResponse) : _workspace.GetOrCreateDocument(_result);
                        }
                    }

                    private void EnsureSourceReponse()
                    {
                        if (!CobyServices.IsFileResult(_result) && _lazySourceResponse == null)
                        {
                            _lazySourceResponse = _sourceResponseTask?.WaitAndGetResult(CancellationToken.None);
                        }
                    }

                    private void EnsureSourceReponseAndSpan()
                    {
                        EnsureSourceReponse();

                        if (!_lazySourceSpan.HasValue)
                        {
                            _lazySourceSpan = CobyServices.GetSourceSpan(_lazySourceResponse, () => Document);
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

                    public ImmutableArray<INavigableItem> ChildItems => ImmutableArray<INavigableItem>.Empty;
                }

                private class SearchResult : INavigateToSearchResult
                {
                    public string AdditionalInformation => _result.resultType + " " + (_result.fullName ?? _result.url);
                    public string Name => _result.name;
                    public string Summary => string.Empty;
                    public string Kind => _result.resultType;
                    public string SecondarySort => _result.url;
                    public bool IsCaseSensitive => CobyServices.IsFileResult(_result) ? false : true;

                    public MatchKind MatchKind { get; }
                    public INavigableItem NavigableItem { get; }

                    private readonly CobyServices.SearchResponse _result;

                    public SearchResult(CobyServices.SearchResponse result, MatchKind matchKind, INavigableItem navigableItem)
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
