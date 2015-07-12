// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    /// <summary>
    /// Stores the current partial analysis state for an analyzer.
    /// </summary>
    internal partial class AnalysisState
    {
        private class PerAnalyzerState
        {
            private readonly object _gate = new object();
            private readonly Dictionary<CompilationEvent, AnalyzerStateData> _pendingEvents = new Dictionary<CompilationEvent, AnalyzerStateData>();
            private readonly Dictionary<ISymbol, AnalyzerStateData> _pendingSymbols = new Dictionary<ISymbol, AnalyzerStateData>();
            private readonly Dictionary<SyntaxNode, DeclarationAnalyzerStateData> _pendingDeclarations = new Dictionary<SyntaxNode, DeclarationAnalyzerStateData>();
            private Dictionary<SyntaxTree, AnalyzerStateData> _lazyPendingSyntaxAnalysisTrees = null;

            public IEnumerable<CompilationEvent> PendingEvents_NoLock =>_pendingEvents.Keys;

            public bool HasPendingSyntaxAnalysis(SyntaxTree treeOpt)
            {
                lock (_gate)
                {
                    return _lazyPendingSyntaxAnalysisTrees != null &&
                        (treeOpt != null ? _lazyPendingSyntaxAnalysisTrees.ContainsKey(treeOpt) : _lazyPendingSyntaxAnalysisTrees.Count > 0);
                }
            }

            public bool HasPendingSymbolAnalysis(ISymbol symbol)
            {
                lock (_gate)
                {
                    return _pendingSymbols.ContainsKey(symbol);
                }
            }

            private bool TryStartProcessingEntity<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, Dictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities, out TAnalyzerStateData newState)
                where TAnalyzerStateData : AnalyzerStateData, new()
            {
                lock (_gate)
                {
                    return TryStartProcessingEntity_NoLock(analysisEntity, pendingEntities, out newState);
                }
            }

            private static bool TryStartProcessingEntity_NoLock<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, Dictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities, out TAnalyzerStateData newState)
                where TAnalyzerStateData : AnalyzerStateData, new()
            {
                TAnalyzerStateData currentState;
                if (pendingEntities.TryGetValue(analysisEntity, out currentState) &&
                    (currentState == null || currentState.StateKind == StateKind.Ready))
                {
                    newState = (TAnalyzerStateData)((currentState ?? new TAnalyzerStateData()).WithStateKind(StateKind.InProcess));
                    pendingEntities[analysisEntity] = newState;
                    return true;
                }

                newState = null;
                return false;
            }

            private void MarkEntityProcessed<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, Dictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities)
                where TAnalyzerStateData : AnalyzerStateData
            {
                lock (_gate)
                {
                    MarkEntityProcessed_NoLock(analysisEntity, pendingEntities);
                }
            }

            private static void MarkEntityProcessed_NoLock<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, Dictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities)
                where TAnalyzerStateData : AnalyzerStateData
            {
                pendingEntities.Remove(analysisEntity);
            }

            private bool IsEntityFullyProcessed<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, Dictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities)
                where TAnalyzerStateData : AnalyzerStateData
            {
                lock (_gate)
                {
                    return IsEntityFullyProcessed_NoLock(analysisEntity, pendingEntities);
                }
            }

            private static bool IsEntityFullyProcessed_NoLock<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, Dictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities)
                where TAnalyzerStateData : AnalyzerStateData
            {
                return !pendingEntities.ContainsKey(analysisEntity);
            }

            public bool TryStartProcessingEvent(CompilationEvent compilationEvent, out AnalyzerStateData state)
            {
                return TryStartProcessingEntity(compilationEvent, _pendingEvents, out state);
            }

            public void MarkEventComplete(CompilationEvent compilationEvent)
            {
                MarkEntityProcessed(compilationEvent, _pendingEvents);
            }

            public bool TryStartAnalyzingSymbol(ISymbol symbol, out AnalyzerStateData state)
            {
                return TryStartProcessingEntity(symbol, _pendingSymbols, out state);
            }

            public void MarkSymbolComplete(ISymbol symbol)
            {
                MarkEntityProcessed(symbol, _pendingSymbols);
            }

            public bool TryStartAnalyzingDeclaration(SyntaxReference decl, out DeclarationAnalyzerStateData state)
            {
                return TryStartProcessingEntity(decl.GetSyntax(), _pendingDeclarations, out state);
            }

            public void MarkDeclarationComplete(SyntaxReference decl)
            {
                MarkEntityProcessed(decl.GetSyntax(), _pendingDeclarations);
            }

            public bool TryStartSyntaxAnalysis(SyntaxTree tree, out AnalyzerStateData state)
            {
                Debug.Assert(_lazyPendingSyntaxAnalysisTrees != null);
                return TryStartProcessingEntity(tree, _lazyPendingSyntaxAnalysisTrees, out state);
            }

            public void MarkSyntaxAnalysisComplete(SyntaxTree tree)
            {
                if (_lazyPendingSyntaxAnalysisTrees != null)
                {
                    MarkEntityProcessed(tree, _lazyPendingSyntaxAnalysisTrees);
                }
            }

            public void MarkDeclarationsComplete(ISymbol symbol)
            {
                lock (_gate)
                {
                    foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
                    {
                        MarkEntityProcessed_NoLock(syntaxRef.GetSyntax(), _pendingDeclarations);
                    }
                }
            }

            public void OnCompilationEventGenerated(CompilationEvent compilationEvent, AnalyzerActionCounts actionCounts)
            {
                lock (_gate)
                {
                    OnCompilationEventGenerated_NoLock(compilationEvent, actionCounts);
                }
            }

            private void OnCompilationEventGenerated_NoLock(CompilationEvent compilationEvent, AnalyzerActionCounts actionCounts)
            {
                var symbolEvent = compilationEvent as SymbolDeclaredCompilationEvent;
                if (symbolEvent != null)
                {
                    var needsAnalysis = false;
                    var symbol = symbolEvent.Symbol;
                    if (!AnalysisScope.ShouldSkipSymbolAnalysis(symbol) && actionCounts.SymbolActionsCount > 0)
                    {
                        needsAnalysis = true;
                        _pendingSymbols[symbol] = null;
                    }

                    if (!AnalysisScope.ShouldSkipDeclarationAnalysis(symbol) &&
                        (actionCounts.SyntaxNodeActionsCount > 0 ||
                        actionCounts.CodeBlockActionsCount > 0 ||
                        actionCounts.CodeBlockStartActionsCount > 0))
                    {
                        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
                        {
                            needsAnalysis = true;
                            _pendingDeclarations[syntaxRef.GetSyntax()]= null;
                        }
                    }

                    if (!needsAnalysis)
                    {
                        return;
                    }
                }
                else if (compilationEvent is CompilationStartedEvent)
                {
                    if (actionCounts.SyntaxTreeActionsCount > 0)
                    {
                        var map = new Dictionary<SyntaxTree, AnalyzerStateData>();
                        foreach (var tree in compilationEvent.Compilation.SyntaxTrees)
                        {
                            map[tree] = null;
                        }

                        _lazyPendingSyntaxAnalysisTrees = map;
                    }

                    if (actionCounts.CompilationActionsCount == 0)
                    {
                        return;
                    }
                }

                _pendingEvents[compilationEvent] = null;
            }

            public bool IsEventAnalyzed(CompilationEvent compilationEvent)
            {
                return IsEntityFullyProcessed(compilationEvent, _pendingEvents);
            }

            public void OnSymbolDeclaredEventProcessed(SymbolDeclaredCompilationEvent symbolDeclaredEvent)
            {
                lock (_gate)
                {
                    OnSymbolDeclaredEventProcessed_NoLock(symbolDeclaredEvent);
                }
            }

            private void OnSymbolDeclaredEventProcessed_NoLock(SymbolDeclaredCompilationEvent symbolDeclaredEvent)
            {
                // Check if the symbol event has been completely processed or not.

                // Have the symbol actions executed?
                if (!IsEntityFullyProcessed_NoLock(symbolDeclaredEvent.Symbol, _pendingSymbols))
                {
                    return;
                }

                // Have the node/code block actions executed for all symbol declarations?
                foreach (var syntaxRef in symbolDeclaredEvent.Symbol.DeclaringSyntaxReferences)
                {
                    if (!IsEntityFullyProcessed_NoLock(syntaxRef.GetSyntax(), _pendingDeclarations))
                    {
                        return;
                    }
                }

                // Mark the symbol event completely processed.
                MarkEntityProcessed_NoLock(symbolDeclaredEvent, _pendingEvents);
            }
        }
    }
}
