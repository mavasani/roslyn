﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.FindSymbols
{
    internal partial class FindReferencesSearchEngine
    {
        /// <summary>
        /// Symbol set used when <see cref="FindReferencesSearchOptions.UnidirectionalHierarchyCascade"/> is <see
        /// langword="true"/>.
        /// </summary>
        private sealed class UnidirectionalSymbolSet : SymbolSet
        {
            /// <summary>
            /// When we're doing a unidirectional find-references, the initial set of up-symbols can never change.
            /// That's because we have computed the up set entirely up front, and no down symbols can produce new
            /// up-symbols (as going down then up would not be unidirectional).
            /// </summary>
            private readonly ImmutableHashSet<ISymbol> _upSymbols;
            private readonly HashSet<ISymbol> _initialAndDownSymbols;

            public UnidirectionalSymbolSet(Solution solution, HashSet<ISymbol> upSymbols, HashSet<ISymbol> initialSymbols)
                : base(solution)
            {
                _upSymbols = upSymbols.ToImmutableHashSet();
                _initialAndDownSymbols = initialSymbols;
            }

            public static async Task<SymbolSet> CreateAsync(
                Solution solution, HashSet<ISymbol> initialSymbols, CancellationToken cancellationToken)
            {
                // Walk and find all the symbols above the starting symbol set. 
                var upSymbols = await GetAllUpSymbolsAsync(solution, initialSymbols, cancellationToken).ConfigureAwait(false);
                return new UnidirectionalSymbolSet(solution, upSymbols, initialSymbols);
            }

            private static async Task<HashSet<ISymbol>> GetAllUpSymbolsAsync(
                Solution solution, HashSet<ISymbol> initialSymbols, CancellationToken cancellationToken)
            {
                var workQueue = new Stack<ISymbol>();
                PushAll(workQueue, initialSymbols);

                var upSymbols = new HashSet<ISymbol>();

                var allProjects = solution.Projects.ToImmutableHashSet();
                while (workQueue.Count > 0)
                {
                    var currentSymbol = workQueue.Pop();
                    await AddUpSymbolsAsync(solution, currentSymbol, upSymbols, workQueue, allProjects, cancellationToken).ConfigureAwait(false);
                }

                return upSymbols;
            }

            public override ImmutableArray<ISymbol> GetAllSymbols()
            {
                using var _ = ArrayBuilder<ISymbol>.GetInstance(out var result);
                result.AddRange(_upSymbols);
                result.AddRange(_initialAndDownSymbols);
                result.RemoveDuplicates();
                return result.ToImmutable();
            }

            public override async Task InheritanceCascadeAsync(Project project, CancellationToken cancellationToken)
            {
                // Start searching using the existing set of symbols found at the start (or anything found below that).
                var workQueue = new Stack<ISymbol>();
                PushAll(workQueue, _initialAndDownSymbols);

                var projects = ImmutableHashSet.Create(project);

                while (workQueue.Count > 0)
                {
                    var current = workQueue.Pop();

                    // Keep adding symbols downwards in this project as long as we keep finding new symbols.
                    await AddDownSymbolsAsync(current, _initialAndDownSymbols, workQueue, projects, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
