// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics.EngineV1
{
    internal partial class DiagnosticIncrementalAnalyzer
    {
        // PERF: Keep track of the current solution crawler analysis state for each project, so that we can reduce memory pressure by disposing off the per-project CompilationWithAnalyzers instances when appropriate.
        private class SolutionCrawlerAnalysisState
        {
            private readonly Dictionary<ProjectId, ProjectAnalysisState> _projectAnalysisStateMap;
            
            public SolutionCrawlerAnalysisState()
            {
                _projectAnalysisStateMap = new Dictionary<ProjectId, ProjectAnalysisState>();
            }

            private class ProjectAnalysisState
            {
                public WeakReference<CompilationWithAnalyzers> CompilationWithAnalyzers { get; set; }
                public VersionArgument VersionArgument { get; set; }
            }

            internal CompilationWithAnalyzers GetOrCreateCompilationWithAnalyzers(Project project, Func<CompilationWithAnalyzers> createCompilationWithAnalyzers, VersionArgument projectVersions)
            {
                CompilationWithAnalyzers compilationWithAnalyzers;
                ProjectAnalysisState projectAnalysisState;

                lock (_projectAnalysisStateMap)
                {
                    if (_projectAnalysisStateMap.TryGetValue(project.Id, out projectAnalysisState) &&
                        CheckSemanticVersions(project, projectAnalysisState.VersionArgument.TextVersion, projectAnalysisState.VersionArgument.DataVersion, projectVersions))
                    {
                        compilationWithAnalyzers = projectAnalysisState.CompilationWithAnalyzers.GetTarget();
                        if (compilationWithAnalyzers == null)
                        {
                            compilationWithAnalyzers = createCompilationWithAnalyzers();
                            projectAnalysisState.CompilationWithAnalyzers.SetTarget(compilationWithAnalyzers);
                        }
                    }
                    else
                    {
                        compilationWithAnalyzers = createCompilationWithAnalyzers();
                        projectAnalysisState = new ProjectAnalysisState
                            {
                                CompilationWithAnalyzers = new WeakReference<CompilationWithAnalyzers>(compilationWithAnalyzers),
                                VersionArgument = projectVersions
                            };

                        _projectAnalysisStateMap[project.Id] = projectAnalysisState;
                    }

                    return compilationWithAnalyzers;
                }
            }

            internal void ResetCompilationWithAnalyzersCache()
            {
                lock (_projectAnalysisStateMap)
                {
                    _projectAnalysisStateMap.Clear();
                }
            }

            internal void ClearProjectAnalysisState(Project project)
            {
                lock (_projectAnalysisStateMap)
                {
                    _projectAnalysisStateMap.Remove(project.Id);
                }
            }
        }
    }
}
