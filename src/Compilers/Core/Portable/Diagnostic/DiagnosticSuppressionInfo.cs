// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Contains information about source level diagnostic suppression.
    /// </summary>
    public class DiagnosticSuppressionInfo : IEquatable<DiagnosticSuppressionInfo>
    {
        public DiagnosticSuppressionInfo(DiagnosticSuppressionMode suppressionMode)
        {
            this.SuppressionMode = suppressionMode;
        }

        public DiagnosticSuppressionMode SuppressionMode { get; }

        public bool Equals(DiagnosticSuppressionInfo other)
        {
            return other != null && other.SuppressionMode == this.SuppressionMode;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DiagnosticSuppressionInfo);
        }

        public override int GetHashCode()
        {
            return this.SuppressionMode.GetHashCode();
        }
    }
}
