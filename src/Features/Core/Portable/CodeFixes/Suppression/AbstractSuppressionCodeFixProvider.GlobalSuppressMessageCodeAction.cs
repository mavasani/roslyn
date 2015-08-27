// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal abstract partial class AbstractSuppressionCodeFixProvider : ISuppressionFixProvider
    {
        internal sealed class GlobalSuppressMessageCodeAction : AbstractGlobalSuppressMessageCodeAction
        {
            private readonly ISymbol _targetSymbol;
            private readonly Diagnostic _diagnostic;

            public GlobalSuppressMessageCodeAction(AbstractSuppressionCodeFixProvider fixer, ISymbol targetSymbol, Project project, Diagnostic diagnostic, string workflowState)
                : base(fixer, workflowState, project)
            {
                _targetSymbol = targetSymbol;
                _diagnostic = diagnostic;
            }

            protected override async Task<Document> GetChangedSuppressionDocumentAsync(CancellationToken cancellationToken)
            {
                var suppressionsDoc = await GetOrCreateSuppressionsDocumentAsync(cancellationToken).ConfigureAwait(false);
                var suppressionsRoot = await suppressionsDoc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await suppressionsDoc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var suppressMessageAttribute = semanticModel.Compilation.Assembly.GetTypeByMetadataName(SuppressMessageAttributeFullName);
                var defineAttributeInSource = suppressMessageAttribute == null || !suppressMessageAttribute.DeclaringSyntaxReferences.Any();
                Fixer.AddGlobalSuppressMessageAttribute(suppressionsRoot, _targetSymbol, _diagnostic, this.WorkflowState, defineAttributeInSource);
                return suppressionsDoc.WithSyntaxRoot(suppressionsRoot);
            }

            protected override string DiagnosticIdForEquivalenceKey => _diagnostic.Id;

            internal ISymbol TargetSymbol_TestOnly => _targetSymbol;
        }
    }
}
