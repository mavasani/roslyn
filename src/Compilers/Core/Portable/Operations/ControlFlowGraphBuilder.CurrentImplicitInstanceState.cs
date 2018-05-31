// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
    internal partial class ControlFlowGraphBuilder
    {
        /// <summary>
        /// Holds the state for the current object or anonymous type instance being initialized if we're visiting it's initializer.
        /// Or the target of a VB With statement.
        /// </summary>
        private struct CurrentImplicitInstanceState
        {
            /// <summary>
            /// Holds the current object instance being initialized if we're visiting an object initializer.
            /// </summary>
            public IOperation Object { get; }

            /// <summary>
            /// Holds the current anonymous type instance being initialized if we're visiting an anonymous object initializer.
            /// </summary>
            public INamedTypeSymbol AnonymousType { get; }

            /// <summary>
            /// Holds the capture Ids for initialized anonymous type properties in an anonymous object initializer.
            /// </summary>
            public PooledDictionary<IPropertySymbol, int> AnonymousTypePropertyCaptureIds { get; }

            public CurrentImplicitInstanceState(IOperation currentImplicitInstance)
            {
                Object = currentImplicitInstance;
                AnonymousType = null;
                AnonymousTypePropertyCaptureIds = null;
            }

            public CurrentImplicitInstanceState(ITypeSymbol currentInitializedAnonymousType)
            {
                Debug.Assert(currentInitializedAnonymousType.IsAnonymousType);

                Object = null;
                AnonymousType = (INamedTypeSymbol)currentInitializedAnonymousType;
                AnonymousTypePropertyCaptureIds = PooledDictionary<IPropertySymbol, int>.GetInstance();
            }

            public void Free()
            {
                AnonymousTypePropertyCaptureIds?.Free();
            }
        }
    }
}
