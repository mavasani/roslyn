﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.VisualStudio.LanguageServices.Implementation
{
    internal sealed class AnalyzerDependencyResults
    {
        public static readonly AnalyzerDependencyResults Empty = new AnalyzerDependencyResults(ImmutableArray<AnalyzerDependencyConflict>.Empty, ImmutableArray<MissingAnalyzerDependency>.Empty, ImmutableArray<LoadedAssemblyAnalyzerConflict>.Empty);

        public AnalyzerDependencyResults(ImmutableArray<AnalyzerDependencyConflict> conflicts, ImmutableArray<MissingAnalyzerDependency> missingDependencies, ImmutableArray<LoadedAssemblyAnalyzerConflict> loadedAssemblyAnalyzerConflicts)
        {
            Debug.Assert(conflicts != default(ImmutableArray<AnalyzerDependencyConflict>));
            Debug.Assert(missingDependencies != default(ImmutableArray<MissingAnalyzerDependency>));
            Debug.Assert(loadedAssemblyAnalyzerConflicts != default(ImmutableArray<LoadedAssemblyAnalyzerConflict>));

            Conflicts = conflicts;
            MissingDependencies = missingDependencies;
            LoadedAssemblyAnalyzerConflicts = loadedAssemblyAnalyzerConflicts;
        }

        public ImmutableArray<AnalyzerDependencyConflict> Conflicts { get; }
        public ImmutableArray<MissingAnalyzerDependency> MissingDependencies { get; }
        public ImmutableArray<LoadedAssemblyAnalyzerConflict> LoadedAssemblyAnalyzerConflicts { get; }
    }
}
