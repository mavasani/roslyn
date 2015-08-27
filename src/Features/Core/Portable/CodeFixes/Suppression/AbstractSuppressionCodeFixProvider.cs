// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal abstract partial class AbstractSuppressionCodeFixProvider : ISuppressionFixProvider
    {
        public const string SuppressMessageAttributeFullName = "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute";
        public const string SuppressMessageAttributeName = "SuppressMessageAttribute";
        private static readonly string s_globalSuppressionsFileName = "GlobalSuppressions";
        private static readonly string s_suppressionsFileCommentTemplate =
@"
{0} This file is used by Code Analysis to maintain SuppressMessage and DiagnosticTriage
{0} attributes that are applied to this project.
{0} Project-level suppressions and triage states either have no target or are given 
{0} a specific target and scoped to a namespace, type, member, etc.

";

        private static bool IsNotConfigurableDiagnostic(Diagnostic diagnostic)
        {
            return diagnostic.Descriptor.CustomTags.Any(c => CultureInfo.InvariantCulture.CompareInfo.Compare(c, WellKnownDiagnosticTags.NotConfigurable) == 0);
        }

        private static bool IsCompilerDiagnostic(Diagnostic diagnostic)
        {
            return diagnostic.Descriptor.CustomTags.Any(c => CultureInfo.InvariantCulture.CompareInfo.Compare(c, WellKnownDiagnosticTags.Compiler) == 0);
        }

        public FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public bool CanBeSuppressedOrTriaged(Diagnostic diagnostic)
        {
            if (diagnostic.Location.Kind != LocationKind.SourceFile || diagnostic.HasSourceSuppression)
            {
                // Don't offer suppression or triage fixes for diagnostics without a source location and diagnostics which have been suppressed.
                return false;
            }

            if (IsCompilerDiagnostic(diagnostic))
            {
                // Compiler diagnostics only support pragma based suppressions.
                return CanBeSuppressed(diagnostic);
            }

            // All other diagnostics can be triaged (and possibly suppressed too).
            return true;
        }

        private bool CanBeSuppressed(Diagnostic diagnostic)
        {
            if (diagnostic.Location.Kind != LocationKind.SourceFile || IsNotConfigurableDiagnostic(diagnostic))
            {
                // Don't offer suppression fixes for diagnostics without a source location and non-configurable diagnostics.
                return false;
            }

            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Error:
                case DiagnosticSeverity.Hidden:
                    return false;

                case DiagnosticSeverity.Warning:
                case DiagnosticSeverity.Info:
                    return true;

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        protected abstract string TitleForPragmaWarningSuppressionFix { get; }
        protected abstract SyntaxTriviaList CreatePragmaDisableDirectiveTrivia(Diagnostic diagnostic, bool needsLeadingEndOfLine);
        protected abstract SyntaxTriviaList CreatePragmaRestoreDirectiveTrivia(Diagnostic diagnostic, bool needsTrailingEndOfLine);

        protected abstract SyntaxNode AddGlobalSuppressMessageAttribute(SyntaxNode newRoot, ISymbol targetSymbol, Diagnostic diagnostic, string workflowState, bool defineAttribute);

        protected abstract string DefaultFileExtension { get; }
        protected abstract string SingleLineCommentStart { get; }
        protected abstract bool IsValidTopLevelNodeForSuppressionFile(SyntaxNode node);
        protected abstract bool IsEndOfLine(SyntaxTrivia trivia);
        protected abstract bool IsEndOfFileToken(SyntaxToken token);

        protected string GlobalSuppressionsFileHeaderComment
        {
            get
            {
                return string.Format(s_suppressionsFileCommentTemplate, this.SingleLineCommentStart);
            }
        }

        protected virtual SyntaxToken GetAdjustedTokenForPragmaDisable(SyntaxToken token, SyntaxNode root, TextLineCollection lines, int indexOfLine)
        {
            return token;
        }

        protected virtual SyntaxToken GetAdjustedTokenForPragmaRestore(SyntaxToken token, SyntaxNode root, TextLineCollection lines, int indexOfLine)
        {
            return token;
        }

        public Task<IEnumerable<CodeFix>> GetSuppressionsAsync(Document document, TextSpan span, IEnumerable<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            return GetSuppressionsAsync(document, span, diagnostics, onlyPragmaSuppressions: false, cancellationToken: cancellationToken);
        }

        internal async Task<IEnumerable<PragmaWarningCodeAction>> GetPragmaSuppressionsAsync(Document document, TextSpan span, IEnumerable<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            var codeFixes = await GetSuppressionsAsync(document, span, diagnostics, onlyPragmaSuppressions: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return codeFixes.Select(fix => (PragmaWarningCodeAction)fix.Action);
        }

        private async Task<IEnumerable<CodeFix>> GetSuppressionsAsync(Document document, TextSpan span, IEnumerable<Diagnostic> diagnostics, bool onlyPragmaSuppressions, CancellationToken cancellationToken)
        {
            // We only care about diagnostics that can be suppressed or triaged.
            diagnostics = diagnostics.Where(CanBeSuppressedOrTriaged);
            var suppressableDiagnostics = ImmutableHashSet.CreateRange(diagnostics.Where(CanBeSuppressed));

            var suppressionTargetInfo = await GetSuppressionTargetInfoAsync(document, span, cancellationToken).ConfigureAwait(false);
            if (suppressionTargetInfo == null)
            {
                return SpecializedCollections.EmptyEnumerable<CodeFix>();
            }

            var result = new List<CodeFix>();
            foreach (var diagnostic in diagnostics)
            {
                var nestedActions = new List<CodeAction>();

                // global assembly-level suppress message attribute.
                AddGlobalSuppressMessageCodeAction(nestedActions, suppressionTargetInfo.TargetSymbol, document, diagnostic, WellKnownWorkflowStates.Deferred, onlyPragmaSuppressions);

                if (!diagnostic.HasSourceSuppression && suppressableDiagnostics.Contains(diagnostic))
        {
                    // global assembly-level suppress message attribute.
                    AddGlobalSuppressMessageCodeAction(nestedActions, suppressionTargetInfo.TargetSymbol, document, diagnostic, WellKnownWorkflowStates.SuppressedWontFix, onlyPragmaSuppressions);
                    AddGlobalSuppressMessageCodeAction(nestedActions, suppressionTargetInfo.TargetSymbol, document, diagnostic, WellKnownWorkflowStates.SuppressedFalsePositive, onlyPragmaSuppressions);

                    // pragma warning disable.
                    nestedActions.Add(new PragmaWarningCodeAction(this, suppressionTargetInfo.StartToken, suppressionTargetInfo.EndToken, suppressionTargetInfo.NodeWithTokens, document, diagnostic));
                }

                result.Add(new CodeFix(new SuppressionCodeAction(diagnostic, nestedActions), diagnostic));
            }

            return result;
        }

        private class SuppressionTargetInfo
            {
            public ISymbol TargetSymbol { get; set; }
            public SyntaxToken StartToken { get; set; }
            public SyntaxToken EndToken { get; set; }
            public SyntaxNode NodeWithTokens { get; set; }
            }

        private async Task<SuppressionTargetInfo> GetSuppressionTargetInfoAsync(Document document, TextSpan span, CancellationToken cancellationToken)
        {
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxTree.GetLineVisibility(span.Start, cancellationToken) == LineVisibility.Hidden)
            {
                return null;
            }

            // Find the start token to attach leading pragma disable warning directive.
            var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            SyntaxTrivia containingTrivia = root.FindTrivia(span.Start);
            var lines = syntaxTree.GetText(cancellationToken).Lines;
            int indexOfLine;
            if (containingTrivia == default(SyntaxTrivia))
            {
                indexOfLine = lines.IndexOf(span.Start);
            }
            else
            {
                indexOfLine = lines.IndexOf(containingTrivia.Token.SpanStart);
            }

            var lineAtPos = lines[indexOfLine];
            var startToken = root.FindToken(lineAtPos.Start);
            startToken = GetAdjustedTokenForPragmaDisable(startToken, root, lines, indexOfLine);

            // Find the end token to attach pragma restore warning directive.
            // This should be the last token on the line that contains the start token.
            indexOfLine = lines.IndexOf(startToken.Span.End);
            lineAtPos = lines[indexOfLine];
            var endToken = root.FindToken(lineAtPos.End);
            endToken = GetAdjustedTokenForPragmaRestore(endToken, root, lines, indexOfLine);

            SyntaxNode nodeWithTokens = null;
            if (IsEndOfFileToken(endToken))
            {
                nodeWithTokens = root;
            }
            else
            {
                nodeWithTokens = startToken.GetCommonRoot(endToken);
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();

            ISymbol targetSymbol = null;
            var targetMemberNode = syntaxFacts.GetContainingMemberDeclaration(root, startToken.SpanStart);
                if (targetMemberNode != null)
                {
                    targetSymbol = semanticModel.GetDeclaredSymbol(targetMemberNode, cancellationToken);

                    if (targetSymbol == null)
                    {
                        var analyzerDriverService = document.GetLanguageService<IAnalyzerDriverService>();

                        // targetMemberNode could be a declaration node with multiple decls (e.g. field declaration defining multiple variables).
                        // Let us compute all the declarations intersecting the span.
                        var decls = analyzerDriverService.GetDeclarationsInSpan(semanticModel, span, true, cancellationToken);
                        if (decls.Any())
                        {
                            var containedDecls = decls.Where(d => span.Contains(d.DeclaredNode.Span));
                            if (containedDecls.Count() == 1)
                            {
                                // Single containing declaration, use this symbol.
                                var decl = containedDecls.Single();
                                targetSymbol = decl.DeclaredSymbol;
                            }
                            else
                            {
                                // Otherwise, use the most enclosing declaration.
                                TextSpan? minContainingSpan = null;
                                foreach (var decl in decls)
                                {
                                    var declSpan = decl.DeclaredNode.Span;
                                    if (declSpan.Contains(span) &&
                                        (!minContainingSpan.HasValue || minContainingSpan.Value.Contains(declSpan)))
                                    {
                                        minContainingSpan = declSpan;
                                        targetSymbol = decl.DeclaredSymbol;
                                    }
                                }
                            }
                        }
                    }
                }

                if (targetSymbol == null)
                {
                    // Outside of a member declaration, suppress diagnostic for the entire assembly.
                    targetSymbol = semanticModel.Compilation.Assembly;
                }

            return new SuppressionTargetInfo() { TargetSymbol = targetSymbol, NodeWithTokens = nodeWithTokens, StartToken = startToken, EndToken = endToken };
        }

        private void AddGlobalSuppressMessageCodeAction(List<CodeAction> nestedActions, ISymbol targetSymbol, Document document, Diagnostic diagnostic, string workflowState, bool onlyPragaSuppressions)
                {
            if (diagnostic.WorkflowState != workflowState && !onlyPragaSuppressions)
                    {
                nestedActions.Add(new GlobalSuppressMessageCodeAction(this, targetSymbol, document.Project, diagnostic, workflowState));
            }
        }

        protected string GetScopeString(SymbolKind targetSymbolKind)
        {
            switch (targetSymbolKind)
            {
                case SymbolKind.Event:
                case SymbolKind.Field:
                case SymbolKind.Method:
                case SymbolKind.Property:
                    return "member";

                case SymbolKind.NamedType:
                    return "type";

                case SymbolKind.Namespace:
                    return "namespace";

                default:
                    return null;
            }
        }

        protected string GetTargetString(ISymbol targetSymbol)
        {
            return "~" + DocumentationCommentId.CreateDeclarationId(targetSymbol);
        }
    }
}
