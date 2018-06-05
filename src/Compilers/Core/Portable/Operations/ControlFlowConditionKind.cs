// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
    /// <summary>
    /// Represents kind of conditional control flow exit from a <see cref="BasicBlock"/>.
    /// </summary>
    public enum ControlFlowConditionKind
    {
        /// <summary>
        /// Indicates no conditional control flow exit from a <see cref="BasicBlock"/>.
        /// Associated <see cref="BasicBlock.ConditionalSuccessor"/> is null.
        /// </summary>
        None,

        /// <summary>
        /// Indicates a conditional control flow 
        /// </summary>
        WhenFalse,
        WhenTrue
    }
}

