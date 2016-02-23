﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices.Implementation.Extensions;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using Roslyn.Utilities;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.CobyIntegration
{
    internal class CobyWorkspace : Workspace
    {
        private static CobyWorkspace s_instance = null;

        private readonly object _gate = new object();
        private readonly Dictionary<string, Data> _map = new Dictionary<string, Data>();

        // Coby Service doesnt have concept of project. just create one ourselves.
        private readonly ProjectId _primaryCSharpProjectId = ProjectId.CreateNewId("primaryCSharpProjectId");
        private readonly ProjectId _primaryVisualBasicProjectId = ProjectId.CreateNewId("primaryVisualBasicProjectId");

        private readonly IServiceProvider _serviceProvider;
        private readonly IVsEditorAdaptersFactoryService _editorAdaptersFactory;

        private static readonly string _cobyTempPath = Path.Combine(Path.GetTempPath(), "Coby");

        public static void Create(HostServices host, IServiceProvider serviceProvider)
        {
            s_instance = new CobyWorkspace(host, serviceProvider);
        }

        public static CobyWorkspace Instance => s_instance;

        private CobyWorkspace(HostServices host, IServiceProvider serviceProvider) : base(host, "CobyWorkspace")
        {
            // REVIEW: need to change default host to contain services I want that are specific to Coby

            _serviceProvider = serviceProvider;

            var componentModel = _serviceProvider.GetService<SComponentModel, IComponentModel>();
            _editorAdaptersFactory = componentModel.GetService<IVsEditorAdaptersFactoryService>();

            // REVIEW: Coby Doesnt have concept of Projects. it already flattened every to entity abtraction.
            // add pirmary project to hold all coby documents.
            // 
            // also, since we can't determine whether a symbol is from csharp or vb without actually getting whole file, this jsut drop support for VB and 
            // consider everything as CSharp. this means, VB will simply not work.
            this.OnProjectAdded(ProjectInfo.Create(_primaryCSharpProjectId, VersionStamp.Create(), "MiscellaneousCSharpNugetFilesProject", "MiscellaneousCSharpNugetFilesProject", LanguageNames.CSharp));
            this.OnProjectAdded(ProjectInfo.Create(_primaryVisualBasicProjectId, VersionStamp.Create(), "MiscellaneousVisualBasicNugetFilesProject", "MiscellaneousVisualBasicNugetFilesProject", LanguageNames.VisualBasic));
        }

        public override bool CanApplyChange(ApplyChangesKind feature) => false;
        public override bool CanOpenDocuments => true;
        internal override bool CanChangeActiveContextDocument => false;

        public Document GetOrCreateDocument(CobyServices.SourceResponse sourceResponse)
        {
            if (sourceResponse == null)
            {
                return null;
            }

            return GetOrCreateDocument(sourceResponse.compoundUrl,
                sourceResponse.compoundUrl.filePath,
                ct => Task.FromResult(SourceText.From(sourceResponse.contents ?? string.Empty)));
        }

        public Document GetOrCreateDocument(CobyServices.SearchResponse searchResponse)
        {
            if (searchResponse == null)
            {
                return null;
            }

            var url = searchResponse.compoundUrl;
            var name = url.filePath ?? searchResponse.fileName ?? searchResponse.name;
            Func<CancellationToken, Task<SourceText>> getSourceTextTask = (CancellationToken ct) => Task.Run(async () =>
            {
                var sourceResult = await CobyServices.GetContentByFileIdAsync(Consts.Repo, searchResponse.id, ct).ConfigureAwait(false);
                return SourceText.From(sourceResult?.contents ?? string.Empty);
            });

            return GetOrCreateDocument(searchResponse.compoundUrl, name, getSourceTextTask);
        }

        private Document GetOrCreateDocument(CobyServices.CompoundUrl url, string name, Func<CancellationToken, Task<SourceText>> getSourceTextTask)
        {
            lock (_gate)
            {
                var filePath = GetFilePath(url);

                // REVIEW: in this prototype, file ever created never goes away. need to think about lifetime management.
                //         currently, best way will be making cobyworkspace not singleton but something one create when needed and let it go once no longer needed for a feature.
                Data data;
                if (_map.TryGetValue(filePath, out data))
                {
                    return CurrentSolution.GetDocument(data.Id);
                }

                // File name returned by coby search contains the full relative path in the repo, we want just the file name.
                name = PathUtilities.GetFileName(name);

                var projectId = CobyServices.IsVisualBasicProject(url) ? _primaryVisualBasicProjectId : _primaryCSharpProjectId;
                var id = DocumentId.CreateNewId(projectId, url.filePath);
                var loader = new CobyTextLoader(getSourceTextTask);
                OnDocumentAdded(DocumentInfo.Create(id, name, loader: loader, filePath: filePath, isGenerated: true));

                _map.Add(filePath, new Data(id, url));

                return CurrentSolution.GetDocument(id);
            }
        }

        public bool Contains(string moniker)
        {
            // REVIEW: really bad way to check.
            return moniker?.StartsWith(Path.Combine(Path.GetTempPath(), "Coby", Consts.Repo)) == true;
        }

        public bool TryCloseDocument(string moniker)
        {
            if (Contains(moniker))
            {
                lock (_gate)
                {
                    Data data;
                    if (!_map.TryGetValue(moniker, out data))
                    {
                        // REVIEW: how?
                        return false;
                    }

                    if (!IsDocumentOpen(data.Id))
                    {
                        // REVIEW; How?
                        return false;
                    }

                    // REIVEW: currently we never delete document once opened in the VS.
                    var document = CurrentSolution.GetDocument(data.Id);

                    // REVIEW: oh, bad.
                    var text = document.GetTextAsync().WaitAndGetResult(CancellationToken.None);
                    var version = document.GetTextVersionAsync().WaitAndGetResult(CancellationToken.None);

                    OnDocumentClosed(data.Id, TextLoader.From(TextAndVersion.Create(text, version, document.FilePath)));
                    return true;
                }
            }

            return false;
        }

        internal string GetDisplayPath(Document document)
        {
            return document.FilePath.Substring(_cobyTempPath.Length);
        }

        private string GetFilePath(CobyServices.CompoundUrl url)
        {
            var repo = url.repository.Replace('~', PathUtilities.DirectorySeparatorChar);
            var filePath = url.filePath.Replace('/', PathUtilities.DirectorySeparatorChar);
            return Path.Combine(_cobyTempPath, repo, filePath);
        }

        public static CobyServices.CompoundUrl GetCompoundUrl(Document document)
        {
            var workspace = document.Project.Solution.Workspace as CobyWorkspace;

            Data data;
            if (workspace._map.TryGetValue(document.FilePath, out data))
            {
                return data.Url;
            }

            return default(CobyServices.CompoundUrl);
        }

        public override void OpenDocument(DocumentId documentId, bool activate = true)
        {
            base.OpenDocument(documentId, activate);

            // REVIEW: there should be a way to open document without saving it to disk first. but I am not going to figure that out now.
            // save content to disk.
            var document = CurrentSolution.GetDocument(documentId);

            // REVIEW: this is bad.
            EnsureFileOnDisk(document);

            var vsRunningDocumentTable4 = _serviceProvider.GetService<SVsRunningDocumentTable, IVsRunningDocumentTable4>();
            var fileAlreadyOpen = vsRunningDocumentTable4.IsMonikerValid(document.FilePath);

            var openDocumentService = _serviceProvider.GetService<SVsUIShellOpenDocument, IVsUIShellOpenDocument>();

            IVsUIHierarchy hierarchy;
            uint itemId;
            IOleServiceProvider localServiceProvider;
            IVsWindowFrame windowFrame;
            openDocumentService.OpenDocumentViaProject(document.FilePath, VSConstants.LOGVIEWID.TextView_guid, out localServiceProvider, out hierarchy, out itemId, out windowFrame);

            var documentCookie = vsRunningDocumentTable4.GetDocumentCookie(document.FilePath);

            var vsTextBuffer = (IVsTextBuffer)vsRunningDocumentTable4.GetDocumentData(documentCookie);
            var textBuffer = _editorAdaptersFactory.GetDataBuffer(vsTextBuffer);

            if (!fileAlreadyOpen)
            {
                ErrorHandler.ThrowOnFailure(windowFrame.SetProperty((int)__VSFPROPID5.VSFPROPID_IsProvisional, true));
                ErrorHandler.ThrowOnFailure(windowFrame.SetProperty((int)__VSFPROPID5.VSFPROPID_OverrideCaption, document.Name));
                ErrorHandler.ThrowOnFailure(windowFrame.SetProperty((int)__VSFPROPID5.VSFPROPID_OverrideToolTip, document.Name));
            }

            windowFrame.Show();

            if (!fileAlreadyOpen)
            {
                OnDocumentOpened(documentId, textBuffer.AsTextContainer());
            }
        }

        private static void EnsureFileOnDisk(Document document)
        {
            if (File.Exists(document.FilePath))
            {
                return;
            }

            var directoryToCreate = Path.GetDirectoryName(document.FilePath);
            Directory.CreateDirectory(directoryToCreate);

            // REVIEW: bad, bad, bad
            File.WriteAllText(document.FilePath, document.State.GetText(CancellationToken.None).ToString());

            new FileInfo(document.FilePath).IsReadOnly = true;
        }

        private class Data
        {
            public DocumentId Id { get; }
            public CobyServices.CompoundUrl Url { get; }

            public Data(DocumentId id, CobyServices.CompoundUrl url)
            {
                Id = id;
                Url = url;
            }
        }

        private class CobyTextLoader : TextLoader
        {
            private readonly Func<CancellationToken, Task<SourceText>> _getSourceTextTask;
            
            public CobyTextLoader(Func<CancellationToken, Task<SourceText>> getSourceTextTask)
            {
                _getSourceTextTask = getSourceTextTask;
            }

            public override async Task<TextAndVersion> LoadTextAndVersionAsync(Workspace workspace, DocumentId documentId, CancellationToken cancellationToken)
            {
                // REVIEW: no concept of versioning in coby.
                var version = VersionStamp.Create();

                var task = _getSourceTextTask(cancellationToken);
                var sourceText = await task.ConfigureAwait(false);
                return TextAndVersion.Create(sourceText, version);
            }
        }
    }
}
