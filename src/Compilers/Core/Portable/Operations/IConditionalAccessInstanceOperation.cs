﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.Semantics
{
    /// <summary>
    /// Represents the value of a conditionally-accessed operation within an operation containing a conditional access.
    /// For a conditional access operation of the form <code>someExpr?.Member</code>, this operation is used as the InstanceReceiver for the right operation <code>Member</code>.
    /// See https://github.com/dotnet/roslyn/issues/21279#issuecomment-323153041 for more details.
    /// <para>
    /// Current usage:
    ///  (1) C# conditional access instance expression.
    ///  (2) VB conditional access instance expression.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This interface is reserved for implementation by its associated APIs. We reserve the right to
    /// change it in the future.
    /// </remarks>
    public interface IConditionalAccessInstanceOperation : IOperation
    {
    }
}

