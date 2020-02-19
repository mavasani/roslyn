﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

#if CODE_STYLE
namespace Microsoft.CodeAnalysis.Internal.Options
#else
namespace Microsoft.CodeAnalysis.Options
#endif
{
    /// <summary>
    /// Group/sub-feature associated with an <see cref="IOption"/>.
    /// </summary>
    internal sealed class OptionGroup
    {
        public static readonly OptionGroup Default = new OptionGroup(string.Empty, int.MaxValue);

        public OptionGroup(string description, int priority)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Priority = priority;
        }

        /// <summary>
        /// A localizable resource description string for the option group.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Relative priority of the option group with respect to other option groups within the same feature.
        /// </summary>
        public int Priority { get; }
    }
}
