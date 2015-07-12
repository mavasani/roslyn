// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using Roslyn.Utilities;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    /// <summary>
    /// Stores the partial analysis state for analyzers executed on a specific compilation.
    /// </summary>
    internal partial class AnalysisState
    {
        /// <summary>
        /// Per-analyzer analysis state map.
        /// </summary>
        private readonly ConcurrentDictionary<DiagnosticAnalyzer, PerAnalyzerState> _analyzerStateMap;

        /// <summary>
        /// Compilation events corresponding to source tree, that are not completely processed for all analyzers.
        /// </summary>
        private readonly ConcurrentDictionary<SyntaxTree, ImmutableHashSet<CompilationEvent>> _eventsCache;

        /// <summary>
        /// Compilation events corresponding to the compilation (compilation start and completed events), that are not completely processed for all analyzers.
        /// </summary>
        private readonly ConcurrentSet<CompilationEvent> _nonSourceEvents;

        public AnalysisState(Compilation compilation, ImmutableArray<DiagnosticAnalyzer> analyzers)
        {
            _analyzerStateMap = CreateAnalyzerStateMap(compilation, analyzers);
            _eventsCache = new ConcurrentDictionary<SyntaxTree, ImmutableHashSet<CompilationEvent>>();
            _nonSourceEvents = new ConcurrentSet<CompilationEvent>();
        }

        private static ConcurrentDictionary<DiagnosticAnalyzer, PerAnalyzerState> CreateAnalyzerStateMap(Compilation compilation, ImmutableArray<DiagnosticAnalyzer> analyzers)
        {
            var map = new ConcurrentDictionary<DiagnosticAnalyzer, PerAnalyzerState>();
            foreach (var analyzer in analyzers)
            {
                map[analyzer] = new PerAnalyzerState(compilation);
            }

            return map;
        }

        public void OnCompilationEventGenerated(CompilationEvent compilationEvent)
        {
            // Add the event to our global pending event cache.
            UpdateEventsCache(compilationEvent, generated: true);

            // Mark the event for analysis for each analyzer.
            foreach (var analyzerState in _analyzerStateMap.Values)
            {
                analyzerState.OnCompilationEventGenerated(compilationEvent);
            }
        }

        private void AnalyzeSymbolEvent(SymbolDeclaredCompilationEvent symbolDeclaredEvent, IEnumerable<DiagnosticAnalyzer> analyzers)
        {
            foreach (var analyzer in analyzers)
            {
                _analyzerStateMap[analyzer].AnalyzeSymbolEvent(symbolDeclaredEvent);
            }
        }

        /// <summary>
        /// Invoke this method at completion of event processing for the given analysis scope.
        /// It updates the analysis state of this event for each analyzer and if the event has been fully processed for all analyzers, then removes it from our event cache.
        /// </summary>
        public void AnalyzeEvent(CompilationEvent compilationEvent, AnalysisScope analysisScope)
        {
            // Analyze if the symbol and all its declaring syntax references are analyzed.
            var symbolDeclaredEvent = compilationEvent as SymbolDeclaredCompilationEvent;
            if (symbolDeclaredEvent != null)
            {
                AnalyzeSymbolEvent(symbolDeclaredEvent, analysisScope.Analyzers);
            }

            // Check if event is fully analyzed for all analyzers.
            foreach (var analyzerState in _analyzerStateMap.Values)
            {
                if (!analyzerState.IsEventAnalyzed(compilationEvent))
                {
                    return;
                }
            }

            // Remove the event from event cache.
            UpdateEventsCache(compilationEvent, generated: false);
        }

        public void UpdateEventsCache(CompilationEvent compilationEvent, bool generated)
        {
            var symbolEvent = compilationEvent as SymbolDeclaredCompilationEvent;
            if (symbolEvent != null)
            {
                // Add/remove symbol events to per-tree event cache.
                // Any diagnostics request for a tree should trigger symbol and syntax node analysis for symbols with at least one declaring reference in the tree.
                foreach (var location in symbolEvent.Symbol.Locations)
                {
                    if (location.SourceTree != null)
                    {
                        if (generated)
                        {
                            _eventsCache.AddOrUpdate(location.SourceTree, ImmutableHashSet.Create(compilationEvent), (tree, events) => events.Add(compilationEvent));
                        }
                        else
                        {
                            RemoveCompilationEvent(location.SourceTree, compilationEvent);
                        }
                    }
                }
            }
            else
            {
                // Add/remove compilation unit completed events to per-tree event cache.
                var compilationUnitCompletedEvent = compilationEvent as CompilationUnitCompletedEvent;
                if (compilationUnitCompletedEvent != null)
                {
                    var tree = compilationUnitCompletedEvent.SemanticModel.SyntaxTree;
                    if (generated)
                    {
                        _eventsCache.AddOrUpdate(tree, ImmutableHashSet.Create(compilationEvent), (t, events) => events.Add(compilationEvent));
                    }
                    else
                    {
                        RemoveCompilationEvent(tree, compilationEvent);
                    }
                }
                else if (compilationEvent is CompilationStartedEvent || compilationEvent is CompilationCompletedEvent)
                {
                    // Add/remove compilation events to '_nonSourceEvents' event cache.
                    if (generated)
                    {
                        _nonSourceEvents.Add(compilationEvent);
                    }
                    else
                    {
                        _nonSourceEvents.Remove(compilationEvent);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unexpected compilation event of type " + compilationEvent.GetType().Name);
                }
            }
        }

        private void RemoveCompilationEvent(SyntaxTree tree, CompilationEvent compilationEvent)
        {
            ImmutableHashSet<CompilationEvent> currentEvents;
            if (_eventsCache.TryGetValue(tree, out currentEvents) && currentEvents.Contains(compilationEvent))
            {
                _eventsCache.TryUpdate(tree, currentEvents.Remove(compilationEvent), currentEvents);
            }
        }

        private HashSet<CompilationEvent> GetPendingEvents(ImmutableArray<DiagnosticAnalyzer> analyzers)
        {
            var uniqueEvents = new HashSet<CompilationEvent>();
            foreach (var analyzer in analyzers)
            {
                var analysisState = _analyzerStateMap[analyzer];
                foreach (var pendingEvent in analysisState.PendingEvents)
                {
                    uniqueEvents.Add(pendingEvent);
                }
            }

            return uniqueEvents;
        }

        /// <summary>
        /// Gets pending events for given set of analyzers for the given syntax tree.
        /// </summary>
        public IEnumerable<CompilationEvent> GetPendingEvents(ImmutableArray<DiagnosticAnalyzer> analyzers, SyntaxTree tree)
        {
            ImmutableHashSet<CompilationEvent> compilationEventsForTree;
            if (_eventsCache.TryGetValue(tree, out compilationEventsForTree))
            {
                var pendingEvents = GetPendingEvents(analyzers);
                if (pendingEvents.Count > 0)
                {
                    foreach (var compilationEventForTree in compilationEventsForTree)
                    {
                        if (pendingEvents.Contains(compilationEventForTree))
                        {
                            yield return compilationEventForTree;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets all pending events for given set of analyzers.
        /// </summary>
        /// <param name="analyzers"></param>
        /// <param name="includeSourceEvents">Indicates if source events (symbol declared, compilation unit completed event) should be included.</param>
        /// <param name="includeNonSourceEvents">Indicates if compilation wide events (compilation started and completed event) should be included.</param>
        public IEnumerable<CompilationEvent> GetPendingEvents(ImmutableArray<DiagnosticAnalyzer> analyzers, bool includeSourceEvents, bool includeNonSourceEvents)
        {
            var pendingEvents = GetPendingEvents(analyzers);

            if (includeSourceEvents)
            {
                var uniqueEvents = new HashSet<CompilationEvent>();
                foreach (var compilationEvents in _eventsCache.Values)
                {
                    foreach (var compilationEvent in compilationEvents)
                    {
                        if (pendingEvents.Contains(compilationEvent) && uniqueEvents.Add(compilationEvent))
                        {
                            yield return compilationEvent;
                        }
                    }
                }
            }

            if (includeNonSourceEvents)
            {
                foreach (var compilationEvent in _nonSourceEvents)
                {
                    if (pendingEvents.Contains(compilationEvent))
                    {
                        yield return compilationEvent;
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if we have any pending syntax analysis for given analysis scope.
        /// </summary>
        public bool HasPendingSyntaxAnalysis(AnalysisScope analysisScope)
        {
            if (analysisScope.IsTreeAnalysis && !analysisScope.IsSyntaxOnlyTreeAnalysis)
            {
                return false;
            }

            foreach (var analyzer in analysisScope.Analyzers)
            {
                if (_analyzerStateMap[analyzer].HasPendingSyntaxAnalysis(analysisScope.FilterTreeOpt))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true if we have any pending symbol analysis for given analysis scope.
        /// </summary>
        public bool HasPendingSymbolAnalysis(AnalysisScope analysisScope)
        {
            Debug.Assert(analysisScope.FilterTreeOpt != null);

            ImmutableHashSet<CompilationEvent> compilationEvents;
            if (_eventsCache.TryGetValue(analysisScope.FilterTreeOpt, out compilationEvents))
            {
                foreach (var compilationEvent in compilationEvents)
                {
                    var symbolDeclaredEvent = compilationEvent as SymbolDeclaredCompilationEvent;
                    if (symbolDeclaredEvent != null && analysisScope.ShouldAnalyze(symbolDeclaredEvent.Symbol))
                    {
                        foreach (var analyzer in analysisScope.Analyzers)
                        {
                            if (_analyzerStateMap[analyzer].HasPendingSymbolAnalysis(symbolDeclaredEvent.Symbol))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to start processing a compilation event for the given analyzer.
        /// </summary>
        /// <returns>
        /// Returns false if the event has already been processed for the analyzer OR is currently being processed by another task.
        /// If true, then it returns a non-null <paramref name="state"/> representing partial analysis state for the given event for the given analyzer.
        /// </returns>
        public bool TryStartProcessingEvent(CompilationEvent compilationEvent, DiagnosticAnalyzer analyzer, out AnalyzerStateData state)
        {
            return _analyzerStateMap[analyzer].TryStartProcessingEvent(compilationEvent, out state);
        }

        /// <summary>
        /// Marks the given event as fully analyzed for the given analyzer.
        /// </summary>
        public void MarkEventComplete(CompilationEvent compilationEvent, DiagnosticAnalyzer analyzer)
        {
            _analyzerStateMap[analyzer].MarkEventComplete(compilationEvent);
        }

        /// <summary>
        /// Attempts to start processing a symbol for the given analyzer's symbol actions.
        /// </summary>
        /// <returns>
        /// Returns false if the symbol has already been processed for the analyzer OR is currently being processed by another task.
        /// If true, then it returns a non-null <paramref name="state"/> representing partial analysis state for the given symbol for the given analyzer.
        /// </returns>
        public bool TryStartAnalyzingSymbol(ISymbol symbol, DiagnosticAnalyzer analyzer, out AnalyzerStateData state)
        {
            return _analyzerStateMap[analyzer].TryStartAnalyzingSymbol(symbol, out state);
        }

        /// <summary>
        /// Marks the given symbol as fully analyzed for the given analyzer.
        /// </summary>
        public void MarkSymbolComplete(ISymbol symbol, DiagnosticAnalyzer analyzer)
        {
            _analyzerStateMap[analyzer].MarkSymbolComplete(symbol);
        }

        /// <summary>
        /// Attempts to start processing a symbol declaration for the given analyzer's syntax node and code block actions.
        /// </summary>
        /// <returns>
        /// Returns false if the declaration has already been processed for the analyzer OR is currently being processed by another task.
        /// If true, then it returns a non-null <paramref name="state"/> representing partial analysis state for the given declaration for the given analyzer.
        /// </returns>
        public bool TryStartAnalyzingDeclaration(SyntaxReference decl, DiagnosticAnalyzer analyzer, out DeclarationAnalyzerStateData state)
        {
            return _analyzerStateMap[analyzer].TryStartAnalyzingDeclaration(decl, out state);
        }

        /// <summary>
        /// Marks the given symbol declaration as fully analyzed for the given analyzer.
        /// </summary>
        public void MarkDeclarationComplete(SyntaxReference decl, DiagnosticAnalyzer analyzer)
        {
            _analyzerStateMap[analyzer].MarkDeclarationComplete(decl);
        }

        /// <summary>
        /// Marks all the symbol declarations for the given symbol as fully analyzed for all the given analyzers.
        /// </summary>
        public void MarkDeclarationsComplete(ISymbol symbol, IEnumerable<DiagnosticAnalyzer> analyzers)
        {
            foreach (var analyzer in analyzers)
            {
                _analyzerStateMap[analyzer].MarkDeclarationsComplete(symbol);
            }
        }

        /// <summary>
        /// Attempts to start processing a syntax tree for the given analyzer's syntax tree actions.
        /// </summary>
        /// <returns>
        /// Returns false if the tree has already been processed for the analyzer OR is currently being processed by another task.
        /// If true, then it returns a non-null <paramref name="state"/> representing partial syntax analysis state for the given tree for the given analyzer.
        /// </returns>
        public bool TryStartSyntaxAnalysis(SyntaxTree tree, DiagnosticAnalyzer analyzer, out AnalyzerStateData state)
        {
            return _analyzerStateMap[analyzer].TryStartSyntaxAnalysis(tree, out state);
        }

        /// <summary>
        /// Marks the given tree as fully syntactically analyzed for the given analyzer.
        /// </summary>
        public void MarkSyntaxAnalysisComplete(SyntaxTree tree, DiagnosticAnalyzer analyzer)
        {
            _analyzerStateMap[analyzer].MarkSyntaxAnalysisComplete(tree);
        }
    }
}
