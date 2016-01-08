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
                    _asyncListener,
                    _displayFactory,
                    callback,
                    searchValue,
                    _cancellationTokenSource.Token);

                searcher.Search();
            }

            private class Searcher
            {
                private readonly ItemDisplayFactory _displayFactory;
                private readonly INavigateToCallback _callback;
                private readonly string _searchPattern;
                private readonly ProgressTracker _progress;
                private readonly IAsynchronousOperationListener _asyncListener;
                private readonly CancellationToken _cancellationToken;

                public Searcher(
                    IAsynchronousOperationListener asyncListener,
                    ItemDisplayFactory displayFactory,
                    INavigateToCallback callback,
                    string searchPattern,
                    CancellationToken cancellationToken)
                {
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

                    _progress.AddItems(2);

                    // make sure we run actual search from other thread. and let this thread return to caller as soon as possible.
                    var dummy = Task.Run(() => Search(navigateToSearch, asyncToken), _cancellationToken);
                }

                private void Search(IDisposable navigateToSearch, IAsyncToken asyncToken)
                {
                    var fileSearchTask = CobyServices.SearchAsync(Consts.CodeBase, CobyServices.EntityTypes.File, _searchPattern, _cancellationToken).SafeContinueWith(p =>
                    {
                        if (p.Result == null)
                        {
                            return;
                        }

                        var patternMatcher = new PatternMatcher(_searchPattern);
                        foreach (var result in p.Result)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
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

                    var navigateToItem = new NavigateToItem(
                        result.name,
                        result.resultType,
                        "csharp",
                        result.url,
                        new SearchResult(result, matchKind, new NavigableItem(result)),
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
                    private readonly CobyServices.SearchResult _result;

                    public NavigableItem(CobyServices.SearchResult result)
                    {
                        _result = result;
                    }

                    public bool DisplayFileLocation => true;

                    public string DisplayString => _result.name;

                    public Glyph Glyph => _result.resultType == "file" ? Glyph.CSharpFile : Glyph.ClassPublic;

                    public Document Document => null;

                    public TextSpan SourceSpan => new TextSpan(0, 0);

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
