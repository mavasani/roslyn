// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.LanguageServices.Implementation.TaskList;
using Microsoft.VisualStudio.Shell.Interop;
using SystemMetadataReader = System.Reflection.Metadata.MetadataReader;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem
{
    internal sealed class VisualStudioAnalyzer : IDisposable
    {
        private readonly string _fullPath;
        private readonly FileChangeTracker _tracker;
        private readonly HostDiagnosticUpdateSource _hostDiagnosticUpdateSource;
        private readonly ProjectId _projectId;
        private readonly Workspace _workspace;
        private readonly IAnalyzerAssemblyLoader _loader;
        private readonly string _language;

        private AnalyzerReference _analyzerReference;
        private List<DiagnosticData> _analyzerLoadDiagnostics;

        public event EventHandler UpdatedOnDisk;

        private readonly DiagnosticDescriptor _analyzerDependencyConflictRule = new DiagnosticDescriptor(
            id: IDEDiagnosticIds.AnalyzerDependencyConflictId,
            title: ServicesVSResources.AnalyzerDependencyConflict,
            messageFormat: ServicesVSResources.Analyzer_assemblies_0_and_1_both_have_identity_2_but_different_contents_Only_one_will_be_loaded_and_analyzers_using_these_assemblies_may_not_run_correctly,
            category: FeaturesResources.Roslyn_HostError,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private readonly DiagnosticDescriptor _loadedAssemblyAnalyzerConflictRule = new DiagnosticDescriptor(
            id: IDEDiagnosticIds.LoadedAssemblyAnalyzerConflictId,
            title: ServicesVSResources.AnalyzerAndLoadedAssemblyConflict,
            messageFormat: ServicesVSResources.Analyzer_assembly_0_cannot_be_loaded_as_another_assembly_with_same_name_but_different_identity_1_has_already_been_loaded_in_the_process_You_may_need_to_restart_the_process_for_analyzers_to_work_correctly,
            category: FeaturesResources.Roslyn_HostError,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public VisualStudioAnalyzer(string fullPath, IVsFileChangeEx fileChangeService, HostDiagnosticUpdateSource hostDiagnosticUpdateSource, ProjectId projectId, Workspace workspace, IAnalyzerAssemblyLoader loader, string language)
        {
            _fullPath = fullPath;
            _tracker = new FileChangeTracker(fileChangeService, fullPath);
            _tracker.UpdatedOnDisk += OnUpdatedOnDisk;
            _tracker.StartFileChangeListeningAsync();
            _tracker.EnsureSubscription();
            _hostDiagnosticUpdateSource = hostDiagnosticUpdateSource;
            _projectId = projectId;
            _workspace = workspace;
            _loader = loader;
            _language = language;
        }

        public string FullPath
        {
            get { return _fullPath; }
        }

        public bool HasLoadErrors
        {
            get { return _analyzerLoadDiagnostics != null && _analyzerLoadDiagnostics.Count > 0; }
        }

        public AnalyzerReference GetReference()
        {
            if (_analyzerReference == null)
            {
                if (File.Exists(_fullPath))
                {
                    _analyzerReference = new AnalyzerFileReference(_fullPath, _loader);
                    ((AnalyzerFileReference)_analyzerReference).AnalyzerAssemblyLoaded += OnAnalyzerAssemblyLoaded;
                    ((AnalyzerFileReference)_analyzerReference).AnalyzerLoadFailed += OnAnalyzerLoadError;
                }
                else
                {
                    _analyzerReference = new UnresolvedAnalyzerReference(_fullPath);
                }
            }

            return _analyzerReference;
        }

        private void AddAnalyzerDiagnostic(DiagnosticData diagnostic)
        {
            _analyzerLoadDiagnostics = _analyzerLoadDiagnostics ?? new List<DiagnosticData>();
            _analyzerLoadDiagnostics.Add(diagnostic);

            _hostDiagnosticUpdateSource.UpdateDiagnosticsForProject(_projectId, this, _analyzerLoadDiagnostics);
        }

        private void OnAnalyzerLoadError(object sender, AnalyzerLoadFailureEventArgs e)
        {
            var data = AnalyzerHelper.CreateAnalyzerLoadFailureDiagnostic(_workspace, _projectId, _language, _fullPath, e);
            AddAnalyzerDiagnostic(data);
        }

        public void Dispose()
        {
            Reset();

            _tracker.Dispose();
            _tracker.UpdatedOnDisk -= OnUpdatedOnDisk;
        }

        public void Reset()
        {
            var analyzerFileReference = _analyzerReference as AnalyzerFileReference;
            if (analyzerFileReference != null)
            {
                analyzerFileReference.AnalyzerAssemblyLoaded -= OnAnalyzerAssemblyLoaded;
                analyzerFileReference.AnalyzerLoadFailed -= OnAnalyzerLoadError;

                if (_analyzerLoadDiagnostics?.Count > 0)
                {
                    _hostDiagnosticUpdateSource.ClearDiagnosticsForProject(_projectId, this);
                }

                _hostDiagnosticUpdateSource.ClearAnalyzerReferenceDiagnostics(analyzerFileReference, _language, _projectId);
            }

            _analyzerLoadDiagnostics = null;
            _analyzerReference = null;
        }

        private void OnUpdatedOnDisk(object sender, EventArgs e)
        {
            UpdatedOnDisk?.Invoke(this, EventArgs.Empty);
        }

        private void OnAnalyzerAssemblyLoaded(object sender, EventArgs e)
        {
            var analyzerReference = (AnalyzerFileReference)sender;
            var loadedAssembly = analyzerReference.GetAssembly();

            AnalyzerInfo analyzerInfo;
            if (loadedAssembly != null && AnalyzerInfo.TryCreate(analyzerReference.FullPath, out analyzerInfo))
            {
                var hasMvidMismatch = false;
                var hasIdentityMismatch = false;

                var loadedIdentity = AssemblyIdentity.FromAssemblyDefinition(loadedAssembly);
                if (loadedIdentity.Equals(analyzerInfo.Identity))
                {
                    hasMvidMismatch = loadedAssembly.ManifestModule.ModuleVersionId != analyzerInfo.MVID;
                }
                else
                {
                    hasIdentityMismatch = true;
                }

                if (!hasMvidMismatch && !hasIdentityMismatch)
                {
                    return;
                }

                var messageArguments = new string[] { analyzerInfo.Identity.ToString(), loadedIdentity.ToString() };
                DiagnosticData diagnostic;
                if (DiagnosticData.TryCreate(_loadedAssemblyAnalyzerConflictRule, messageArguments, _projectId, _workspace, out diagnostic))
                {
                    AddAnalyzerDiagnostic(diagnostic);
                }
            }
        }

        // internal for testing purposes.
        internal sealed class AnalyzerInfo
        {
            private AnalyzerInfo(string filePath, AssemblyIdentity identity, Guid mvid)
            {
                Path = filePath;
                Identity = identity;
                MVID = mvid;
            }

            public string Path { get; }
            public AssemblyIdentity Identity { get; }
            public Guid MVID { get; }

            public static bool TryCreate(string filePath, out AnalyzerInfo analyzerInfo)
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    using (var peReader = new PEReader(stream))
                    {
                        var metadataReader = peReader.GetMetadataReader();

                        Guid mvid = ReadMvid(metadataReader);
                        AssemblyIdentity identity = ReadAssemblyIdentity(metadataReader);
                        analyzerInfo = new AnalyzerInfo(filePath, identity, mvid);
                        return true;
                    }
                }
                catch { }

                analyzerInfo = null;
                return false;
            }

            private static AssemblyIdentity ReadAssemblyIdentity(SystemMetadataReader metadataReader)
            {
                var assemblyDefinition = metadataReader.GetAssemblyDefinition();
                string name = metadataReader.GetString(assemblyDefinition.Name);
                Version version = assemblyDefinition.Version;
                string cultureName = metadataReader.GetString(assemblyDefinition.Culture);
                ImmutableArray<byte> publicKeyOrToken = metadataReader.GetBlobContent(assemblyDefinition.PublicKey);
                AssemblyFlags flags = assemblyDefinition.Flags;
                bool hasPublicKey = (flags & AssemblyFlags.PublicKey) != 0;

                return new AssemblyIdentity(name, version, cultureName, publicKeyOrToken, hasPublicKey: hasPublicKey);
            }

            private static Guid ReadMvid(SystemMetadataReader metadataReader)
            {
                var mvidHandle = metadataReader.GetModuleDefinition().Mvid;
                return metadataReader.GetGuid(mvidHandle);
            }
        }
    }
}
