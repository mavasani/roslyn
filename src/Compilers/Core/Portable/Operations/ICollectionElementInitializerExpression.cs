// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.Semantics
{
    /// <summary>
    /// Represents a collection element initializer expression.
    /// </summary>
    /// <remarks>
    /// This interface is reserved for implementation by its associated APIs. We reserve the right to
    /// change it in the future.
    /// </remarks>
    public interface ICollectionElementInitializerExpression : IHasArgumentsExpression
    {
        /// <summary>
        /// Set of applicable methods for an implicit dynamic invocation OR the implicit Add method symbol for non-dynamic invocation.
        /// </summary>
        ImmutableArray<IMethodSymbol> ApplicableMethods { get; }

        /// <summary>
        /// Flag indicating if this is a dynamic initializer invocation.
        /// </summary>
        bool IsDynamic { get; }
    }
}

