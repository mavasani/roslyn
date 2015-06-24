// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.Diagnostics
{
    /// <summary>
    /// </summary>
    public class HostAnalysisContext
    {
        private readonly Workspace _workspace;
        private readonly Project _project;
        private readonly Document _document;
        private readonly HostAnalysisContextKind _kind;

        internal HostAnalysisContext(Workspace workspace)
            : this(workspace, project: null, document: null, kind: HostAnalysisContextKind.Workspace)
        {
        }

        internal HostAnalysisContext(Project project)
            : this(workspace: project.Solution.Workspace, project: project, document: null, kind: HostAnalysisContextKind.Project)
        {
        }

        internal HostAnalysisContext(Document document)
            : this(workspace: document.Project.Solution.Workspace, project: document.Project, document: document, kind: HostAnalysisContextKind.Document)
        {
        }

        private HostAnalysisContext(Workspace workspace, Project project, Document document, HostAnalysisContextKind kind)
        {
            _workspace = workspace;
            _project = project;
            _document = document;
            _kind = kind;
        }

        public HostAnalysisContextKind Kind => _kind;

        public Workspace Workspace => _workspace;

        public Project Project => _project;

        public Document Document => _document;
    }
}
