// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal abstract class DiagnosticQueue
    {
        public abstract bool TryComplete();
        public abstract bool TryDequeue(out Diagnostic d);
        public abstract void Enqueue(Diagnostic diagnostic);

        // Methods specific to CategorizedDiagnosticQueue
        public abstract void EnqueueLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer, bool isSyntaxDiagnostic);
        public abstract void EnqueueNonLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> GetLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> GetLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> GetNonLocalDiagnostics(DiagnosticAnalyzer analyzer);

        public static DiagnosticQueue Create(bool categorized = false)
        {
            return categorized ? (DiagnosticQueue)new CategorizedDiagnosticQueue() : new SimpleDiagnosticQueue();
        }

        /// <summary>
        /// Simple diagnostics queue: maintains all diagnostics reported by all analyzers in a single queue.
        /// </summary>
        private sealed class SimpleDiagnosticQueue : DiagnosticQueue
        {
            private readonly AsyncQueue<Diagnostic> _queue;

            public SimpleDiagnosticQueue()
            {
                _queue = new AsyncQueue<Diagnostic>();
            }

            public SimpleDiagnosticQueue(Diagnostic diagnostic)
            {
                _queue = new AsyncQueue<Diagnostic>();
                _queue.Enqueue(diagnostic);
            }

            public override void Enqueue(Diagnostic diagnostic)
            {
                _queue.Enqueue(diagnostic);
            }

            public override void EnqueueLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer, bool isSyntaxDiagnostic)
            {
                _queue.Enqueue(diagnostic);
            }

            public override void EnqueueNonLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                _queue.Enqueue(diagnostic);
            }

            public override ImmutableArray<Diagnostic> GetLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new NotImplementedException();
            }

            public override ImmutableArray<Diagnostic> GetLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new NotImplementedException();
            }

            public override ImmutableArray<Diagnostic> GetNonLocalDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new NotImplementedException();
            }

            public override bool TryComplete()
            {
                return _queue.TryComplete();
            }

            public override bool TryDequeue(out Diagnostic d)
            {
                return _queue.TryDequeue(out d);
            }
        }

        /// <summary>
        /// Categorized diagnostics queue: maintains separate set of simple diagnostic queues for local semantic, local syntax and non-local diagnostics for every analyzer.
        /// </summary>
        private sealed class CategorizedDiagnosticQueue : DiagnosticQueue
        {
            private ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue> _localSemanticDiagnostics;
            private ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue> _localSyntaxDiagnostics;
            private ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue> _nonLocalDiagnostics;

            public CategorizedDiagnosticQueue()
            {
                _localSemanticDiagnostics = null;
                _localSyntaxDiagnostics = null;
                _nonLocalDiagnostics = null;
            }

            public override void Enqueue(Diagnostic diagnostic)
            {
                throw new InvalidOperationException();
            }

            public override void EnqueueLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer, bool isSyntaxDiagnostic)
            {
                if (isSyntaxDiagnostic)
                {
                    if (_localSyntaxDiagnostics == null)
                    {
                        Interlocked.CompareExchange(ref _localSyntaxDiagnostics, new ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue>(), null);
                    }

                    EnqueueCore(_localSyntaxDiagnostics, diagnostic, analyzer);
                }
                else
                {
                    if (_localSemanticDiagnostics == null)
                    {
                        Interlocked.CompareExchange(ref _localSemanticDiagnostics, new ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue>(), null);
                    }

                    EnqueueCore(_localSemanticDiagnostics, diagnostic, analyzer);
                }
            }

            public override void EnqueueNonLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                if (_nonLocalDiagnostics == null)
                {
                    Interlocked.CompareExchange(ref _nonLocalDiagnostics, new ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue>(), null);
                }

                EnqueueCore(_nonLocalDiagnostics, diagnostic, analyzer);
            }

            private static void EnqueueCore(ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue> diagnosticsMap, Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                diagnosticsMap.AddOrUpdate(analyzer,
                    new SimpleDiagnosticQueue(diagnostic),
                    (a, queue) =>
                    {
                        queue.Enqueue(diagnostic);
                        return queue;
                    });
            }

            public override bool TryComplete()
            {
                return true;
            }

            private static bool TryDequeue(ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue> diagnosticsMap, out Diagnostic d)
            {
                Diagnostic diag = null;
                if (diagnosticsMap != null && diagnosticsMap.Any(kvp => kvp.Value.TryDequeue(out diag)))
                {
                    d = diag;
                    return true;
                }

                d = null;
                return false;
            }

            public override bool TryDequeue(out Diagnostic d)
            {
                return TryDequeue(_localSemanticDiagnostics, out d) ||
                    TryDequeue(_localSyntaxDiagnostics, out d) ||
                    TryDequeue(_nonLocalDiagnostics, out d);
            }

            public override ImmutableArray<Diagnostic> GetLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return GetDiagnosticsCore(analyzer, _localSyntaxDiagnostics);
            }

            public override ImmutableArray<Diagnostic> GetLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return GetDiagnosticsCore(analyzer, _localSemanticDiagnostics);
            }

            public override ImmutableArray<Diagnostic> GetNonLocalDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return GetDiagnosticsCore(analyzer, _nonLocalDiagnostics);
            }

            private ImmutableArray<Diagnostic> GetDiagnosticsCore(DiagnosticAnalyzer analyzer, ConcurrentDictionary<DiagnosticAnalyzer, SimpleDiagnosticQueue> diagnosticsMap)
            {
                SimpleDiagnosticQueue queue;
                if (diagnosticsMap != null && diagnosticsMap.TryGetValue(analyzer, out queue))
                {
                    var builder = ImmutableArray.CreateBuilder<Diagnostic>();
                    Diagnostic d;
                    while (queue.TryDequeue(out d))
                    {
                        builder.Add(d);
                    }

                    return builder.ToImmutable();
                }

                return ImmutableArray<Diagnostic>.Empty;
            }
        }
    }
}
