﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics.Telemetry;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Workspaces.Diagnostics;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Remote.Diagnostics
{
    internal class DiagnosticComputer
    {
        private readonly Document? _document;
        private readonly Project _project;
        private readonly TextSpan? _span;
        private readonly AnalysisKind? _analysisKind;
        private readonly IPerformanceTrackerService? _performanceTracker;
        private readonly DiagnosticAnalyzerInfoCache _analyzerInfoCache;

        public DiagnosticComputer(
            DocumentId? documentId,
            Project project,
            TextSpan? span,
            AnalysisKind? analysisKind,
            DiagnosticAnalyzerInfoCache analyzerInfoCache)
        {
            _document = documentId != null ? project.GetRequiredDocument(documentId) : null;
            _project = project;
            _span = span;
            _analysisKind = analysisKind;
            _analyzerInfoCache = analyzerInfoCache;

            // We only track performance from primary branch. All forked branch we don't care such as preview.
            _performanceTracker = project.IsFromPrimaryBranch() ? project.Solution.Workspace.Services.GetService<IPerformanceTrackerService>() : null;
        }

        public async Task<DiagnosticAnalysisResultMap<string, DiagnosticAnalysisResultBuilder>> GetDiagnosticsAsync(
            IEnumerable<string> analyzerIds,
            bool reportSuppressedDiagnostics,
            bool logPerformanceInfo,
            bool getTelemetryInfo,
            CancellationToken cancellationToken)
        {
            var analyzerToIdMap = AnalyzerToIdMap.GetOrCreate(_project);
            var analyzers = GetAnalyzers(analyzerToIdMap, analyzerIds);
            if (analyzers.IsEmpty)
            {
                return DiagnosticAnalysisResultMap<string, DiagnosticAnalysisResultBuilder>.Empty;
            }

            var cacheService = _project.Solution.Workspace.Services.GetRequiredService<IProjectCacheService>();
            using var cache = cacheService.EnableCaching(_project.Id);
            var skippedAnalyzersInfo = _project.GetSkippedAnalyzersInfo(_analyzerInfoCache);
            return await AnalyzeAsync(analyzers, analyzerToIdMap, skippedAnalyzersInfo,
                reportSuppressedDiagnostics, logPerformanceInfo, getTelemetryInfo, cancellationToken).ConfigureAwait(false);
        }

        private async Task<DiagnosticAnalysisResultMap<string, DiagnosticAnalysisResultBuilder>> AnalyzeAsync(
            ImmutableArray<DiagnosticAnalyzer> analyzers,
            BidirectionalMap<string, DiagnosticAnalyzer> analyzerToIdMap,
            SkippedHostAnalyzersInfo skippedAnalyzersInfo,
            bool reportSuppressedDiagnostics,
            bool logPerformanceInfo,
            bool getTelemetryInfo,
            CancellationToken cancellationToken)
        {
            var compilationWithAnalyzers = await CreateCompilationWithAnalyzersAsync(analyzers,
                logAnalyzerExecutionTime: logPerformanceInfo || getTelemetryInfo,
                reportSuppressedDiagnostics,
                cancellationToken).ConfigureAwait(false);

            var documentAnalysisScope = _document != null
                ? new DocumentAnalysisScope(_document, _span, analyzers, _analysisKind!.Value)
                : null;

            var (analysisResult, additionalPragmaSuppressionDiagnostics) = await compilationWithAnalyzers.GetAnalysisResultAsync(
                documentAnalysisScope, _project, _analyzerInfoCache, cancellationToken).ConfigureAwait(false);

            // Record performance if tracker is available
            if (logPerformanceInfo && _performanceTracker != null)
            {
                // +1 to include project itself
                var unitCount = documentAnalysisScope != null ? 1 : _project.DocumentIds.Count + 1;
                _performanceTracker.AddSnapshot(analysisResult.AnalyzerTelemetryInfo.ToAnalyzerPerformanceInfo(_analyzerInfoCache), unitCount);
            }

            var builderMap = analysisResult.ToResultBuilderMap(
                additionalPragmaSuppressionDiagnostics, documentAnalysisScope,
                _project, VersionStamp.Default, compilationWithAnalyzers.Compilation,
                analyzers, skippedAnalyzersInfo, reportSuppressedDiagnostics, cancellationToken);

            var result = builderMap.ToImmutableDictionary(kv => GetAnalyzerId(analyzerToIdMap, kv.Key), kv => kv.Value);
            var telemetry = getTelemetryInfo
                ? GetTelemetryInfo(analysisResult, analyzers, analyzerToIdMap)
                : ImmutableDictionary<string, AnalyzerTelemetryInfo>.Empty;
            return DiagnosticAnalysisResultMap.Create(result, telemetry);

            static ImmutableDictionary<string, AnalyzerTelemetryInfo> GetTelemetryInfo(
                AnalysisResult analysisResult,
                ImmutableArray<DiagnosticAnalyzer> analyzers,
                BidirectionalMap<string, DiagnosticAnalyzer> analyzerToIdMap)
            {
                Func<DiagnosticAnalyzer, bool> shouldInclude;
                if (analyzers.Length < analysisResult.AnalyzerTelemetryInfo.Count)
                {
                    // Filter the telemetry info to the executed analyzers.
                    using var _ = PooledHashSet<DiagnosticAnalyzer>.GetInstance(out var analyzersSet);
                    analyzersSet.AddRange(analyzers);

                    shouldInclude = analyzer => analyzersSet.Contains(analyzer);
                }
                else
                {
                    shouldInclude = _ => true;
                }

                var telemetryBuilder = ImmutableDictionary.CreateBuilder<string, AnalyzerTelemetryInfo>();
                foreach (var (analyzer, analyzerTelemetry) in analysisResult.AnalyzerTelemetryInfo)
                {
                    if (shouldInclude(analyzer))
                    {
                        var analyzerId = GetAnalyzerId(analyzerToIdMap, analyzer);
                        telemetryBuilder.Add(analyzerId, analyzerTelemetry);
                    }
                }

                return telemetryBuilder.ToImmutable();
            }
        }

        private static string GetAnalyzerId(BidirectionalMap<string, DiagnosticAnalyzer> analyzerMap, DiagnosticAnalyzer analyzer)
        {
            var analyzerId = analyzerMap.GetKeyOrDefault(analyzer);
            Contract.ThrowIfNull(analyzerId);

            return analyzerId;
        }

        private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(BidirectionalMap<string, DiagnosticAnalyzer> analyzerMap, IEnumerable<string> analyzerIds)
        {
            // TODO: this probably need to be cached as well in analyzer service?
            var builder = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();

            foreach (var analyzerId in analyzerIds)
            {
                if (analyzerMap.TryGetValue(analyzerId, out var analyzer))
                {
                    builder.Add(analyzer);
                }
            }

            return builder.ToImmutable();
        }

        private async Task<CompilationWithAnalyzers> CreateCompilationWithAnalyzersAsync(
            ImmutableArray<DiagnosticAnalyzer> analyzers,
            bool logAnalyzerExecutionTime,
            bool reportSuppressedDiagnostics,
            CancellationToken cancellationToken)
        {
            // Always run analyzers concurrently in OOP
            const bool concurrentAnalysis = true;

            // Get original compilation
            var compilation = await _project.GetRequiredCompilationAsync(cancellationToken).ConfigureAwait(false);

            // Fork compilation with concurrent build. this is okay since WithAnalyzers will fork compilation
            // anyway to attach event queue. This should make compiling compilation concurrent and make things
            // faster
            compilation = compilation.WithOptions(compilation.Options.WithConcurrentBuild(concurrentAnalysis));

            // TODO: can we support analyzerExceptionFilter in remote host? 
            //       right now, host doesn't support watson, we might try to use new NonFatal watson API?
            var analyzerOptions = new CompilationWithAnalyzersOptions(
                options: new WorkspaceAnalyzerOptions(_project.AnalyzerOptions, _project.Solution),
                onAnalyzerException: null,
                analyzerExceptionFilter: null,
                concurrentAnalysis: concurrentAnalysis,
                logAnalyzerExecutionTime: logAnalyzerExecutionTime,
                reportSuppressedDiagnostics: reportSuppressedDiagnostics);
            return compilation.WithAnalyzers(analyzers, analyzerOptions);
        }

        private sealed class AnalyzerToIdMap
        {
            /// <summary>
            /// Cache of <see cref="CompilationWithAnalyzersOptions"/> and a map from analyzer IDs to <see cref="DiagnosticAnalyzer"/>s
            /// for all analyzers for each project.
            /// </summary>
            private static readonly ConditionalWeakTable<Project, BidirectionalMap<string, DiagnosticAnalyzer>> s_cache
                = new ConditionalWeakTable<Project, BidirectionalMap<string, DiagnosticAnalyzer>>();

            public static BidirectionalMap<string, DiagnosticAnalyzer> GetOrCreate(Project project)
            {
                if (s_cache.TryGetValue(project, out var data))
                {
                    return data;
                }

                data = Create(project);
                return s_cache.GetValue(project, _ => data);
            }

            private static BidirectionalMap<string, DiagnosticAnalyzer> Create(Project project)
            {
                // We could consider creating a service so that we don't do this repeatedly if this shows up as perf cost
                using var pooledObject = SharedPools.Default<HashSet<object>>().GetPooledObject();
                using var pooledMap = SharedPools.Default<Dictionary<string, DiagnosticAnalyzer>>().GetPooledObject();
                var referenceSet = pooledObject.Object;
                var analyzerMapBuilder = pooledMap.Object;

                // This follows what we do in DiagnosticAnalyzerInfoCache.CheckAnalyzerReferenceIdentity
                foreach (var reference in project.Solution.AnalyzerReferences.Concat(project.AnalyzerReferences))
                {
                    if (!referenceSet.Add(reference.Id))
                    {
                        continue;
                    }

                    var analyzers = reference.GetAnalyzers(project.Language);
                    analyzerMapBuilder.AppendAnalyzerMap(analyzers);
                }

                return new BidirectionalMap<string, DiagnosticAnalyzer>(analyzerMapBuilder);
            }
        }
    }
}
