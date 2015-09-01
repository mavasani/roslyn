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
            private readonly object _gate = new object();
            private ProjectAnalysisState _activeProjectState;
            // private readonly Dictionary<ProjectId, ProjectAnalysisState> _projectAnalysisStateMap;
            
            public SolutionCrawlerAnalysisState()
            {
                //_projectAnalysisStateMap = new Dictionary<ProjectId, ProjectAnalysisState>();
                _activeProjectState = null;
            }

            private class ProjectAnalysisState
            {
                public WeakReference<CompilationWithAnalyzers> CompilationWithAnalyzers { get; set; }
                public VersionArgument VersionArgument { get; set; }
                public ProjectId ProjectId { get; set; }
            }

            internal CompilationWithAnalyzers GetOrCreateCompilationWithAnalyzers(Project project, Func<CompilationWithAnalyzers> createCompilationWithAnalyzers, VersionArgument projectVersions)
            {
                CompilationWithAnalyzers compilationWithAnalyzers;

                lock (_gate)
                {
                    if (_activeProjectState?.ProjectId == project.Id &&
                        CheckSemanticVersions(project, _activeProjectState.VersionArgument.TextVersion, _activeProjectState.VersionArgument.DataVersion, projectVersions))
                    {
                        compilationWithAnalyzers = _activeProjectState.CompilationWithAnalyzers.GetTarget();
                        if (compilationWithAnalyzers == null)
                        {
                            compilationWithAnalyzers = createCompilationWithAnalyzers();
                            _activeProjectState.CompilationWithAnalyzers.SetTarget(compilationWithAnalyzers);
                        }
                    }
                    else
                    {
                        compilationWithAnalyzers = createCompilationWithAnalyzers();
                        _activeProjectState = new ProjectAnalysisState
                            {
                                CompilationWithAnalyzers = new WeakReference<CompilationWithAnalyzers>(compilationWithAnalyzers),
                                VersionArgument = projectVersions,
                                ProjectId = project.Id
                            };
                    }

                    return compilationWithAnalyzers;
                }

                //lock (_projectAnalysisStateMap)
                //{
                //    if (_projectAnalysisStateMap.TryGetValue(project.Id, out projectAnalysisState) &&
                //        CheckSemanticVersions(project, projectAnalysisState.VersionArgument.TextVersion, projectAnalysisState.VersionArgument.DataVersion, projectVersions))
                //    {
                //        compilationWithAnalyzers = projectAnalysisState.CompilationWithAnalyzers; //.GetTarget();
                //        //if (compilationWithAnalyzers == null)
                //        //{
                //        //    compilationWithAnalyzers = createCompilationWithAnalyzers();
                //        //    projectAnalysisState.CompilationWithAnalyzers.SetTarget(compilationWithAnalyzers);
                //        //}
                //    }
                //    else
                //    {
                //        compilationWithAnalyzers = createCompilationWithAnalyzers();
                //        projectAnalysisState = new ProjectAnalysisState
                //            {
                //                CompilationWithAnalyzers = compilationWithAnalyzers, // new WeakReference<CompilationWithAnalyzers>(compilationWithAnalyzers),
                //                VersionArgument = projectVersions
                //            };

                //        _projectAnalysisStateMap[project.Id] = projectAnalysisState;
                //    }

                //    return compilationWithAnalyzers;
                //}
            }

            internal void ClearProjectAnalysisState(Project project)
            {
                lock (_gate)
                {
                    _activeProjectState = null;
                }
            }
        }
    }
}
