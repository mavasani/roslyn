// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Indicates the mode of diagnostic suppression in source.
    /// </summary>
    public enum DiagnosticSuppressionMode
    {
        /// <summary>
        /// Inline suppression in source through a pragma/Disable directive.
        /// </summary>
        SourceDirective,

        /// <summary>
        /// Inline suppression in source through a member level <see cref="SuppressMessageAttribute"/>.
        /// </summary>
        LocalSuppressMessageAttribute,

        /// <summary>
        /// Suppression through an assembly or module level <see cref="SuppressMessageAttribute"/> in a separate suppressions source file.
        /// </summary>
        GlobalSuppressMessageAttribute
    }
}
