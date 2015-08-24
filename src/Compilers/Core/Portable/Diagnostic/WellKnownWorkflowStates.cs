// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis
{
    public static class WellKnownWorkflowStates
    {
        /// <summary>
        /// Indicates that the diagnostic has been marked to be triaged in future.
        /// </summary>
        public const string DeferredTriage = "DeferredTriage";

        /// <summary>
        /// Indicates that the diagnostic has been triaged to be a valid issue, but has been deferred to be fixed in future, possibly due to its low priority and/or presence of a workaround.
        /// </summary>
        public const string DeferredFix = "DeferredFix";

        /// <summary>
        /// Indicates that the diagnostic has been triaged as a valid issue that will never be fixed, possibly due to its low priority and/or presence of a workaround.
        /// </summary>
        public const string SuppressedWontFix = "SuppressedWontFix";

        /// <summary>
        /// Indicates that the diagnostic has been triaged as a false report, and hence marked as suppressed.
        /// </summary>
        public const string SuppressedFalsePositive = "SuppressedFalsePositive";
    }
}
