﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.VisualStudio.LanguageServices.Implementation.TaskList;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem.CPS
{
    internal sealed partial class CPSProject : AbstractProject
    {
        private bool _designTimeBuildStatus;

        public CPSProject(
            VisualStudioProjectTracker projectTracker,
            Func<ProjectId, IVsReportExternalErrors> reportExternalErrorCreatorOpt,
            string projectDisplayName,
            string projectFilePath,
            IVsHierarchy hierarchy,
            string language,
            IServiceProvider serviceProvider,
            VisualStudioWorkspaceImpl visualStudioWorkspaceOpt,
            HostDiagnosticUpdateSource hostDiagnosticUpdateSourceOpt,
            ICommandLineParserService commandLineParserServiceOpt)
            : base(projectTracker, reportExternalErrorCreatorOpt, projectDisplayName, projectFilePath,
                   hierarchy, language, serviceProvider, visualStudioWorkspaceOpt, hostDiagnosticUpdateSourceOpt, commandLineParserServiceOpt)
        {
            // Set the initial default output bin path for the project, before we add the project to the project tracker.
            var uniqueDefaultOutputPath = PathUtilities.CombinePathsUnchecked(Path.GetTempPath(), Guid.NewGuid().ToString());
            SetOutputPathAndRelatedData(objOutputPath: uniqueDefaultOutputPath, hasSameBinAndObjOutputPaths: true);

            projectTracker.AddProject(this);

            _designTimeBuildStatus = true;
        }

        protected sealed override bool TryGetOutputPathFromHierarchy(out string binOutputPath)
        {
            throw new NotSupportedException();
        }

        protected sealed override bool DesignTimeBuildStatus => _designTimeBuildStatus;
    }
}
