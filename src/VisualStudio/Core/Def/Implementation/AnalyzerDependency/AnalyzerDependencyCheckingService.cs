// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem;
using Microsoft.VisualStudio.LanguageServices.Implementation.TaskList;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation
{
    [Export(typeof(AnalyzerDependencyCheckingService))]
    internal sealed class AnalyzerDependencyCheckingService
    {
        private static readonly object s_dependencyConflictErrorId = new object();
        
        private readonly VisualStudioWorkspaceImpl _workspace;
        private readonly HostDiagnosticUpdateSource _updateSource;
        
        private readonly DiagnosticDescriptor _missingAnalyzerReferenceRule = new DiagnosticDescriptor(
            id: IDEDiagnosticIds.MissingAnalyzerReferenceId,
            title: ServicesVSResources.MissingAnalyzerReference,
            messageFormat: ServicesVSResources.Analyzer_assembly_0_depends_on_1_but_it_was_not_found_Analyzers_may_not_run_correctly_unless_the_missing_assembly_is_added_as_an_analyzer_reference_as_well,
            category: FeaturesResources.Roslyn_HostError,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private readonly DiagnosticDescriptor _analyzerDependencyConflictRule = new DiagnosticDescriptor(
            id: IDEDiagnosticIds.AnalyzerDependencyConflictId,
            title: ServicesVSResources.AnalyzerDependencyConflict,
            messageFormat: ServicesVSResources.Analyzer_assemblies_0_and_1_both_have_identity_2_but_different_contents_Only_one_will_be_loaded_and_analyzers_using_these_assemblies_may_not_run_correctly,
            category: FeaturesResources.Roslyn_HostError,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private readonly Dictionary<AnalyzerFileReference, List<ProjectId>> _analyzerReferenceToProjectsMap;

        [ImportingConstructor]
        public AnalyzerDependencyCheckingService(
            VisualStudioWorkspaceImpl workspace,
            HostDiagnosticUpdateSource updateSource)
        {
            _workspace = workspace;
            _updateSource = updateSource;
            _analyzerReferenceToProjectsMap = new Dictionary<AnalyzerFileReference, List<ProjectId>>();
        }

        
    }
}
