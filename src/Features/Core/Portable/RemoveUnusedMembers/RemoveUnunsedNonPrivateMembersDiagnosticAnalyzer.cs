// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.RemoveUnusedMembers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    internal sealed class RemoveUnunsedNonPrivateMembersDiagnosticAnalyzer : DocumentDiagnosticAnalyzer, IBuiltInAnalyzer
    {
        private static readonly FindReferencesSearchOptions s_findReferencesSearchOptions = FindReferencesSearchOptions.Default;

        private static readonly DiagnosticDescriptor s_descriptor = new DiagnosticDescriptor(
            IDEDiagnosticIds.RemoveUnusedNonPrivateMembersDiagnosticId,
            title: new LocalizableResourceString(nameof(FeaturesResources.Remove_unused_member), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            messageFormat: new LocalizableResourceString(nameof(FeaturesResources.Member_0_is_unused), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            DiagnosticCategory.CodeQuality,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            customTags: DiagnosticCustomTags.Unnecessary);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(s_descriptor);

        public DiagnosticAnalyzerCategory GetAnalyzerCategory() => DiagnosticAnalyzerCategory.SemanticDocumentAnalysis;

        public bool OpenFileOnly(Workspace workspace) => true;

        public override async Task<ImmutableArray<Diagnostic>> AnalyzeSemanticsAsync(Document document, CancellationToken cancellationToken)
        {
            var analyzerDriverService = document.GetLanguageService<IAnalyzerDriverService>();
            if (analyzerDriverService == null)
            {
                return ImmutableArray<Diagnostic>.Empty;
            }

            using var disposer = ArrayBuilder<DeclarationInfo>.GetInstance(out var declsBuilder);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            analyzerDriverService.ComputeDeclarationsInSpan(semanticModel, root.Span, true, declsBuilder, cancellationToken);

            if (declsBuilder.Count == 0)
            {
                return ImmutableArray<Diagnostic>.Empty;
            }

            var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var candidateChecker = new UnusedMembersCandidateChecker(compilation, SymbolVisibility.Internal);
            using var tasksDisposer = ArrayBuilder<Task<(ISymbol symbol, bool hasReference)>>.GetInstance(out var tasksBuilder);
            foreach (var decl in declsBuilder)
            {
                var symbol = decl.DeclaredSymbol?.OriginalDefinition;
                if (symbol != null && candidateChecker.IsCandidateSymbol(symbol))
                {
                    tasksBuilder.Add(HasReferencesAsync(symbol, document, cancellationToken));
                }
            }

            var results = await Task.WhenAll(tasksBuilder).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            using var diagnosticsDisposer = ArrayBuilder<Diagnostic>.GetInstance(out var diagnosticsBuilder);
            foreach (var (symbol, hasNoReferences) in results)
            {
                if (hasNoReferences)
                {
                    var diagnostic = Diagnostic.Create(s_descriptor, symbol.Locations[0], symbol.Name);
                    diagnosticsBuilder.Add(diagnostic);
                }
            }

            return diagnosticsBuilder.ToImmutableArrayOrEmpty();

            // Local functions.
            static async Task<(ISymbol symbol, bool hasNoReferences)> HasReferencesAsync(ISymbol symbol, Document document, CancellationToken cancellationToken)
            {
                using var progress = new FindReferencesProgress(cancellationToken);

                try
                {
                    await SymbolFinder.FindReferencesAsync(
                        symbolAndProjectId: SymbolAndProjectId.Create(symbol, document.Project.Id),
                        solution: document.Project.Solution,
                        documents: null,
                        progress: progress,
                        options: s_findReferencesSearchOptions,
                        cancellationToken: progress.LinkedTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                return (symbol, progress.HasNoReferences);
            }
        }

        public override Task<ImmutableArray<Diagnostic>> AnalyzeSyntaxAsync(Document document, CancellationToken cancellationToken)
            => SpecializedTasks.EmptyImmutableArray<Diagnostic>();

        private sealed class FindReferencesProgress : IStreamingFindReferencesProgress, IDisposable
        {
            private readonly CancellationTokenSource _source;
            private bool _referenceFound;
            private bool _isCompleted;

            public FindReferencesProgress(CancellationToken cancellationToken)
            {
                _source = new CancellationTokenSource();
                LinkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_source.Token, cancellationToken);
            }

            public CancellationTokenSource LinkedTokenSource { get; }
            public bool HasNoReferences => _isCompleted && !_referenceFound;

            public Task OnReferenceFoundAsync(SymbolAndProjectId symbol, ReferenceLocation location)
            {
                _referenceFound = true;
                _source.Cancel();
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                _source.Dispose();
                LinkedTokenSource.Dispose();
            }

            public Task OnCompletedAsync()
            {
                _isCompleted = true;
                return Task.CompletedTask;
            }

            public Task ReportProgressAsync(int current, int maximum) => Task.CompletedTask;

            public Task OnStartedAsync() => Task.CompletedTask;
            public Task OnDefinitionFoundAsync(SymbolAndProjectId symbol) => Task.CompletedTask;
            public Task OnFindInDocumentStartedAsync(Document document) => Task.CompletedTask;
            public Task OnFindInDocumentCompletedAsync(Document document) => Task.CompletedTask;
        }
    }
}
