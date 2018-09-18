// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.CSharp.CodeStyle
{
    internal static partial class CSharpCodeStyleOptions
    {
        public static readonly CodeStyleOption<ExpressionBodyPreference> NeverWithSilentEnforcement =
            new CodeStyleOption<ExpressionBodyPreference>(ExpressionBodyPreference.Never, NotificationOption.Silent);

        public static readonly CodeStyleOption<ExpressionBodyPreference> NeverWithSuggestionEnforcement =
            new CodeStyleOption<ExpressionBodyPreference>(ExpressionBodyPreference.Never, NotificationOption.Suggestion);

        public static readonly CodeStyleOption<ExpressionBodyPreference> WhenPossibleWithSilentEnforcement =
            new CodeStyleOption<ExpressionBodyPreference>(ExpressionBodyPreference.WhenPossible, NotificationOption.Silent);

        public static readonly CodeStyleOption<ExpressionBodyPreference> WhenPossibleWithSuggestionEnforcement =
            new CodeStyleOption<ExpressionBodyPreference>(ExpressionBodyPreference.WhenPossible, NotificationOption.Suggestion);

        public static readonly CodeStyleOption<ExpressionBodyPreference> WhenOnSingleLineWithSilentEnforcement =
            new CodeStyleOption<ExpressionBodyPreference>(ExpressionBodyPreference.WhenOnSingleLine, NotificationOption.Silent);

        private static readonly SyntaxKind[] s_preferredModifierOrderDefault =
            {
                SyntaxKind.PublicKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword, SyntaxKind.InternalKeyword,
                SyntaxKind.StaticKeyword,
                SyntaxKind.ExternKeyword,
                SyntaxKind.NewKeyword,
                SyntaxKind.VirtualKeyword, SyntaxKind.AbstractKeyword, SyntaxKind.SealedKeyword, SyntaxKind.OverrideKeyword,
                SyntaxKind.ReadOnlyKeyword,
                SyntaxKind.UnsafeKeyword,
                SyntaxKind.VolatileKeyword,
                SyntaxKind.AsyncKeyword
            };

        static CSharpCodeStyleOptions()
        {
            var groupedCodeStyleOptionsBuilder = ImmutableDictionary.CreateBuilder<CSharpCodeStyleOptionsGroup, ImmutableArray<IOption>>();

            #region Var preferences
            var builder = ImmutableArray.CreateBuilder<IOption>();

            UseImplicitTypeForIntrinsicTypes = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(UseImplicitTypeForIntrinsicTypes), defaultValue: CodeStyleOption<bool>.Default,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_style_var_for_built_in_types"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.UseImplicitTypeForIntrinsicTypes")});
            builder.Add(UseImplicitTypeForIntrinsicTypes);

            UseImplicitTypeWhereApparent = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(UseImplicitTypeWhereApparent), defaultValue: CodeStyleOption<bool>.Default,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_style_var_when_type_is_apparent"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.UseImplicitTypeWhereApparent")});
            builder.Add(UseImplicitTypeWhereApparent);

            UseImplicitTypeWherePossible = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(UseImplicitTypeWherePossible), defaultValue: CodeStyleOption<bool>.Default,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_style_var_elsewhere"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.UseImplicitTypeWherePossible")});
            builder.Add(UseImplicitTypeWherePossible);

            groupedCodeStyleOptionsBuilder.Add(CSharpCodeStyleOptionsGroup.VarPreferences, builder.ToImmutable());
            #endregion

            #region Expression bodied member options
            builder.Clear();

            PreferExpressionBodiedConstructors = new Option<CodeStyleOption<ExpressionBodyPreference>>(
                nameof(CodeStyleOptions), nameof(PreferExpressionBodiedConstructors), defaultValue: NeverWithSilentEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<CodeStyleOption<ExpressionBodyPreference>>("csharp_style_expression_bodied_constructors",
                        s => ParseExpressionBodyPreference(s, NeverWithSilentEnforcement),
                        GetExpressionBodyPreferenceEditorConfigString),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferExpressionBodiedConstructors)}")});
            builder.Add(PreferExpressionBodiedConstructors);

            PreferExpressionBodiedMethods = new Option<CodeStyleOption<ExpressionBodyPreference>>(
                nameof(CodeStyleOptions), nameof(PreferExpressionBodiedMethods), defaultValue: NeverWithSilentEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<CodeStyleOption<ExpressionBodyPreference>>("csharp_style_expression_bodied_methods",
                        s => ParseExpressionBodyPreference(s, NeverWithSilentEnforcement),
                        GetExpressionBodyPreferenceEditorConfigString),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferExpressionBodiedMethods)}")});
            builder.Add(PreferExpressionBodiedMethods);

            PreferExpressionBodiedOperators = new Option<CodeStyleOption<ExpressionBodyPreference>>(
                nameof(CodeStyleOptions), nameof(PreferExpressionBodiedOperators), defaultValue: NeverWithSilentEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<CodeStyleOption<ExpressionBodyPreference>>("csharp_style_expression_bodied_operators",
                        s => ParseExpressionBodyPreference(s, NeverWithSilentEnforcement),
                        GetExpressionBodyPreferenceEditorConfigString),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferExpressionBodiedOperators)}")});
            builder.Add(PreferExpressionBodiedOperators);

            PreferExpressionBodiedProperties = new Option<CodeStyleOption<ExpressionBodyPreference>>(
                nameof(CodeStyleOptions), nameof(PreferExpressionBodiedProperties), defaultValue: WhenPossibleWithSilentEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<CodeStyleOption<ExpressionBodyPreference>>("csharp_style_expression_bodied_properties",
                        s => ParseExpressionBodyPreference(s, WhenPossibleWithSilentEnforcement),
                        GetExpressionBodyPreferenceEditorConfigString),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferExpressionBodiedProperties)}")});
            builder.Add(PreferExpressionBodiedProperties);

            PreferExpressionBodiedIndexers = new Option<CodeStyleOption<ExpressionBodyPreference>>(
                nameof(CodeStyleOptions), nameof(PreferExpressionBodiedIndexers), defaultValue: WhenPossibleWithSilentEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<CodeStyleOption<ExpressionBodyPreference>>("csharp_style_expression_bodied_indexers",
                        s => ParseExpressionBodyPreference(s, WhenPossibleWithSilentEnforcement),
                        GetExpressionBodyPreferenceEditorConfigString),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferExpressionBodiedIndexers)}")});
            builder.Add(PreferExpressionBodiedIndexers);

            PreferExpressionBodiedAccessors = new Option<CodeStyleOption<ExpressionBodyPreference>>(
                nameof(CodeStyleOptions), nameof(PreferExpressionBodiedAccessors), defaultValue: WhenPossibleWithSilentEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<CodeStyleOption<ExpressionBodyPreference>>("csharp_style_expression_bodied_accessors",
                        s => ParseExpressionBodyPreference(s, WhenPossibleWithSilentEnforcement),
                        GetExpressionBodyPreferenceEditorConfigString),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferExpressionBodiedAccessors)}")});
            builder.Add(PreferExpressionBodiedAccessors);

            PreferExpressionBodiedLambdas = new Option<CodeStyleOption<ExpressionBodyPreference>>(
                nameof(CodeStyleOptions), nameof(PreferExpressionBodiedLambdas), defaultValue: WhenPossibleWithSilentEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    new EditorConfigStorageLocation<CodeStyleOption<ExpressionBodyPreference>>("csharp_style_expression_bodied_lambdas",
                        s => ParseExpressionBodyPreference(s, WhenPossibleWithSilentEnforcement),
                        GetExpressionBodyPreferenceEditorConfigString),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferExpressionBodiedLambdas)}")});
            builder.Add(PreferExpressionBodiedLambdas);

            groupedCodeStyleOptionsBuilder.Add(CSharpCodeStyleOptionsGroup.ExpressionBodiedMembers, builder.ToImmutable());
            #endregion

            #region Pattern matching options
            builder.Clear();

            PreferPatternMatchingOverAsWithNullCheck = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(PreferPatternMatchingOverAsWithNullCheck), defaultValue: CodeStyleOptions.TrueWithSuggestionEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_style_pattern_matching_over_as_with_null_check"),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferPatternMatchingOverAsWithNullCheck)}")});
            builder.Add(PreferPatternMatchingOverAsWithNullCheck);

            PreferPatternMatchingOverIsWithCastCheck = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(PreferPatternMatchingOverIsWithCastCheck), defaultValue: CodeStyleOptions.TrueWithSuggestionEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_style_pattern_matching_over_is_with_cast_check"),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferPatternMatchingOverIsWithCastCheck)}")});
            builder.Add(PreferPatternMatchingOverIsWithCastCheck);

            groupedCodeStyleOptionsBuilder.Add(CSharpCodeStyleOptionsGroup.PatternMatching, builder.ToImmutable());
            #endregion
            
            #region Null checking options
            builder.Clear();

            PreferConditionalDelegateCall = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(PreferConditionalDelegateCall), defaultValue: CodeStyleOptions.TrueWithSuggestionEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_style_conditional_delegate_call"),
                    new RoamingProfileStorageLocation("TextEditor.CSharp.Specific.PreferConditionalDelegateCall")});
            builder.Add(PreferConditionalDelegateCall);

            groupedCodeStyleOptionsBuilder.Add(CSharpCodeStyleOptionsGroup.NullCheckingPreferences, builder.ToImmutable());
            #endregion

            #region Modifier order preferences
            builder.Clear();

            PreferredModifierOrder = new Option<CodeStyleOption<string>>(
                nameof(CodeStyleOptions), nameof(PreferredModifierOrder),
                defaultValue: new CodeStyleOption<string>(string.Join(",", s_preferredModifierOrderDefault.Select(SyntaxFacts.GetText)), NotificationOption.Silent),
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForStringCodeStyleOption("csharp_preferred_modifier_order"),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferredModifierOrder)}")});
            builder.Add(PreferredModifierOrder);

            groupedCodeStyleOptionsBuilder.Add(CSharpCodeStyleOptionsGroup.ModifierOrder, builder.ToImmutable());
            #endregion

            #region Code block preferences
            builder.Clear();

            PreferBraces = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(PreferBraces), defaultValue: CodeStyleOptions.TrueWithSilentEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_prefer_braces"),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferBraces)}")});
            builder.Add(PreferBraces);

            groupedCodeStyleOptionsBuilder.Add(CSharpCodeStyleOptionsGroup.CodeBlockPreferences, builder.ToImmutable());
            #endregion

            #region Expression level preferences
            builder.Clear();

            PreferSimpleDefaultExpression = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(PreferSimpleDefaultExpression), defaultValue: CodeStyleOptions.TrueWithSuggestionEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_prefer_simple_default_expression"),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferSimpleDefaultExpression)}")});
            builder.Add(PreferSimpleDefaultExpression);

            PreferLocalOverAnonymousFunction = new Option<CodeStyleOption<bool>>(
                nameof(CodeStyleOptions), nameof(PreferLocalOverAnonymousFunction), defaultValue: CodeStyleOptions.TrueWithSuggestionEnforcement,
                storageLocations: new OptionStorageLocation[] {
                    EditorConfigStorageLocation.ForBoolCodeStyleOption("csharp_style_pattern_local_over_anonymous_function"),
                    new RoamingProfileStorageLocation($"TextEditor.CSharp.Specific.{nameof(PreferLocalOverAnonymousFunction)}")});
            builder.Add(PreferLocalOverAnonymousFunction);

            groupedCodeStyleOptionsBuilder.Add(CSharpCodeStyleOptionsGroup.ExpressionLevelPreferences, builder.ToImmutable());
            #endregion

            GroupedCodeStyleOptions = groupedCodeStyleOptionsBuilder.ToImmutable();
        }

        internal static ImmutableDictionary<CSharpCodeStyleOptionsGroup, ImmutableArray<IOption>> GroupedCodeStyleOptions { get; }
        
        // Var preferences.
        public static Option<CodeStyleOption<bool>> UseImplicitTypeForIntrinsicTypes { get; }
        public static Option<CodeStyleOption<bool>> UseImplicitTypeWhereApparent { get; }
        public static Option<CodeStyleOption<bool>> UseImplicitTypeWherePossible { get; }

        // Null checking preferences.
        public static Option<CodeStyleOption<bool>> PreferConditionalDelegateCall { get; }

        // Pattern matching preferences.
        public static Option<CodeStyleOption<bool>> PreferPatternMatchingOverAsWithNullCheck { get; }
        public static Option<CodeStyleOption<bool>> PreferPatternMatchingOverIsWithCastCheck { get; }

        // Expression-bodied member preferences.
        public static Option<CodeStyleOption<ExpressionBodyPreference>> PreferExpressionBodiedConstructors { get; }
        public static Option<CodeStyleOption<ExpressionBodyPreference>> PreferExpressionBodiedMethods { get; }
        public static Option<CodeStyleOption<ExpressionBodyPreference>> PreferExpressionBodiedOperators { get; }
        public static Option<CodeStyleOption<ExpressionBodyPreference>> PreferExpressionBodiedProperties { get; }
        public static Option<CodeStyleOption<ExpressionBodyPreference>> PreferExpressionBodiedIndexers { get; }
        public static Option<CodeStyleOption<ExpressionBodyPreference>> PreferExpressionBodiedAccessors { get; }
        public static Option<CodeStyleOption<ExpressionBodyPreference>> PreferExpressionBodiedLambdas { get; }

        // Code block preferences.
        public static Option<CodeStyleOption<bool>> PreferBraces { get; }

        // Expression level preferences.
        public static Option<CodeStyleOption<bool>> PreferSimpleDefaultExpression { get; }
        public static Option<CodeStyleOption<bool>> PreferLocalOverAnonymousFunction { get; }

        // Modifier order preferences
        public static Option<CodeStyleOption<string>> PreferredModifierOrder { get; }

        public static IEnumerable<Option<CodeStyleOption<bool>>> GetCodeStyleOptions()
        {
            yield return UseImplicitTypeForIntrinsicTypes;
            yield return UseImplicitTypeWhereApparent;
            yield return UseImplicitTypeWherePossible;
            yield return PreferConditionalDelegateCall;
            yield return PreferPatternMatchingOverAsWithNullCheck;
            yield return PreferPatternMatchingOverIsWithCastCheck;
            yield return PreferBraces;
            yield return PreferSimpleDefaultExpression;
            yield return PreferLocalOverAnonymousFunction;
        }

        public static IEnumerable<Option<CodeStyleOption<ExpressionBodyPreference>>> GetExpressionBodyOptions()
        {
            yield return PreferExpressionBodiedConstructors;
            yield return PreferExpressionBodiedMethods;
            yield return PreferExpressionBodiedOperators;
            yield return PreferExpressionBodiedProperties;
            yield return PreferExpressionBodiedIndexers;
            yield return PreferExpressionBodiedAccessors;
            yield return PreferExpressionBodiedLambdas;
        }
    }

    internal enum CSharpCodeStyleOptionsGroup
    {
        VarPreferences,
        ExpressionBodiedMembers,
        PatternMatching,
        NullCheckingPreferences,
        ModifierOrder,
        CodeBlockPreferences,
        ExpressionLevelPreferences,
    }
}
