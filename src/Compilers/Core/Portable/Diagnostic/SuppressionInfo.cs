// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

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
        public AttributeData? Attribute { get; }

        /// <summary>
        /// If the diagnostic was suppressed by the '!' or the nullable suppression operator.
        /// </summary>
        public bool IsNullableSuppression { get; }

        /// <summary>
        /// If the diagnostic was suppressed by a pragma suppression.
        /// </summary>
        public bool IsPragmaSuppression { get; }

        internal SuppressionInfo(string id, AttributeData? attribute, bool isNullableSuppression)
        {
            Debug.Assert(!isNullableSuppression || attribute == null);
            Id = id;
            Attribute = attribute;
            IsNullableSuppression = isNullableSuppression;
            IsPragmaSuppression = !isNullableSuppression && attribute == null;

        }
    }
}
