// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.Editor.Navigation;
using Microsoft.CodeAnalysis.Editor.SymbolMapping;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.FindReferences
{
    internal abstract class AbstractFindReferencesService : IFindReferencesService
    {
        private readonly IEnumerable<IReferencedSymbolsPresenter> _referenceSymbolPresenters;
        private readonly IEnumerable<INavigableItemsPresenter> _navigableItemPresenters;

        protected AbstractFindReferencesService(IEnumerable<IReferencedSymbolsPresenter> referenceSymbolPresenters, IEnumerable<INavigableItemsPresenter> navigableItemPresenters)
        {
            _referenceSymbolPresenters = referenceSymbolPresenters;
            _navigableItemPresenters = navigableItemPresenters;
        }

        private async Task<Tuple<IEnumerable<ReferencedSymbol>, Solution>> FindReferencedSymbolsAsync(Document document, int position, IWaitContext waitContext)
        {
            var cancellationToken = waitContext.CancellationToken;

            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (symbol != null)
            {
                // If this document is not in the primary workspace, we may want to search for results
                // in a solution different from the one we started in. Use the starting workspace's
                // ISymbolMappingService to get a context for searching in the proper solution.
                var mappingService = document.Project.Solution.Workspace.Services.GetService<ISymbolMappingService>();

                var mapping = await mappingService.MapSymbolAsync(document, symbol, cancellationToken).ConfigureAwait(false);
                if (mapping != null)
                {
                    var displayName = mapping.Symbol.IsConstructor() ? mapping.Symbol.ContainingType.Name : mapping.Symbol.Name;

                    waitContext.Message = string.Format(EditorFeaturesResources.FindingReferencesOf, displayName);

                    var result = await SymbolFinder.FindReferencesAsync(mapping.Symbol, mapping.Solution, cancellationToken).ConfigureAwait(false);
                    var searchSolution = mapping.Solution;

                    return Tuple.Create(result, searchSolution);
                }
            }

            return null;
        }

        public async Task<IEnumerable<INavigableItem>> FindReferencesAsync(Document document, int position, IWaitContext waitContext)
        {
            var cancellationToken = waitContext.CancellationToken;
            var result = await this.FindReferencedSymbolsAsync(document, position, waitContext).ConfigureAwait(false);
            if (result == null)
            {
                return SpecializedCollections.EmptyEnumerable<INavigableItem>();
            }

            var referencedSymbols = result.Item1;
            var searchSolution = result.Item2;

            var q = from r in referencedSymbols
                    from loc in r.Locations
                    select NavigableItemFactory.GetItemFromSymbolLocation(searchSolution, r.Definition, loc.Location);

            // realize the list here so that the consumer await'ing the result doesn't lazily cause
            // them to be created on an inappropriate thread.
            return q.ToList();
        }

        public bool TryFindReferences(Document document, int position, IWaitContext waitContext)
        {
            var cancellationToken = waitContext.CancellationToken;

            Tuple<IEnumerable<ReferencedSymbol>, Solution> result = null;
            var service = document.Project.Solution.Workspace.Services.GetService<IFindReferencesServiceInternal>();
            if (service != null && _navigableItemPresenters != null && _navigableItemPresenters.Any())
            {
                var navigableItems = service.FindReferencesAsync(document, position, cancellationToken).WaitAndGetResult(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (navigableItems != null)
                {
                    if (DisplayReferences(navigableItems))
                    {
                        return true;
                    }

                    result = Tuple.Create(SpecializedCollections.EmptyEnumerable<ReferencedSymbol>(), document.Project.Solution);
                }
            }

            if (result == null)
            {
                result = this.FindReferencedSymbolsAsync(document, position, waitContext).WaitAndGetResult(cancellationToken);
            }

            return DisplayReferences(result);
        }

        private bool DisplayReferences(Tuple<IEnumerable<ReferencedSymbol>, Solution> result)
        {
            if (result != null && result.Item1 != null)
            {
                var searchSolution = result.Item2;
                foreach (var presenter in _referenceSymbolPresenters)
                {
                    presenter.DisplayResult(searchSolution, result.Item1);
                    return true;
                }
            }

            return false;
        }

        private bool DisplayReferences(IEnumerable<INavigableItem> result)
        {
            if (result != null && result.Any())
            {
                var title = result.First().DisplayString;
                foreach (var presenter in _navigableItemPresenters)
                {
                    presenter.DisplayResult(title, result);
                    return true;
                }
            }

            return false;
        }
    }
}
