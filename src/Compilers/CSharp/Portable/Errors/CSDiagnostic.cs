// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.CSharp
{
    /// <summary>
    /// A diagnostic, along with the location where it occurred.
    /// </summary>
    internal sealed class CSDiagnostic : DiagnosticWithInfo
    {
        internal CSDiagnostic(DiagnosticInfo info, Location location, bool isSuppressed = false, ImmutableHashSet<string> suppressingAnalyzers = null)
            : base(info, location, isSuppressed, suppressingAnalyzers ?? ImmutableHashSet<string>.Empty)
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
                return new CSDiagnostic(this.Info, location, this.IsSuppressed);
            }

            return this;
        }

        internal override Diagnostic WithSeverity(DiagnosticSeverity severity)
        {
            if (this.Severity != severity)
            {
                return new CSDiagnostic(this.Info.GetInstanceWithSeverity(severity), this.Location, this.IsSuppressed);
            }

            return this;
        }

        internal override Diagnostic WithIsSuppressed(bool isSuppressed)
        {
            if (this.IsSuppressed != isSuppressed)
            {
                return new CSDiagnostic(this.Info, this.Location, isSuppressed);
            }

            return this;
        }

        internal override Diagnostic WithAnalyzerSuppressions(ImmutableHashSet<string> suppressingAnalyzers)
        {
            Debug.Assert(!this.IsSuppressed);
            Debug.Assert(this.SuppressingAnalyzers.IsEmpty);
            return new CSDiagnostic(this.Info, this.Location, isSuppressed: true, suppressingAnalyzers);
        }
    }
}
