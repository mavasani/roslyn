// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    /// <summary>
    /// The base type for diagnostic suppressors that can programmatically suppress diagnostics.
    /// </summary>
    public abstract class DiagnosticSuppressor : DiagnosticAnalyzer
    {
        public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray<DiagnosticDescriptor>.Empty;

        /// <summary>
        /// Returns a set of descriptors for the suppressions that this suppressor is capable of producing.
        /// </summary>
        public abstract ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; }
    }
}
