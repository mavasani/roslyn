// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    /// <summary>
    /// Stores the current partial analysis state for an analyzer.
    /// </summary>
    internal partial class AnalysisState
    {
        private class PerAnalyzerState
        {
            private readonly ConcurrentDictionary<CompilationEvent, AnalyzerStateData> _pendingEvents = new ConcurrentDictionary<CompilationEvent, AnalyzerStateData>();
            private readonly ConcurrentDictionary<ISymbol, AnalyzerStateData> _pendingSymbols = new ConcurrentDictionary<ISymbol, AnalyzerStateData>();
            private readonly ConcurrentDictionary<SyntaxNode, DeclarationAnalyzerStateData> _pendingDeclarations = new ConcurrentDictionary<SyntaxNode, DeclarationAnalyzerStateData>();
            private readonly ConcurrentDictionary<SyntaxTree, AnalyzerStateData> _pendingSyntaxAnalysisTrees = new ConcurrentDictionary<SyntaxTree, AnalyzerStateData>();

            public ImmutableArray<CompilationEvent> PendingEvents => _pendingEvents.Keys.ToImmutableArray();

            public PerAnalyzerState(Compilation compilation)
            {
                foreach (var tree in compilation.SyntaxTrees)
                {
                    _pendingSyntaxAnalysisTrees[tree] = null;
                }
            }

            public bool HasPendingSyntaxAnalysis(SyntaxTree treeOpt)
            {
                return treeOpt != null ?
                    _pendingSyntaxAnalysisTrees.ContainsKey(treeOpt) :
                    _pendingSyntaxAnalysisTrees.Count > 0;
            }

            public bool HasPendingSymbolAnalysis(ISymbol symbol)
            {
                return _pendingSymbols.ContainsKey(symbol);
            }

            private static bool TryStartProcessingEntity<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, ConcurrentDictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities, out TAnalyzerStateData newState)
                where TAnalyzerStateData : AnalyzerStateData, new()
            {
                TAnalyzerStateData currentState;
                if (pendingEntities.TryGetValue(analysisEntity, out currentState) &&
                    (currentState == null || currentState.StateKind == StateKind.Ready))
                {
                    newState = (TAnalyzerStateData)((currentState ?? new TAnalyzerStateData()).WithStateKind(StateKind.InProcess));
                    return pendingEntities.TryUpdate(analysisEntity, newState, currentState);
                }

                newState = null;
                return false;
            }

            private static void MarkEntityProcessed<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, ConcurrentDictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities)
                where TAnalyzerStateData : AnalyzerStateData
            {
                TAnalyzerStateData state;
                var wasBeingProcessed = pendingEntities.TryRemove(analysisEntity, out state);
                Debug.Assert(wasBeingProcessed || state == null);
            }

            private static bool IsEntityFullyProcessed<TAnalysisEntity, TAnalyzerStateData>(TAnalysisEntity analysisEntity, ConcurrentDictionary<TAnalysisEntity, TAnalyzerStateData> pendingEntities)
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
                return TryStartProcessingEntity(tree, _pendingSyntaxAnalysisTrees, out state);
            }

            public void MarkSyntaxAnalysisComplete(SyntaxTree tree)
            {
                MarkEntityProcessed(tree, _pendingSyntaxAnalysisTrees);
            }

            public void MarkDeclarationsComplete(ISymbol symbol)
            {
                foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
                {
                    MarkEntityProcessed(syntaxRef.GetSyntax(), _pendingDeclarations);
                }
            }

            public void OnCompilationEventGenerated(CompilationEvent compilationEvent)
            {
                var symbolEvent = compilationEvent as SymbolDeclaredCompilationEvent;
                if (symbolEvent != null)
                {
                    var needsAnalysis = false;
                    var symbol = symbolEvent.Symbol;
                    if (!AnalysisScope.ShouldSkipSymbolAnalysis(symbol))
                    {
                        needsAnalysis = true;
                        _pendingSymbols.TryAdd(symbol, null);
                    }

                    if (!AnalysisScope.ShouldSkipDeclarationAnalysis(symbol))
                    {
                        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
                        {
                            needsAnalysis = true;
                            _pendingDeclarations.TryAdd(syntaxRef.GetSyntax(), null);
                        }
                    }

                    if (!needsAnalysis)
                    {
                        return;
                    }
                }

                _pendingEvents.TryAdd(compilationEvent, null);
            }

            public bool IsEventAnalyzed(CompilationEvent compilationEvent)
            {
                return IsEntityFullyProcessed(compilationEvent, _pendingEvents);
            }

            public void AnalyzeSymbolEvent(SymbolDeclaredCompilationEvent symbolDeclaredEvent)
            {
                if (IsEntityFullyProcessed(symbolDeclaredEvent, _pendingEvents))
                {
                    return;
                }

                if (!IsEntityFullyProcessed(symbolDeclaredEvent.Symbol, _pendingSymbols))
                {
                    return;
                }

                foreach (var syntaxRef in symbolDeclaredEvent.Symbol.DeclaringSyntaxReferences)
                {
                    if (!IsEntityFullyProcessed(syntaxRef.GetSyntax(), _pendingDeclarations))
                    {
                        return;
                    }
                }

                MarkEntityProcessed(symbolDeclaredEvent, _pendingEvents);
            }
        }
    }
}
