// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    /// <summary>
    /// Queue to store analyzer diagnostics on the <see cref="AnalyzerDriver"/>.
    /// </summary>
    internal abstract class DiagnosticQueue
    {
        public abstract bool TryComplete();
        public abstract bool TryDequeue(DiagnosticAnalyzer analyzer, out Diagnostic d);
        public abstract void Enqueue(Diagnostic diagnostic, DiagnosticAnalyzer analyzer);

        // Methods specific to CategorizedDiagnosticQueue
        public abstract void EnqueueLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer, bool isSyntaxDiagnostic);
        public abstract void EnqueueNonLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> DequeueNonCatergorizedDiagnostics(DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> DequeueCategorizedLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> DequeueCategorizedLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> DequeueCategorizedNonLocalDiagnostics(DiagnosticAnalyzer analyzer);

        public static DiagnosticQueue Create(bool categorized = false)
        {
            return categorized ? (DiagnosticQueue)new CategorizedDiagnosticQueue() : new NonCategorizedDiagnosticQueue();
        }

        /// <summary>
        /// Non categorized diagnostics queue: maintains all diagnostics reported by each analyzer in a single queue, i.e. does not categorize local (syntax or semantic) versus non-local diagnostics.
        /// </summary>
        private sealed class NonCategorizedDiagnosticQueue : DiagnosticQueue
        {
            private readonly object _gate = new object();
            private Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>> _lazyDiagnosticsMap;
            private bool _sealed;

            public NonCategorizedDiagnosticQueue()
            {
                _lazyDiagnosticsMap = null;
                _sealed = false;
            }

            public override void Enqueue(Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                lock (_gate)
                {
                    Contract.Assert(!_sealed);
                    _lazyDiagnosticsMap = _lazyDiagnosticsMap ?? new Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>>();
                    Enqueue_NoLock(_lazyDiagnosticsMap, diagnostic, analyzer);
                }
            }

            private static void Enqueue_NoLock(Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>> diagnosticsMap, Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                Queue<Diagnostic> queue;
                if (!diagnosticsMap.TryGetValue(analyzer, out queue))
                {
                    queue = diagnosticsMap[analyzer] = new Queue<Diagnostic>();
                }

                queue.Enqueue(diagnostic);
            }

            public override void EnqueueLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer, bool isSyntaxDiagnostic)
            {
                Enqueue(diagnostic, analyzer);
            }

            public override void EnqueueNonLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                Enqueue(diagnostic, analyzer);
            }

            public override ImmutableArray<Diagnostic> DequeueCategorizedLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new InvalidOperationException();
            }

            public override ImmutableArray<Diagnostic> DequeueCategorizedLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new InvalidOperationException();
            }

            public override ImmutableArray<Diagnostic> DequeueCategorizedNonLocalDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new InvalidOperationException();
            }

            public override bool TryComplete()
            {
                lock (_gate)
                {
                    _sealed = true;
                    return true;
                }
            }

            public override bool TryDequeue(DiagnosticAnalyzer analyzer, out Diagnostic diagnostic)
            {
                lock (_gate)
                {
                    return TryDequeue_NoLock(_lazyDiagnosticsMap, analyzer, out diagnostic);
                }
            }

            private static bool TryDequeue_NoLock(Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>> diagnosticsMapOpt, DiagnosticAnalyzer analyzer, out Diagnostic diagnostic)
            {
                Queue<Diagnostic> queue;
                if (diagnosticsMapOpt == null || !diagnosticsMapOpt.TryGetValue(analyzer, out queue) || queue.Count == 0)
                {
                    diagnostic = null;
                    return false;
                }

                diagnostic = queue.Dequeue();
                return true;
            }

            public override ImmutableArray<Diagnostic> DequeueNonCatergorizedDiagnostics(DiagnosticAnalyzer analyzer)
            {
                lock (_gate)
                {
                    return DequeueNonCatergorizedDiagnostics_NoLock(_lazyDiagnosticsMap, analyzer);
                }
            }

            private static ImmutableArray<Diagnostic> DequeueNonCatergorizedDiagnostics_NoLock(Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>> diagnosticsMapOpt, DiagnosticAnalyzer analyzer)
            {
                Queue<Diagnostic> queue;
                if (diagnosticsMapOpt == null || !diagnosticsMapOpt.TryGetValue(analyzer, out queue) || queue.Count == 0)
                {
                    return ImmutableArray<Diagnostic>.Empty;
                }

                var builder = ImmutableArray.CreateBuilder<Diagnostic>();
                while (queue.Count > 0)
                {
                    builder.Add(queue.Dequeue());
                }

                return builder.ToImmutable();
            }
        }

        /// <summary>
        /// Categorized diagnostics queue: maintains separate set of non categorized diagnostic queues for local semantic, local syntax and non-local diagnostics for every analyzer.
        /// </summary>
        private sealed class CategorizedDiagnosticQueue : DiagnosticQueue
        {
            private readonly NonCategorizedDiagnosticQueue _localSemanticDiagnostics;
            private readonly NonCategorizedDiagnosticQueue _localSyntaxDiagnostics;
            private readonly NonCategorizedDiagnosticQueue _nonLocalDiagnostics;

            public CategorizedDiagnosticQueue()
            {
                _localSemanticDiagnostics = new NonCategorizedDiagnosticQueue();
                _localSyntaxDiagnostics = new NonCategorizedDiagnosticQueue();
                _nonLocalDiagnostics = new NonCategorizedDiagnosticQueue();
            }

            public override void Enqueue(Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                throw new InvalidOperationException();
            }

            public override void EnqueueLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer, bool isSyntaxDiagnostic)
            {
                if (isSyntaxDiagnostic)
                {
                    EnqueueCore(_localSyntaxDiagnostics, diagnostic, analyzer);
                }
                else
                {
                    EnqueueCore(_localSemanticDiagnostics, diagnostic, analyzer);
                }
            }

            public override void EnqueueNonLocal(Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                EnqueueCore(_nonLocalDiagnostics, diagnostic, analyzer);
            }

            private void EnqueueCore(NonCategorizedDiagnosticQueue diagnosticsQueue, Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                diagnosticsQueue.Enqueue(diagnostic, analyzer);
            }

            public override bool TryComplete()
            {
                var syntaxCompleted = _localSyntaxDiagnostics.TryComplete();
                var semanticCompleted = _localSemanticDiagnostics.TryComplete();
                var nonLocalCompleted = _nonLocalDiagnostics.TryComplete();
                return syntaxCompleted && semanticCompleted && nonLocalCompleted;
            }

            public override bool TryDequeue(DiagnosticAnalyzer analyzer, out Diagnostic diagnostic)
            {
                return TryDequeueCore(_localSemanticDiagnostics, analyzer, out diagnostic) ||
                    TryDequeueCore(_localSyntaxDiagnostics, analyzer, out diagnostic) ||
                    TryDequeueCore(_nonLocalDiagnostics, analyzer, out diagnostic);
            }

            private static bool TryDequeueCore(NonCategorizedDiagnosticQueue diagnostics, DiagnosticAnalyzer analyzer, out Diagnostic diagnostic)
            {
                return diagnostics.TryDequeue(analyzer, out diagnostic);
            }

            public override ImmutableArray<Diagnostic> DequeueNonCatergorizedDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new InvalidOperationException();
            }

            public override ImmutableArray<Diagnostic> DequeueCategorizedLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return DequeueDiagnosticsCore(analyzer, _localSyntaxDiagnostics);
            }

            public override ImmutableArray<Diagnostic> DequeueCategorizedLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return DequeueDiagnosticsCore(analyzer, _localSemanticDiagnostics);
            }

            public override ImmutableArray<Diagnostic> DequeueCategorizedNonLocalDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return DequeueDiagnosticsCore(analyzer, _nonLocalDiagnostics);
            }

            private static ImmutableArray<Diagnostic> DequeueDiagnosticsCore(DiagnosticAnalyzer analyzer, NonCategorizedDiagnosticQueue diagnostics)
            {
                var builder = ImmutableArray.CreateBuilder<Diagnostic>();
                Diagnostic diagnostic;
                while (diagnostics.TryDequeue(analyzer, out diagnostic))
                {
                    builder.Add(diagnostic);
                }

                return builder.ToImmutable();
            }
        }
    }
}
