// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Options;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Formatting
{
    public static partial class CSharpFormattingOptions
    {
        private static readonly ImmutableDictionary<Option<bool>, SpacingWithinParenthesesOption> s_spacingWithinParenthesisOptionsMap;
        private static readonly ImmutableDictionary<Option<bool>, NewLineOption> s_newLineOptionsMap;
        private static readonly BidirectionalMap<string, SpacingWithinParenthesesOption> s_spacingWithinParenthesisOptionsEditorConfigMap =
            new BidirectionalMap<string, SpacingWithinParenthesesOption>(new[]
            {
                KeyValuePairUtil.Create("expressions", SpacingWithinParenthesesOption.Expressions),
                KeyValuePairUtil.Create("type_casts", SpacingWithinParenthesesOption.TypeCasts),
                KeyValuePairUtil.Create("control_flow_statements", SpacingWithinParenthesesOption.ControlFlowStatements),
            });
        private static readonly BidirectionalMap<string, BinaryOperatorSpacingOptions> s_binaryOperatorSpacingOptionsMap =
            new BidirectionalMap<string, BinaryOperatorSpacingOptions>(new[]
            {
                KeyValuePairUtil.Create("ignore", BinaryOperatorSpacingOptions.Ignore),
                KeyValuePairUtil.Create("none", BinaryOperatorSpacingOptions.Remove),
                KeyValuePairUtil.Create("before_and_after", BinaryOperatorSpacingOptions.Single),
            });
        private static readonly BidirectionalMap<string, LabelPositionOptions> s_labelPositionOptionsMap =
            new BidirectionalMap<string, LabelPositionOptions>(new[]
            {
                KeyValuePairUtil.Create("flush_left", LabelPositionOptions.LeftMost),
                KeyValuePairUtil.Create("no_change", LabelPositionOptions.NoIndent),
                KeyValuePairUtil.Create("one_less_than_current", LabelPositionOptions.OneLess),
            });
        private static readonly BidirectionalMap<string, NewLineOption> s_newLineOptionsEditorConfigMap =
            new BidirectionalMap<string, NewLineOption>(new[]
            {
                KeyValuePairUtil.Create("accessors", NewLineOption.Accessors),
                KeyValuePairUtil.Create("types", NewLineOption.Types),
                KeyValuePairUtil.Create("methods", NewLineOption.Methods),
                KeyValuePairUtil.Create("properties", NewLineOption.Properties),
                KeyValuePairUtil.Create("indexers", NewLineOption.Indexers),
                KeyValuePairUtil.Create("events", NewLineOption.Events),
                KeyValuePairUtil.Create("anonymous_methods", NewLineOption.AnonymousMethods),
                KeyValuePairUtil.Create("control_blocks", NewLineOption.ControlBlocks),
                KeyValuePairUtil.Create("anonymous_types", NewLineOption.AnonymousTypes),
                KeyValuePairUtil.Create("object_collection_array_initalizers", NewLineOption.ObjectCollectionsArrayInitializers),
                KeyValuePairUtil.Create("lambdas", NewLineOption.Lambdas),
                KeyValuePairUtil.Create("local_functions", NewLineOption.LocalFunction),
            });

        static CSharpFormattingOptions()
        {
            var groupedFormattingOptionsBuilder = ImmutableDictionary.CreateBuilder<CSharpFormattingOptionsGroup, ImmutableArray<IOption>>();

            #region Spacing options
            var builder = ImmutableArray.CreateBuilder<IOption>();
            SpacingAfterMethodDeclarationName = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpacingAfterMethodDeclarationName), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_between_method_declaration_name_and_open_parenthesis"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpacingAfterMethodDeclarationName")});
            builder.Add(SpacingAfterMethodDeclarationName);

            SpaceWithinMethodDeclarationParenthesis = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceWithinMethodDeclarationParenthesis), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_between_method_declaration_parameter_list_parentheses"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceWithinMethodDeclarationParenthesis")});
            builder.Add(SpaceWithinMethodDeclarationParenthesis);

            SpaceBetweenEmptyMethodDeclarationParentheses = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceBetweenEmptyMethodDeclarationParentheses), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_between_method_declaration_empty_parameter_list_parentheses"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceBetweenEmptyMethodDeclarationParentheses")});
            builder.Add(SpaceBetweenEmptyMethodDeclarationParentheses);

            SpaceAfterMethodCallName = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceAfterMethodCallName), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_between_method_call_name_and_opening_parenthesis"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceAfterMethodCallName")});
            builder.Add(SpaceAfterMethodCallName);

            SpaceWithinMethodCallParentheses = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceWithinMethodCallParentheses), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_between_method_call_parameter_list_parentheses"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceWithinMethodCallParentheses")});
            builder.Add(SpaceWithinMethodCallParentheses);

            SpaceBetweenEmptyMethodCallParentheses = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceBetweenEmptyMethodCallParentheses), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_between_method_call_empty_parameter_list_parentheses"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceBetweenEmptyMethodCallParentheses")});
            builder.Add(SpaceBetweenEmptyMethodCallParentheses);

            SpaceAfterControlFlowStatementKeyword = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceAfterControlFlowStatementKeyword), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_after_keywords_in_control_flow_statements"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceAfterControlFlowStatementKeyword")});
            builder.Add(SpaceAfterControlFlowStatementKeyword);

            var spacingWithinParenthesisOptionsMapBuilder = ImmutableDictionary.CreateBuilder<Option<bool>, SpacingWithinParenthesesOption>();
            SpaceWithinExpressionParentheses = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceWithinExpressionParentheses), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_space_between_parentheses",
                        s => DetermineIfSpaceOptionIsSet(s, SpaceWithinExpressionParentheses),
                        GetSpacingWithParenthesesEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceWithinExpressionParentheses")});
            builder.Add(SpaceWithinExpressionParentheses);
            spacingWithinParenthesisOptionsMapBuilder.Add(SpaceWithinExpressionParentheses, SpacingWithinParenthesesOption.Expressions);

            SpaceWithinCastParentheses = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceWithinCastParentheses), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_space_between_parentheses",
                        s => DetermineIfSpaceOptionIsSet(s, SpaceWithinCastParentheses),
                        GetSpacingWithParenthesesEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceWithinCastParentheses")});
            builder.Add(SpaceWithinCastParentheses);
            spacingWithinParenthesisOptionsMapBuilder.Add(SpaceWithinCastParentheses, SpacingWithinParenthesesOption.TypeCasts);

            SpaceWithinOtherParentheses = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceWithinOtherParentheses), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_space_between_parentheses",
                        s => DetermineIfSpaceOptionIsSet(s, SpaceWithinOtherParentheses),
                        GetSpacingWithParenthesesEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceWithinOtherParentheses")});
            builder.Add(SpaceWithinOtherParentheses);
            spacingWithinParenthesisOptionsMapBuilder.Add(SpaceWithinOtherParentheses, SpacingWithinParenthesesOption.ControlFlowStatements);

            s_spacingWithinParenthesisOptionsMap = spacingWithinParenthesisOptionsMapBuilder.ToImmutable();

            SpaceAfterCast = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceAfterCast), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_after_cast"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceAfterCast")});
            builder.Add(SpaceAfterCast);

            SpacesIgnoreAroundVariableDeclaration = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpacesIgnoreAroundVariableDeclaration), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_space_around_declaration_statements",
                        s => DetermineIfIgnoreSpacesAroundVariableDeclarationIsSet(s),
                        v => v ? "ignore" : null),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpacesIgnoreAroundVariableDeclaration")});
            builder.Add(SpacesIgnoreAroundVariableDeclaration);

            SpaceBeforeOpenSquareBracket = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceBeforeOpenSquareBracket), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_before_open_square_brackets"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceBeforeOpenSquareBracket")});
            builder.Add(SpaceBeforeOpenSquareBracket);

            SpaceBetweenEmptySquareBrackets = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceBetweenEmptySquareBrackets), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_between_empty_square_brackets"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceBetweenEmptySquareBrackets")});
            builder.Add(SpaceBetweenEmptySquareBrackets);

            SpaceWithinSquareBrackets = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceWithinSquareBrackets), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_between_square_brackets"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceWithinSquareBrackets")});
            builder.Add(SpaceWithinSquareBrackets);

            SpaceAfterColonInBaseTypeDeclaration = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceAfterColonInBaseTypeDeclaration), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_after_colon_in_inheritance_clause"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceAfterColonInBaseTypeDeclaration")});
            builder.Add(SpaceAfterColonInBaseTypeDeclaration);

            SpaceAfterComma = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceAfterComma), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_after_comma"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceAfterComma")});
            builder.Add(SpaceAfterComma);

            SpaceAfterDot = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceAfterDot), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_after_dot"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceAfterDot")});
            builder.Add(SpaceAfterDot);

            SpaceAfterSemicolonsInForStatement = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceAfterSemicolonsInForStatement), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_after_semicolon_in_for_statement"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceAfterSemicolonsInForStatement")});
            builder.Add(SpaceAfterSemicolonsInForStatement);

            SpaceBeforeColonInBaseTypeDeclaration = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceBeforeColonInBaseTypeDeclaration), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_before_colon_in_inheritance_clause"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceBeforeColonInBaseTypeDeclaration")});
            builder.Add(SpaceBeforeColonInBaseTypeDeclaration);

            SpaceBeforeComma = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceBeforeComma), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_before_comma"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceBeforeComma")});
            builder.Add(SpaceBeforeComma);

            SpaceBeforeDot = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceBeforeDot), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_before_dot"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceBeforeDot")});
            builder.Add(SpaceBeforeDot);

            SpaceBeforeSemicolonsInForStatement = new Option<bool>(nameof(CSharpFormattingOptions), nameof(SpaceBeforeSemicolonsInForStatement), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_space_before_semicolon_in_for_statement"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpaceBeforeSemicolonsInForStatement")});
            builder.Add(SpaceBeforeSemicolonsInForStatement);

            SpacingAroundBinaryOperator = new Option<BinaryOperatorSpacingOptions>(nameof(CSharpFormattingOptions), nameof(SpacingAroundBinaryOperator), defaultValue: BinaryOperatorSpacingOptions.Single,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<BinaryOperatorSpacingOptions>("csharp_space_around_binary_operators",
                        s => ParseEditorConfigSpacingAroundBinaryOperator(s),
                        GetSpacingAroundBinaryOperatorEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.SpacingAroundBinaryOperator")});
            builder.Add(SpacingAroundBinaryOperator);

            groupedFormattingOptionsBuilder.Add(CSharpFormattingOptionsGroup.Spacing, builder.ToImmutable());
            #endregion

            #region Indentation options
            builder.Clear();

            IndentBraces = new Option<bool>(nameof(CSharpFormattingOptions), nameof(IndentBraces), defaultValue: false,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_indent_braces"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.OpenCloseBracesIndent")});
            builder.Add(IndentBraces);

            IndentBlock = new Option<bool>(nameof(CSharpFormattingOptions), nameof(IndentBlock), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_indent_block_contents"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.IndentBlock")});
            builder.Add(IndentBlock);

            IndentSwitchSection = new Option<bool>(nameof(CSharpFormattingOptions), nameof(IndentSwitchSection), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_indent_switch_labels"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.IndentSwitchSection")});
            builder.Add(IndentSwitchSection);

            IndentSwitchCaseSection = new Option<bool>(nameof(CSharpFormattingOptions), nameof(IndentSwitchCaseSection), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_indent_case_contents"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.IndentSwitchCaseSection")});
            builder.Add(IndentSwitchCaseSection);

            IndentSwitchCaseSectionWhenBlock = new Option<bool>(nameof(CSharpFormattingOptions), nameof(IndentSwitchCaseSectionWhenBlock), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_indent_case_contents_when_block"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.IndentSwitchCaseSectionWhenBlock")});
            builder.Add(IndentSwitchCaseSectionWhenBlock);

            LabelPositioning = new Option<LabelPositionOptions>(nameof(CSharpFormattingOptions), nameof(LabelPositioning), defaultValue: LabelPositionOptions.OneLess,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<LabelPositionOptions>("csharp_indent_labels",
                        s => ParseEditorConfigLabelPositioning(s),
                        GetLabelPositionOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.LabelPositioning")});
            builder.Add(LabelPositioning);

            groupedFormattingOptionsBuilder.Add(CSharpFormattingOptionsGroup.Indentation, builder.ToImmutable());
            #endregion

            #region Wrapping options
            builder.Clear();

            WrappingPreserveSingleLine = new Option<bool>(nameof(CSharpFormattingOptions), nameof(WrappingPreserveSingleLine), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_preserve_single_line_blocks"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.WrappingPreserveSingleLine")});
            builder.Add(WrappingPreserveSingleLine);

            WrappingKeepStatementsOnSingleLine = new Option<bool>(nameof(CSharpFormattingOptions), nameof(WrappingKeepStatementsOnSingleLine), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_preserve_single_line_statements"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.WrappingKeepStatementsOnSingleLine")});
            builder.Add(WrappingKeepStatementsOnSingleLine);

            groupedFormattingOptionsBuilder.Add(CSharpFormattingOptionsGroup.Wrapping, builder.ToImmutable());
            #endregion

            #region NewLine options
            builder.Clear();

            var newLineOptionsMapBuilder = ImmutableDictionary.CreateBuilder<Option<bool>, NewLineOption>();
            NewLinesForBracesInTypes = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInTypes), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInTypes),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesBracesType")});
            builder.Add(NewLinesForBracesInTypes);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInTypes, NewLineOption.Types);

            NewLinesForBracesInMethods = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInMethods), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInMethods),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesForBracesInMethods")});
            builder.Add(NewLinesForBracesInMethods);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInMethods, NewLineOption.Methods);

            NewLinesForBracesInProperties = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInProperties), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInProperties),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesForBracesInProperties")});
            builder.Add(NewLinesForBracesInProperties);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInProperties, NewLineOption.Properties);

            NewLinesForBracesInAccessors = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInAccessors), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInAccessors),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesForBracesInAccessors")});
            builder.Add(NewLinesForBracesInAccessors);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInAccessors, NewLineOption.Accessors);

            NewLinesForBracesInAnonymousMethods = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInAnonymousMethods), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInAnonymousMethods),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesForBracesInAnonymousMethods")});
            builder.Add(NewLinesForBracesInAnonymousMethods);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInAnonymousMethods, NewLineOption.AnonymousMethods);

            NewLinesForBracesInControlBlocks = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInControlBlocks), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInControlBlocks),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesForBracesInControlBlocks")});
            builder.Add(NewLinesForBracesInControlBlocks);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInControlBlocks, NewLineOption.ControlBlocks);

            NewLinesForBracesInAnonymousTypes = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInAnonymousTypes), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInAnonymousTypes),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesForBracesInAnonymousTypes")});
            builder.Add(NewLinesForBracesInAnonymousTypes);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInAnonymousTypes, NewLineOption.AnonymousTypes);

            NewLinesForBracesInObjectCollectionArrayInitializers = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInObjectCollectionArrayInitializers), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInObjectCollectionArrayInitializers),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesForBracesInObjectCollectionArrayInitializers")});
            builder.Add(NewLinesForBracesInObjectCollectionArrayInitializers);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInObjectCollectionArrayInitializers, NewLineOption.ObjectCollectionsArrayInitializers);

            NewLinesForBracesInLambdaExpressionBody = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLinesForBracesInLambdaExpressionBody), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<bool>("csharp_new_line_before_open_brace",
                        value => DetermineIfNewLineOptionIsSet(value, NewLinesForBracesInLambdaExpressionBody),
                        GetNewLineOptionEditorConfigString),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLinesForBracesInLambdaExpressionBody")});
            builder.Add(NewLinesForBracesInLambdaExpressionBody);
            newLineOptionsMapBuilder.Add(NewLinesForBracesInLambdaExpressionBody, NewLineOption.Lambdas);

            s_newLineOptionsMap = newLineOptionsMapBuilder.ToImmutable();

            NewLineForElse = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLineForElse), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_new_line_before_else"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLineForElse")});
            builder.Add(NewLineForElse);

            NewLineForCatch = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLineForCatch), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_new_line_before_catch"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLineForCatch")});
            builder.Add(NewLineForCatch);

            NewLineForFinally = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLineForFinally), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_new_line_before_finally"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLineForFinally")});
            builder.Add(NewLineForFinally);

            NewLineForMembersInObjectInit = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLineForMembersInObjectInit), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_new_line_before_members_in_object_initializers"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLineForMembersInObjectInit")});
            builder.Add(NewLineForMembersInObjectInit);

            NewLineForMembersInAnonymousTypes = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLineForMembersInAnonymousTypes), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_new_line_before_members_in_anonymous_types"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLineForMembersInAnonymousTypes")});
            builder.Add(NewLineForMembersInAnonymousTypes);

            NewLineForClausesInQuery = new Option<bool>(nameof(CSharpFormattingOptions), nameof(NewLineForClausesInQuery), defaultValue: true,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolOption("csharp_new_line_between_query_expression_clauses"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.NewLineForClausesInQuery")});
            builder.Add(NewLineForClausesInQuery);

            groupedFormattingOptionsBuilder.Add(CSharpFormattingOptionsGroup.NewLine, builder.ToImmutable());
            #endregion

            GroupedFormattingOptions = groupedFormattingOptionsBuilder.ToImmutable();
        }

        internal static ImmutableDictionary<CSharpFormattingOptionsGroup, ImmutableArray<IOption>> GroupedFormattingOptions { get; }
        
        // Spacing options.
        public static Option<bool> SpacingAfterMethodDeclarationName { get; }
        public static Option<bool> SpaceWithinMethodDeclarationParenthesis { get; }
        public static Option<bool> SpaceBetweenEmptyMethodDeclarationParentheses { get; }
        public static Option<bool> SpaceAfterMethodCallName { get; }
        public static Option<bool> SpaceWithinMethodCallParentheses { get; }
        public static Option<bool> SpaceBetweenEmptyMethodCallParentheses { get; }
        public static Option<bool> SpaceAfterControlFlowStatementKeyword { get; }
        public static Option<bool> SpaceWithinExpressionParentheses { get; }
        public static Option<bool> SpaceWithinCastParentheses { get; }
        public static Option<bool> SpaceWithinOtherParentheses { get; }
        public static Option<bool> SpaceAfterCast { get; }
        public static Option<bool> SpacesIgnoreAroundVariableDeclaration { get; }
        public static Option<bool> SpaceBeforeOpenSquareBracket { get; }
        public static Option<bool> SpaceBetweenEmptySquareBrackets { get; }
        public static Option<bool> SpaceWithinSquareBrackets { get; }
        public static Option<bool> SpaceAfterColonInBaseTypeDeclaration { get; }
        public static Option<bool> SpaceAfterComma { get; }
        public static Option<bool> SpaceAfterDot { get; }
        public static Option<bool> SpaceAfterSemicolonsInForStatement { get; }
        public static Option<bool> SpaceBeforeColonInBaseTypeDeclaration { get; }
        public static Option<bool> SpaceBeforeComma { get; }
        public static Option<bool> SpaceBeforeDot { get; }
        public static Option<bool> SpaceBeforeSemicolonsInForStatement { get; }
        public static Option<BinaryOperatorSpacingOptions> SpacingAroundBinaryOperator { get; }

        // Indentation options.
        public static Option<bool> IndentBraces { get; }
        public static Option<bool> IndentBlock { get; }
        public static Option<bool> IndentSwitchSection { get; }
        public static Option<bool> IndentSwitchCaseSection { get; }
        public static Option<bool> IndentSwitchCaseSectionWhenBlock { get; }
        public static Option<LabelPositionOptions> LabelPositioning { get; }

        // Wrapping options
        public static Option<bool> WrappingPreserveSingleLine { get; }
        public static Option<bool> WrappingKeepStatementsOnSingleLine { get; }

        // NewLine options
        public static Option<bool> NewLinesForBracesInTypes { get; }
        public static Option<bool> NewLinesForBracesInMethods { get; }
        public static Option<bool> NewLinesForBracesInProperties { get; }
        public static Option<bool> NewLinesForBracesInAccessors { get; }
        public static Option<bool> NewLinesForBracesInAnonymousMethods { get; }
        public static Option<bool> NewLinesForBracesInControlBlocks { get; }
        public static Option<bool> NewLinesForBracesInAnonymousTypes { get; }
        public static Option<bool> NewLinesForBracesInObjectCollectionArrayInitializers { get; }
        public static Option<bool> NewLinesForBracesInLambdaExpressionBody { get; }
        public static Option<bool> NewLineForElse { get; }
        public static Option<bool> NewLineForCatch { get; }
        public static Option<bool> NewLineForFinally { get; }
        public static Option<bool> NewLineForMembersInObjectInit { get; }
        public static Option<bool> NewLineForMembersInAnonymousTypes { get; }
        public static Option<bool> NewLineForClausesInQuery { get; }
    }

    public enum LabelPositionOptions
    {
        /// Placed in the Zeroth column of the text editor
        LeftMost = 0,

        /// Placed at one less indent to the current context
        OneLess = 1,

        /// Placed at the same indent as the current context
        NoIndent = 2
    }

    public enum BinaryOperatorSpacingOptions
    {
        /// Single Spacing
        Single = 0,

        /// Ignore Formatting
        Ignore = 1,

        /// Remove Spacing
        Remove = 2
    }

    internal enum CSharpFormattingOptionsGroup
    {
        NewLine,
        Indentation,
        Spacing,
        Wrapping
    }
}
