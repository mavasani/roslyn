﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics.Log;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics.EngineV1
{
    internal class DiagnosticAnalyzerDriver
    {
        private readonly Document _document;

        // The root of the document.  May be null for documents without a root.
        private readonly SyntaxNode _root;

        // The span of the documents we want diagnostics for.  If null, then we want diagnostics 
        // for the entire file.
        private readonly TextSpan? _span;
        private readonly Project _project;
        private readonly DiagnosticIncrementalAnalyzer _owner;
        private readonly CancellationToken _cancellationToken;
        private readonly Action<Exception, DiagnosticAnalyzer, Diagnostic> _onAnalyzerException;
        private readonly AnalyzerOptions _analyzerOptions;

        public DiagnosticAnalyzerDriver(
            Document document,
            TextSpan? span,
            SyntaxNode root,
            DiagnosticIncrementalAnalyzer owner,
            CancellationToken cancellationToken)
            : this(document.Project, owner, cancellationToken)
        {
            _document = document;
            _span = span;
            _root = root;
        }

        public DiagnosticAnalyzerDriver(
            Project project,
            DiagnosticIncrementalAnalyzer owner,
            CancellationToken cancellationToken)
        {
            _project = project;
            _owner = owner;
            _cancellationToken = cancellationToken;
            _analyzerOptions = new WorkspaceAnalyzerOptions(project.AnalyzerOptions, project.Solution.Workspace);
            _onAnalyzerException = owner.GetOnAnalyzerException(project.Id);
        }

        public Document Document
        {
            get
            {
                Contract.ThrowIfNull(_document);
                return _document;
            }
        }

        public TextSpan? Span
        {
            get
            {
                Contract.ThrowIfNull(_document);
                return _span;
            }
        }

        public Project Project
        {
            get
            {
                Contract.ThrowIfNull(_project);
                return _project;
            }
        }

        public CancellationToken CancellationToken
        {
            get
            {
                return _cancellationToken;
            }
        }

        private CompilationWithAnalyzers GetCompilationWithAnalyzers(Compilation compilation)
        {
            return _owner.HostAnalyzerManager.GetOrCreateCompilationWithAnalyzers(_project, p =>
                new CompilationWithAnalyzers(compilation, _owner.GetAnalyzers(p).ToImmutableArray().Distinct(), _analyzerOptions, _onAnalyzerException, allowConcurrentAnalysis: false, logAnalyzerExecutionTime: false));
        }

        public async Task<ImmutableArray<Diagnostic>> GetSyntaxDiagnosticsAsync(DiagnosticAnalyzer analyzer)
        {
            var compilation = _document.Project.SupportsCompilation ? await _document.Project.GetCompilationAsync(_cancellationToken).ConfigureAwait(false) : null;

            Contract.ThrowIfNull(_document);

            using (var pooledObject = SharedPools.Default<List<Diagnostic>>().GetPooledObject())
            {
                var diagnostics = pooledObject.Object;

                _cancellationToken.ThrowIfCancellationRequested();
                var documentAnalyzer = analyzer as DocumentDiagnosticAnalyzer;
                if (documentAnalyzer != null)
                {
                    try
                    {
                        await documentAnalyzer.AnalyzeSyntaxAsync(_document, diagnostics.Add, _cancellationToken).ConfigureAwait(false);
                        return diagnostics.ToImmutableArrayOrEmpty();
                    }
                    catch (Exception e) when (!IsCanceled(e, _cancellationToken))
                    {
                        OnAnalyzerException(e, analyzer, compilation);
                        return ImmutableArray<Diagnostic>.Empty;
                    }
                }

                if (_document.SupportsSyntaxTree)
                {
                    var compilationWithAnalyzers = GetCompilationWithAnalyzers(compilation);
                    var syntaxDiagnostics = await compilationWithAnalyzers.GetAnalyzerSyntaxDiagnosticsAsync(_root.SyntaxTree, ImmutableArray.Create(analyzer), _cancellationToken).ConfigureAwait(false);
                    diagnostics.AddRange(syntaxDiagnostics);
                    await UpdateAnalyzerTelemetryDataAsync(analyzer, compilationWithAnalyzers).ConfigureAwait(false);
                }

                if (diagnostics.Count == 0)
                {
                    return ImmutableArray<Diagnostic>.Empty;
                }

                return GetFilteredDocumentDiagnostics(diagnostics, compilation).ToImmutableArray();
            }
        }

        private IEnumerable<Diagnostic> GetFilteredDocumentDiagnostics(IEnumerable<Diagnostic> diagnostics, Compilation compilation)
        {
            if (_root == null)
            {
                return diagnostics;
            }

            return GetFilteredDocumentDiagnosticsCore(diagnostics, compilation);
        }

        private IEnumerable<Diagnostic> GetFilteredDocumentDiagnosticsCore(IEnumerable<Diagnostic> diagnostics, Compilation compilation)
        {
            var diagsFilteredByLocation = diagnostics.Where(diagnostic => (diagnostic.Location == Location.None) ||
                        (diagnostic.Location.SourceTree == _root.SyntaxTree &&
                         (_span == null || diagnostic.Location.SourceSpan.IntersectsWith(_span.Value))));

            return compilation == null
                ? diagnostics
                : CompilationWithAnalyzers.GetEffectiveDiagnostics(diagsFilteredByLocation, compilation);
        }

        internal void OnAnalyzerException(Exception ex, DiagnosticAnalyzer analyzer, Compilation compilation)
        {
            var exceptionDiagnostic = CompilationWithAnalyzers.CreateAnalyzerExceptionDiagnostic(analyzer, ex);

            if (compilation != null)
            {
                exceptionDiagnostic = CompilationWithAnalyzers.GetEffectiveDiagnostics(ImmutableArray.Create(exceptionDiagnostic), compilation).SingleOrDefault();
            }

            _onAnalyzerException(ex, analyzer, exceptionDiagnostic);
        }

        public async Task<ImmutableArray<Diagnostic>> GetSemanticDiagnosticsAsync(DiagnosticAnalyzer analyzer)
        {
            var model = await _document.GetSemanticModelAsync(_cancellationToken).ConfigureAwait(false);
            var compilation = model?.Compilation;

            Contract.ThrowIfNull(_document);

            using (var pooledObject = SharedPools.Default<List<Diagnostic>>().GetPooledObject())
            {
                var diagnostics = pooledObject.Object;

                _cancellationToken.ThrowIfCancellationRequested();

                var documentAnalyzer = analyzer as DocumentDiagnosticAnalyzer;
                if (documentAnalyzer != null)
                {
                    try
                    {
                        await documentAnalyzer.AnalyzeSemanticsAsync(_document, diagnostics.Add, _cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (!IsCanceled(e, _cancellationToken))
                    {
                        OnAnalyzerException(e, analyzer, compilation);
                        return ImmutableArray<Diagnostic>.Empty;
                    }
                }
                else
                {
                    var compilationWithAnalyzers = GetCompilationWithAnalyzers(compilation);
                    var semanticDiagnostics = await compilationWithAnalyzers.GetAnalyzerSemanticDiagnosticsAsync(model, _span, ImmutableArray.Create(analyzer), _cancellationToken).ConfigureAwait(false);
                    diagnostics.AddRange(semanticDiagnostics);
                    await UpdateAnalyzerTelemetryDataAsync(analyzer, compilationWithAnalyzers).ConfigureAwait(false);
                }

                return GetFilteredDocumentDiagnostics(diagnostics, compilation).ToImmutableArray();
            }
        }

        public async Task<ImmutableArray<Diagnostic>> GetProjectDiagnosticsAsync(DiagnosticAnalyzer analyzer)
        {
            Contract.ThrowIfNull(_project);
            Contract.ThrowIfFalse(_document == null);

            using (var diagnostics = SharedPools.Default<List<Diagnostic>>().GetPooledObject())
            {
                if (_project.SupportsCompilation)
                {
                    await this.GetCompilationDiagnosticsAsync(analyzer, diagnostics.Object).ConfigureAwait(false);
                }

                await this.GetProjectDiagnosticsWorkerAsync(analyzer, diagnostics.Object).ConfigureAwait(false);

                return diagnostics.Object.ToImmutableArray();
            }
        }

        private async Task GetProjectDiagnosticsWorkerAsync(DiagnosticAnalyzer analyzer, List<Diagnostic> diagnostics)
        {
            var projectAnalyzer = analyzer as ProjectDiagnosticAnalyzer;
            if (projectAnalyzer == null)
            {
                return;
            }

            try
            {
                await projectAnalyzer.AnalyzeProjectAsync(_project, diagnostics.Add, _cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!IsCanceled(e, _cancellationToken))
            {
                var compilation = await _project.GetCompilationAsync(_cancellationToken).ConfigureAwait(false);
                OnAnalyzerException(e, analyzer, compilation);
            }
        }

        private async Task GetCompilationDiagnosticsAsync(DiagnosticAnalyzer analyzer, List<Diagnostic> diagnostics)
        {
            Contract.ThrowIfFalse(_project.SupportsCompilation);

            using (var pooledObject = SharedPools.Default<List<Diagnostic>>().GetPooledObject())
            {
                var localDiagnostics = pooledObject.Object;
                var compilation = await _project.GetCompilationAsync(_cancellationToken).ConfigureAwait(false);

                var compilationWithAnalyzers = GetCompilationWithAnalyzers(compilation);
                var compilationDiagnostics = await compilationWithAnalyzers.GetAnalyzerCompilationDiagnosticsAsync(ImmutableArray.Create(analyzer), _cancellationToken).ConfigureAwait(false);
                localDiagnostics.AddRange(compilationDiagnostics);
                await UpdateAnalyzerTelemetryDataAsync(analyzer, compilationWithAnalyzers).ConfigureAwait(false);

                var filteredDiagnostics = CompilationWithAnalyzers.GetEffectiveDiagnostics(localDiagnostics, compilation);
                diagnostics.AddRange(filteredDiagnostics);
            }
        }

        private async Task UpdateAnalyzerTelemetryDataAsync(DiagnosticAnalyzer analyzer, CompilationWithAnalyzers compilationWithAnalyzers)
        {
            var actionCounts = await compilationWithAnalyzers.GetAnalyzerActionCountsAsync(analyzer, _cancellationToken).ConfigureAwait(false);
            DiagnosticAnalyzerLogger.UpdateAnalyzerTypeCount(analyzer, actionCounts, _project, _owner.DiagnosticLogAggregator);
        }

        private static bool IsCanceled(Exception ex, CancellationToken cancellationToken)
        {
            return (ex as OperationCanceledException)?.CancellationToken == cancellationToken;
        }
    }
}
