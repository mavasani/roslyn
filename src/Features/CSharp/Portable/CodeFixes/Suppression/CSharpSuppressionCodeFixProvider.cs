// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Composition;
using System.Globalization;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeFixes.Suppression;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.CodeAnalysis.CSharp.CodeFixes.Suppression
{
    [ExportSuppressionFixProvider(PredefinedCodeFixProviderNames.Suppression, LanguageNames.CSharp), Shared]
    internal class CSharpSuppressionCodeFixProvider : AbstractSuppressionCodeFixProvider
    {
        private const string AttributeDefinition = @"
using System;
using System.Diagnostics.CodeAnalysis;

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal sealed class SuppressMessageAttribute : Attribute
    {
        public SuppressMessageAttribute(string category, string checkId)
        {
            this.Category = category;
            this.CheckId = checkId;
        }

        public string Category { get; private set; }
        public string CheckId { get; private set; }
        public string Scope { get; set; }
        public string Target { get; set; }
        public string MessageId { get; set; }
        public string Justification { get; set; }
        public string WorkflowState { get; set; }
    }
}
";

        protected override SyntaxTriviaList CreatePragmaRestoreDirectiveTrivia(Diagnostic diagnostic, bool needsTrailingEndOfLine)
        {
            var restoreKeyword = SyntaxFactory.Token(SyntaxKind.RestoreKeyword);
            return CreatePragmaDirectiveTrivia(restoreKeyword, diagnostic, true, needsTrailingEndOfLine);
        }

        protected override SyntaxTriviaList CreatePragmaDisableDirectiveTrivia(Diagnostic diagnostic, bool needsLeadingEndOfLine)
        {
            var disableKeyword = SyntaxFactory.Token(SyntaxKind.DisableKeyword);
            return CreatePragmaDirectiveTrivia(disableKeyword, diagnostic, needsLeadingEndOfLine, true);
        }

        private SyntaxTriviaList CreatePragmaDirectiveTrivia(SyntaxToken disableOrRestoreKeyword, Diagnostic diagnostic, bool needsLeadingEndOfLine, bool needsTrailingEndOfLine)
        {
            var id = SyntaxFactory.IdentifierName(diagnostic.Id);
            var ids = new SeparatedSyntaxList<ExpressionSyntax>().Add(id);
            var pragmaDirective = SyntaxFactory.PragmaWarningDirectiveTrivia(disableOrRestoreKeyword, ids, true);
            var pragmaDirectiveTrivia = SyntaxFactory.Trivia(pragmaDirective.WithAdditionalAnnotations(Formatter.Annotation));
            var endOfLineTrivia = SyntaxFactory.ElasticCarriageReturnLineFeed;
            var triviaList = SyntaxFactory.TriviaList(pragmaDirectiveTrivia);

            var title = diagnostic.Descriptor.Title.ToString(CultureInfo.CurrentUICulture);
            if (!string.IsNullOrWhiteSpace(title))
            {
                var titleComment = SyntaxFactory.Comment(string.Format(" // {0}", title)).WithAdditionalAnnotations(Formatter.Annotation);
                triviaList = triviaList.Add(titleComment);
            }

            if (needsLeadingEndOfLine)
            {
                triviaList = triviaList.Insert(0, endOfLineTrivia);
            }

            if (needsTrailingEndOfLine)
            {
                triviaList = triviaList.Add(endOfLineTrivia);
            }

            return triviaList;
        }

        protected override string DefaultFileExtension
        {
            get
            {
                return ".cs";
            }
        }

        protected override string SingleLineCommentStart
        {
            get
            {
                return "//";
            }
        }

        protected override string TitleForPragmaWarningSuppressionFix
        {
            get
            {
                return CSharpFeaturesResources.SuppressWithPragma;
            }
        }

        protected override bool IsValidTopLevelNodeForSuppressionFile(SyntaxNode node)
        {
            if (node is UsingDirectiveSyntax)
            {
                return true;
            }

            var namespaceDecl = node as NamespaceDeclarationSyntax;
            if (namespaceDecl != null)
            {
                if (namespaceDecl.Members.Count == 1 && namespaceDecl.Members[0].Kind() == SyntaxKind.ClassDeclaration)
                {
                    var classDecl = (ClassDeclarationSyntax)namespaceDecl.Members[0];
                    if (classDecl.Identifier.ValueText == SuppressMessageAttributeName)
                    {
                        return true;
                    }
                }

                return false;
            }

            var attributeList = node as AttributeListSyntax;
            return attributeList != null &&
                attributeList.Target != null &&
                attributeList.Target.Identifier.Kind() == SyntaxKind.AssemblyKeyword;
        }

        protected override bool IsEndOfLine(SyntaxTrivia trivia)
        {
            return trivia.Kind() == SyntaxKind.EndOfLineTrivia;
        }

        protected override bool IsEndOfFileToken(SyntaxToken token)
        {
            return token.Kind() == SyntaxKind.EndOfFileToken;
        }

        protected override SyntaxNode AddGlobalSuppressMessageAttribute(SyntaxNode newRoot, ISymbol targetSymbol, Diagnostic diagnostic, string workflowState, bool defineAttribute)
        {
            var compilationRoot = (CompilationUnitSyntax)newRoot;
            var addHeaderComment = !compilationRoot.AttributeLists.Any();

            if (defineAttribute)
            {
                compilationRoot = SyntaxFactory.ParseCompilationUnit(AttributeDefinition)
                    .WithAdditionalAnnotations(Formatter.Annotation);
            }

            if (addHeaderComment)
            {
                compilationRoot = compilationRoot.WithLeadingTrivia(SyntaxFactory.Comment(GlobalSuppressionsFileHeaderComment))
                    .WithAdditionalAnnotations(Formatter.Annotation);
            }

            var attributeList = CreateAttributeList(targetSymbol, diagnostic, workflowState);
            return compilationRoot.AddAttributeLists(attributeList);
        }

        private AttributeListSyntax CreateAttributeList(ISymbol targetSymbol, Diagnostic diagnostic, string workflowState)
        {
            var attributeArguments = CreateAttributeArguments(targetSymbol, diagnostic, workflowState);
            var attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName(SuppressMessageAttributeFullName), attributeArguments)
                .WithAdditionalAnnotations(Simplifier.Annotation);
            var attributes = new SeparatedSyntaxList<AttributeSyntax>().Add(attribute);

            var targetSpecifier = SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.AssemblyKeyword));
            var attributeList = SyntaxFactory.AttributeList(targetSpecifier, attributes);

            return attributeList.WithAdditionalAnnotations(Formatter.Annotation);
        }

        private AttributeArgumentListSyntax CreateAttributeArguments(ISymbol targetSymbol, Diagnostic diagnostic, string workflowState)
        {
            // DiagnosticWorkflowStateAttribute("Rule Category", "Rule Id", WorkflowState = "WorkflowState", Justification = "Justification" , Scope = "Scope", Target = "Target")
            var category = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(diagnostic.Descriptor.Category));
            var categoryArgument = SyntaxFactory.AttributeArgument(category);

            var title = diagnostic.Descriptor.Title.ToString(CultureInfo.CurrentUICulture);
            var ruleIdText = string.IsNullOrWhiteSpace(title) ? diagnostic.Id : string.Format("{0}:{1}", diagnostic.Id, title);
            var ruleId = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(ruleIdText));
            var ruleIdArgument = SyntaxFactory.AttributeArgument(ruleId);

            var workflowStateExpr = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(workflowState ?? WellKnownWorkflowStates.SuppressedFalsePositive));
            var workflowStateArgument = SyntaxFactory.AttributeArgument(SyntaxFactory.NameEquals("WorkflowState"), nameColon: null, expression: workflowStateExpr);

            var justificationExpr = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(FeaturesResources.SuppressionPendingJustification));
            var justificationArgument = SyntaxFactory.AttributeArgument(SyntaxFactory.NameEquals("Justification"), nameColon: null, expression: justificationExpr);

            var attributeArgumentList = SyntaxFactory.AttributeArgumentList().AddArguments(categoryArgument, ruleIdArgument, workflowStateArgument, justificationArgument);

            var scopeString = GetScopeString(targetSymbol.Kind);
            if (scopeString != null)
            {
                var scopeExpr = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(scopeString));
                var scopeArgument = SyntaxFactory.AttributeArgument(SyntaxFactory.NameEquals("Scope"), nameColon: null, expression: scopeExpr);

                var targetString = GetTargetString(targetSymbol);
                var targetExpr = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(targetString));
                var targetArgument = SyntaxFactory.AttributeArgument(SyntaxFactory.NameEquals("Target"), nameColon: null, expression: targetExpr);

                attributeArgumentList = attributeArgumentList.AddArguments(scopeArgument, targetArgument);
            }

            return attributeArgumentList;
        }
    }
}
