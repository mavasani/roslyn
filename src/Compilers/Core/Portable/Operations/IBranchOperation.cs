// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.Operations
{
    /// <summary>
    /// Represents a branch operation.
    /// <para>
    /// Current usage:
    ///  (1) C# goto, break, or continue statement.
    ///  (2) VB GoTo, Exit ***, or Continue *** statement.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This interface is reserved for implementation by its associated APIs. We reserve the right to
    /// change it in the future.
    /// </remarks>
    public interface IBranchOperation : IOperation
    {
        /// <summary>
        /// Label that is the target of the branch.
        /// </summary>
        ILabelSymbol Target { get; }
        /// <summary>
        /// Kind of the branch.
        /// </summary>
        BranchKind BranchKind { get; }
        /// <summary>
        /// Optional condition of the branch.
        /// Non-null iff <see cref="BranchKind"/> is <see cref="BranchKind.ConditionalGoTo"/>.
        /// </summary>
        IOperation Condition { get; }
        /// <summary>
        /// Always false for non-conditional branch operation.
        /// For conditinal branch operation, indicates if the jump will be executed when the <see cref="Condition"/> is true.
        /// Otherwise, it will be executed when the <see cref="Condition"/> is false.
        /// </summary>
        bool JumpIfConditionTrue { get; }
    }
}

