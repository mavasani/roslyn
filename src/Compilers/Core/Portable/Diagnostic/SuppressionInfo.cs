// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

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
        /// Additional information about the source of the suppression. Possible values are:
        ///     1. "pragma": Suppressed by an inline pragma source directive
        ///     2. "SuppressMessageAttribute": Suppressed by a local or a global SuppressMessageAttribute
        ///     3. Comma separated list of programmatic suppressions, where each suppression is in the format: %SuppressionID: SuppressionDescription%
        /// </summary>
        public string Source { get; }

        internal SuppressionInfo(string id, AttributeData attribute, string suppressionMessage)
        {
            Id = id;
            Attribute = attribute;
            Source = suppressionMessage;
        }
    }
}
