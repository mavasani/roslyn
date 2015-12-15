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
        public abstract ImmutableArray<Diagnostic> DequeueLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> DequeueLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer);
        public abstract ImmutableArray<Diagnostic> DequeueNonLocalDiagnostics(DiagnosticAnalyzer analyzer);

        public static DiagnosticQueue Create(bool categorized = false)
        {
            return categorized ? (DiagnosticQueue)new CategorizedDiagnosticQueue() : new SimpleDiagnosticQueue();
        }

        /// <summary>
        /// Simple diagnostics queue: maintains all diagnostics reported by all analyzers in a single queue.
        /// </summary>
        private sealed class SimpleDiagnosticQueue : DiagnosticQueue
        {
            private readonly object _gate = new object();
            private Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>> _diagnosticsMap;
            private bool _sealed;

            public SimpleDiagnosticQueue()
            {
                _diagnosticsMap = new Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>>();
                _sealed = false;
            }

            public SimpleDiagnosticQueue(Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                _diagnosticsMap = new Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>>();
                _sealed = false;
                Enqueue(diagnostic, analyzer);
            }

            public override void Enqueue(Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                lock (_gate)
                {
                    Contract.Assert(!_sealed);
                    Enqueue_NoLock(_diagnosticsMap, diagnostic, analyzer);
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

            public override ImmutableArray<Diagnostic> DequeueLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new NotImplementedException();
            }

            public override ImmutableArray<Diagnostic> DequeueLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new NotImplementedException();
            }

            public override ImmutableArray<Diagnostic> DequeueNonLocalDiagnostics(DiagnosticAnalyzer analyzer)
            {
                throw new NotImplementedException();
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
                    return TryDequeue_NoLock(_diagnosticsMap, analyzer, out diagnostic);
                }
            }

            private static bool TryDequeue_NoLock(Dictionary<DiagnosticAnalyzer, Queue<Diagnostic>> diagnosticsMap, DiagnosticAnalyzer analyzer, out Diagnostic diagnostic)
            {
                Queue<Diagnostic> diagnostics;
                if (diagnosticsMap.TryGetValue(analyzer, out diagnostics) && diagnostics.Count > 0)
                {
                    diagnostic = diagnostics.Dequeue();
                    return true;
                }

                diagnostic = null;
                return false;
            }
        }

        /// <summary>
        /// Categorized diagnostics queue: maintains separate set of simple diagnostic queues for local semantic, local syntax and non-local diagnostics for every analyzer.
        /// </summary>
        private sealed class CategorizedDiagnosticQueue : DiagnosticQueue
        {
            private readonly SimpleDiagnosticQueue _localSemanticDiagnostics;
            private readonly SimpleDiagnosticQueue _localSyntaxDiagnostics;
            private readonly SimpleDiagnosticQueue _nonLocalDiagnostics;

            public CategorizedDiagnosticQueue()
            {
                _localSemanticDiagnostics = new SimpleDiagnosticQueue();
                _localSyntaxDiagnostics = new SimpleDiagnosticQueue();
                _nonLocalDiagnostics = new SimpleDiagnosticQueue();
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

            private void EnqueueCore(SimpleDiagnosticQueue lazyDiagnosticsQueue, Diagnostic diagnostic, DiagnosticAnalyzer analyzer)
            {
                lazyDiagnosticsQueue.Enqueue(diagnostic, analyzer);
            }

            public override bool TryComplete()
            {
                return true;
            }

            public override bool TryDequeue(DiagnosticAnalyzer analyzer, out Diagnostic diagnostic)
            {
                return TryDequeueCore(_localSemanticDiagnostics, analyzer, out diagnostic) ||
                    TryDequeueCore(_localSyntaxDiagnostics, analyzer, out diagnostic) ||
                    TryDequeueCore(_nonLocalDiagnostics, analyzer, out diagnostic);
            }

            private static bool TryDequeueCore(SimpleDiagnosticQueue diagnostics, DiagnosticAnalyzer analyzer, out Diagnostic diagnostic)
            {
                return diagnostics.TryDequeue(analyzer, out diagnostic);
            }

            public override ImmutableArray<Diagnostic> DequeueLocalSyntaxDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return DequeueDiagnosticsCore(analyzer, _localSyntaxDiagnostics);
            }

            public override ImmutableArray<Diagnostic> DequeueLocalSemanticDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return DequeueDiagnosticsCore(analyzer, _localSemanticDiagnostics);
            }

            public override ImmutableArray<Diagnostic> DequeueNonLocalDiagnostics(DiagnosticAnalyzer analyzer)
            {
                return DequeueDiagnosticsCore(analyzer, _nonLocalDiagnostics);
            }

            private static ImmutableArray<Diagnostic> DequeueDiagnosticsCore(DiagnosticAnalyzer analyzer, SimpleDiagnosticQueue diagnostics)
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
