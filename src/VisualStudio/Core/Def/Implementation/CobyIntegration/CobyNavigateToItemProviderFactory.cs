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

                    _progress.AddItems(1);

                    // make sure we run actual search from other thread. and let this thread return to caller as soon as possible.
                    var dummy = Task.Run(() => Search(navigateToSearch, asyncToken), _cancellationToken);
                }

                private void Search(IDisposable navigateToSearch, IAsyncToken asyncToken)
                {
                    var searchTask = CobyServices.SearchAsync(Consts.CodeBase, CobyServices.EntityTypes.File, _searchPattern, _cancellationToken);

                    var reportTask = searchTask.SafeContinueWith(p =>
                    {
                        foreach (var result in p.Result)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            ReportMatchResult(result);
                        }

                        _progress.ItemCompleted();
                    },
                    _cancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                    reportTask.SafeContinueWith(_ =>
                    {
                        _callback.Done();
                        navigateToSearch.Dispose();
                        asyncToken.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                }

                private void ReportMatchResult(CobyServices.SearchResult result)
                {
                    //var navigateToItem = new NavigateToItem(
                    //    result.Name,
                    //    result.Kind,
                    //    "csharp",
                    //    result.SecondarySort,
                    //    result,
                    //    result.MatchKind,
                    //    result.IsCaseSensitive,
                    //    _displayFactory);
                    //_callback.AddItem(navigateToItem);
                }
            }
        }
    }
}
