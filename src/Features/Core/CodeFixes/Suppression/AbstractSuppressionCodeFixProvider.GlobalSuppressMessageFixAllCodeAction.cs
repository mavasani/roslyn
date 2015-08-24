// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal abstract partial class AbstractSuppressionCodeFixProvider : ISuppressionFixProvider
    {
        internal sealed class GlobalSuppressMessageFixAllCodeAction : AbstractGlobalSuppressMessageCodeAction
        {
            private readonly ImmutableDictionary<ISymbol, ImmutableArray<Diagnostic>> _diagnosticsBySymbol;
            
            private GlobalSuppressMessageFixAllCodeAction(AbstractSuppressionCodeFixProvider fixer, ImmutableDictionary<ISymbol, ImmutableArray<Diagnostic>> diagnosticsMap, string workflowState, Project project)
                : base(fixer, workflowState, project)
            {
                _diagnosticsBySymbol = diagnosticsMap;
            }

            internal static async Task<Solution> CreateChangedSolutionAsync(AbstractSuppressionCodeFixProvider fixer, Document triggerDocument, ImmutableDictionary<Document, ImmutableArray<Diagnostic>> diagnosticsByDocument, string workflowState, CancellationToken cancellationToken)
            {
                var currentSolution = triggerDocument.Project.Solution;
                foreach (var grouping in diagnosticsByDocument.GroupBy(d => d.Key.Project))
                {
                    var oldProject = grouping.Key;
                    var diagnosticsMap = await CreateDiagnosticsMapAsync(fixer, triggerDocument, diagnosticsByDocument, workflowState, cancellationToken).ConfigureAwait(false);
                    var projectCodeAction = new GlobalSuppressMessageFixAllCodeAction(fixer, diagnosticsMap, workflowState, oldProject);
                    var newDocument = await projectCodeAction.GetChangedSuppressionDocumentAsync(cancellationToken).ConfigureAwait(false);
                    var newText = await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);

                    var currentProject = currentSolution.GetProject(oldProject.Id);
                    var newProject = currentProject.ContainsDocument(newDocument.Id) ?
                        currentProject.GetDocument(newDocument.Id).WithText(newText).Project :
                        currentProject.AddDocument(newDocument.Name, newText, newDocument.Folders, newDocument.FilePath).Project;

                    currentSolution = newProject.Solution;
                }

                return currentSolution;
            }

            internal static async Task<Solution> CreateChangedSolutionAsync(AbstractSuppressionCodeFixProvider fixer, Project triggerProject, ImmutableDictionary<Project, ImmutableArray<Diagnostic>> diagnosticsByProject, string workflowState, CancellationToken cancellationToken)
            {
                var currentSolution = triggerProject.Solution;
                foreach (var kvp in diagnosticsByProject)
                {
                    var oldProject = kvp.Key;
                    var diagnosticsMap = await CreateDiagnosticsMapAsync(fixer, oldProject, kvp.Value, workflowState, cancellationToken).ConfigureAwait(false);
                    var projectCodeAction = new GlobalSuppressMessageFixAllCodeAction(fixer, diagnosticsMap, workflowState, oldProject);
                    var newDocument = await projectCodeAction.GetChangedSuppressionDocumentAsync(cancellationToken).ConfigureAwait(false);
                    var newText = await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);

                    var currentProject = currentSolution.GetProject(oldProject.Id);
                    var newProject = currentProject.ContainsDocument(newDocument.Id) ?
                        currentProject.GetDocument(newDocument.Id).WithText(newText).Project :
                        currentProject.AddDocument(newDocument.Name, newText, newDocument.Folders, newDocument.FilePath).Project;

                    currentSolution = newProject.Solution;
                }

                return currentSolution;
            }

            // Equivalence key is not meaningful for FixAll code action.
            protected override string DiagnosticIdForEquivalenceKey => string.Empty;

            protected override async Task<Document> GetChangedSuppressionDocumentAsync(CancellationToken cancellationToken)
            {
                var suppressionsDoc = await GetOrCreateSuppressionsDocumentAsync(cancellationToken).ConfigureAwait(false);
                var suppressionsRoot = await suppressionsDoc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await suppressionsDoc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var defineAttribute = semanticModel.Compilation.Assembly.GetTypeByMetadataName(DiagnosticTriageAttributeFullName) == null;

                foreach (var kvp in _diagnosticsBySymbol)
                {
                    var targetSymbol = kvp.Key;
                    var diagnostics = kvp.Value;

                    foreach (var diagnostic in diagnostics)
                    {
                        suppressionsRoot = Fixer.AddGlobalSuppressMessageAttribute(suppressionsRoot, targetSymbol, diagnostic, WorkflowState, defineAttribute);
                        defineAttribute = false;
                    }
                }

                return suppressionsDoc.WithSyntaxRoot(suppressionsRoot);
            }

            private static async Task<ImmutableDictionary<ISymbol, ImmutableArray<Diagnostic>>> CreateDiagnosticsMapAsync(AbstractSuppressionCodeFixProvider fixer, Document triggerDocument, IEnumerable<KeyValuePair<Document, ImmutableArray<Diagnostic>>> diagnosticsByDocument, string workflowState, CancellationToken cancellationToken)
            {
                var diagnosticsMapBuilder = ImmutableDictionary.CreateBuilder<ISymbol, List<Diagnostic>>();
                foreach (var kvp in diagnosticsByDocument)
                {
                    var document = kvp.Key;
                    var diagnostics = kvp.Value;
                    foreach (var diagnostic in diagnostics)
                    {
                        Contract.ThrowIfFalse(diagnostic.Location.IsInSource);
                        if (diagnostic.WorkflowState != workflowState)
                        {
                            var suppressionTargetInfo = await fixer.GetSuppressionTargetInfoAsync(document, diagnostic.Location.SourceSpan, cancellationToken).ConfigureAwait(false);
                            if (suppressionTargetInfo != null)
                            {
                                var targetSymbol = suppressionTargetInfo.TargetSymbol;
                                Contract.ThrowIfNull(targetSymbol);
                                AddDiagnosticsForSymbol(targetSymbol, diagnostic, diagnosticsMapBuilder);
                            }
                        }
                    }
                }

                return CreateDiagnosticsMap(diagnosticsMapBuilder);
            }

            private static async Task<ImmutableDictionary<ISymbol, ImmutableArray<Diagnostic>>> CreateDiagnosticsMapAsync(AbstractSuppressionCodeFixProvider fixer, Project project, ImmutableArray<Diagnostic> diagnostics, string workflowState, CancellationToken cancellationToken)
            {
                var diagnosticsMapBuilder = ImmutableDictionary.CreateBuilder<ISymbol, List<Diagnostic>>();
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation != null)
                {
                    foreach (var diagnostic in diagnostics)
                    {
                        Contract.ThrowIfFalse(!diagnostic.Location.IsInSource);
                        if (diagnostic.WorkflowState != workflowState)
                        {
                            var targetSymbol = compilation.Assembly;
                            AddDiagnosticsForSymbol(targetSymbol, diagnostic, diagnosticsMapBuilder);
                        }
                    }
                }

                return CreateDiagnosticsMap(diagnosticsMapBuilder);
            }

            private static void AddDiagnosticsForSymbol(ISymbol targetSymbol, Diagnostic diagnostic, ImmutableDictionary<ISymbol, List<Diagnostic>>.Builder diagnosticsMapBuilder)
            {
                List<Diagnostic> diagnosticsForSymbol;
                if (!diagnosticsMapBuilder.TryGetValue(targetSymbol, out diagnosticsForSymbol))
                {
                    diagnosticsForSymbol = new List<Diagnostic>();
                    diagnosticsMapBuilder.Add(targetSymbol, diagnosticsForSymbol);
                }

                diagnosticsForSymbol.Add(diagnostic);
            }

            private static ImmutableDictionary<ISymbol, ImmutableArray<Diagnostic>> CreateDiagnosticsMap(ImmutableDictionary<ISymbol, List<Diagnostic>>.Builder diagnosticsMapBuilder)
            {
                if (diagnosticsMapBuilder.Count == 0)
                {
                    return ImmutableDictionary<ISymbol, ImmutableArray<Diagnostic>>.Empty;
                }

                var builder = ImmutableDictionary.CreateBuilder<ISymbol, ImmutableArray<Diagnostic>>();
                foreach (var kvp in diagnosticsMapBuilder)
                {
                    builder.Add(kvp.Key, GetUniqueDiagnostics(kvp.Value));
                }

                return builder.ToImmutable();
            }

            private static ImmutableArray<Diagnostic> GetUniqueDiagnostics(List<Diagnostic> diagnostics)
            {
                var uniqueIds = new HashSet<string>();
                var uniqueDiagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
                foreach (var diagnostic in diagnostics)
                {
                    if (uniqueIds.Add(diagnostic.Id))
                    {
                        uniqueDiagnostics.Add(diagnostic);
                    }
                }

                return uniqueDiagnostics.ToImmutable();
            }
        }
    }
}
