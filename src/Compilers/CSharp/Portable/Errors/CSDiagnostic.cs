﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;

namespace Microsoft.CodeAnalysis.CSharp
{
    /// <summary>
    /// A diagnostic, along with the location where it occurred.
    /// </summary>
    internal sealed class CSDiagnostic : DiagnosticWithInfo
    {
        internal CSDiagnostic(DiagnosticInfo info, Location location, string workflowState = null)
            : base(info, location, workflowState)
        {
        }

        public override string ToString()
        {
            return CSharpDiagnosticFormatter.Instance.Format(this);
        }

        internal override Diagnostic WithLocation(Location location)
        {
            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            if (location != this.Location)
            {
                return new CSDiagnostic(this.Info, location, this.WorkflowState);
            }

            return this;
        }

        internal override Diagnostic WithSeverity(DiagnosticSeverity severity)
        {
            if (this.Severity != severity)
            {
                return new CSDiagnostic(this.Info.GetInstanceWithSeverity(severity), this.Location, this.WorkflowState);
            }

            return this;
        }

        internal override Diagnostic WithWorkflowState(string workflowState)
        {
            if (this.WorkflowState != workflowState)
            {
                return new CSDiagnostic(this.Info, this.Location, workflowState);
            }

            return this;
        }
    }
}
