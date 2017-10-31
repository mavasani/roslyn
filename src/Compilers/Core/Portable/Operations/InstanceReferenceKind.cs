// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.
                                   
namespace Microsoft.CodeAnalysis.Operations
{
    /// <summary>
    /// Kinds of instance reference.
    /// </summary>
    public enum InstanceReferenceKind
    {
        /// <summary>
        /// Represents unknown instance reference kind.
        /// </summary>
        None = 0x0,

        /// <summary>
        /// Reference to the current instance.
        /// <para>
        /// (1) C# "this" reference.
        /// (2) VB "Me" and "MyClass" reference.
        /// </para>
        /// </summary>
        This = 0x1,

        /// <summary>
        /// Reference to the base instance.
        /// <para>
        /// (1) C# "base" reference.
        /// (2) VB "MyBase" reference.
        /// </para>
        /// </summary>
        Base = 0x2,

        /// <summary>
        /// Reference to the current object being created by an <see cref="IObjectCreationOperation"/>.
        /// </summary>
        ObjectCreation = 0x3,

        /// <summary>
        /// Reference to the anonymous type object being created by an <see cref="IAnonymousObjectCreationOperation"/>.
        /// </summary>
        AnonymousObjectCreation = 0x4,
    }
}

