// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis
{
    public static class WellKnownWorkflowStates
    {
        /// <summary>
        /// Indicates that the diagnostic has been deferred for triage/fix in future.
        /// </summary>
        public const string Deferred = "Deferred";

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
