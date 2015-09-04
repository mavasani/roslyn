// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.CodeActions;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal abstract partial class AbstractSuppressionCodeFixProvider : ISuppressionFixProvider
    {
        internal abstract class AbstractSuppressionCodeAction : CodeAction
        {
            private readonly AbstractSuppressionCodeFixProvider _fixer;
            private readonly string _title;

            protected AbstractSuppressionCodeAction(AbstractSuppressionCodeFixProvider fixer, string title)
            {
                _fixer = fixer;
                _title = title;
            }

            public sealed override string Title => _title;
            protected AbstractSuppressionCodeFixProvider Fixer => _fixer;
            protected abstract string DiagnosticIdForEquivalenceKey { get; }

            public sealed override string EquivalenceKey => Title + DiagnosticIdForEquivalenceKey;
            public static bool IsEquivalenceKeyForGlobalSuppression(string equivalenceKey) =>
                equivalenceKey.StartsWith(FeaturesResources.SuppressWithGlobalSuppressMessage);
        }
    }
}
