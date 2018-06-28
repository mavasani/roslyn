// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.Operations
{
    /// <summary>
    /// Represents a method body operation which could contain an expression body and/or a block body operation.
    /// <para>
    /// Current usage:
    ///  (1) C# method body, where the body could be be written both as an expression body and a block body in valid code.
    ///      Note that this operation is *not* generated for symbol initializers.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This interface is reserved for implementation by its associated APIs. We reserve the right to
    /// change it in the future.
    /// </remarks>
    public interface IMethodBodyBaseOperation : IOperation
    {
        /// <summary>
        /// Method body corresponding to BaseMethodDeclarationSyntax.Body or AccessorDeclarationSyntax.Body
        /// </summary>
        IBlockOperation BlockBody { get; }

        /// <summary>
        /// Method body corresponding to BaseMethodDeclarationSyntax.ExpressionBody or AccessorDeclarationSyntax.ExpressionBody
        /// </summary>
        IBlockOperation ExpressionBody { get; }
    }
}
