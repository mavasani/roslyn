// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal sealed class CachingSemanticModelProvider : SemanticModelProvider
    {
        private readonly ConditionalWeakTable<SyntaxTree, SemanticModel> _semanticModelsCache;

        public CachingSemanticModelProvider()
        {
            _semanticModelsCache = new ConditionalWeakTable<SyntaxTree, SemanticModel>();
        }

        public override SemanticModel GetSemanticModel(SyntaxTree tree, Compilation compilation)
        {
            if (_semanticModelsCache.TryGetValue(tree, out var model))
            {
                Debug.Assert(model.Compilation == compilation);
                return model;
            }

            return _semanticModelsCache.GetValue(tree, createSemanticModel);

            SemanticModel createSemanticModel(SyntaxTree tree)
            {
                // Avoid infinite recursion by passing 'useSemanticModelProviderIfNonNull: false'
                return compilation.GetSemanticModel(tree, ignoreAccessibility: false, useSemanticModelProviderIfNonNull: false);
            }
        }

        internal void RemoveCachedSemanticModel(SyntaxTree tree)
        {
            _semanticModelsCache.Remove(tree);
        }
    }
}
