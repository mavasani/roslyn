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
        internal sealed class PragmaWarningCodeAction : AbstractSuppressionCodeAction
        {
            private readonly SyntaxToken _startToken;
            private readonly SyntaxToken _endToken;
            private readonly SyntaxNode _nodeWithTokens;
            private readonly Document _document;
            private readonly Diagnostic _diagnostic;
            private readonly bool _forFixMultipleContext;

            public PragmaWarningCodeAction(
                AbstractSuppressionCodeFixProvider fixer,
                SyntaxToken startToken,
                SyntaxToken endToken,
                SyntaxNode nodeWithTokens,
                Document document,
                Diagnostic diagnostic,
                bool forFixMultipleContext = false)
                : base (fixer, title: FeaturesResources.SuppressWithPragma)
            {
                _startToken = startToken;
                _endToken = endToken;
                _nodeWithTokens = nodeWithTokens;
                _document = document;
                _diagnostic = diagnostic;
                _forFixMultipleContext = forFixMultipleContext;
            }
            
            public PragmaWarningCodeAction CloneForFixMultipleContext()
            {
                return new PragmaWarningCodeAction(Fixer, _startToken, _endToken, _nodeWithTokens, _document, _diagnostic, forFixMultipleContext: true);

            }
            protected override string DiagnosticIdForEquivalenceKey =>
                _forFixMultipleContext ? string.Empty : _diagnostic.Id;

            protected async override Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
            {
                return await PragmaHelpers.GetChangeDocumentWithPragmaAdjustedAsync(
                    _document,
                    _startToken,
                    _endToken,
                    _nodeWithTokens,
                    start => PragmaHelpers.GetNewStartTokenWithAddedPragma(start, _diagnostic, Fixer),
                    end => PragmaHelpers.GetNewEndTokenWithAddedPragma(end, _diagnostic, Fixer),
                    cancellationToken).ConfigureAwait(false);
            }
            
            public SyntaxToken StartToken_TestOnly => _startToken;
            public SyntaxToken EndToken_TestOnly => _endToken;
        }
    }
}
