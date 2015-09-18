// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal abstract partial class AbstractSuppressionCodeFixProvider : ISuppressionFixProvider
    {
        internal sealed class RemoveSuppressionBatchFixAllProvider : BatchFixAllProvider
        {
            private readonly AbstractSuppressionCodeFixProvider _suppressionFixProvider;

            public RemoveSuppressionBatchFixAllProvider(AbstractSuppressionCodeFixProvider suppressionFixProvider)
            {
                _suppressionFixProvider = suppressionFixProvider;
            }

            public override async Task AddDocumentFixesAsync(Document document, ImmutableArray<Diagnostic> diagnostics, Action<CodeAction> addFix, FixAllContext fixAllContext)
            {
                foreach (var diagnosticsForSpan in diagnostics.Where(d => d.Location.IsInSource && d.IsSuppressed).GroupBy(d => d.Location.SourceSpan))
                {
                    var span = diagnosticsForSpan.First().Location.SourceSpan;
                    var removeSuppressionFixes = await _suppressionFixProvider.GetSuppressionsAsync(document, span, diagnosticsForSpan, fixAllContext.CancellationToken).ConfigureAwait(false);
                    var removeSuppressionActions = removeSuppressionFixes.Select(fix => fix.Action).Cast<RemoveSuppressionCodeAction>();
                    foreach (var removeSuppressionAction in removeSuppressionActions)
                    {
                        if (fixAllContext is FixMultipleContext)
                        {
                            addFix(removeSuppressionAction.CloneForFixMultipleContext());
                        }
                        else
                        {
                            addFix(removeSuppressionAction);
                        }
                    }
                }
            }
        }
    }
}
