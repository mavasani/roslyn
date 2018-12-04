// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    /// <summary>
    /// Contains information about the source of diagnostic suppression.
    /// </summary>
    public sealed class SuppressionInfo
    {
        /// <summary>
        /// <see cref="Diagnostic.Id"/> of the suppressed diagnostic.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// If the diagnostic was suppressed by an attribute, then returns that attribute.
        /// Otherwise, returns null.
        /// </summary>
        public AttributeData Attribute { get; }

        /// <summary>
        /// If the diagnostic was suppressed by one or more analyzer suppression actions, then returns the set of fully qualified
        /// names of suppressing analyzers.
        /// Otherwise, returns null.
        /// </summary>
        public ImmutableHashSet<string> SuppressingAnalyzers { get; }

        internal SuppressionInfo(string id, AttributeData attribute, ImmutableHashSet<string> suppressingAnalyzers)
        {
            Debug.Assert(suppressingAnalyzers != null);
            Debug.Assert(attribute == null || suppressingAnalyzers.IsEmpty);

            Id = id;
            Attribute = attribute;
            SuppressingAnalyzers = suppressingAnalyzers;
        }
    }
}
