// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Linq;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Formatting
{
    public static partial class CSharpFormattingOptions
    {
        private static readonly BidirectionalMap<string, SpacingWithinParenthesesOption> s_spacingWithinParenthesisOptionsMap =
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

        private static readonly BidirectionalMap<string, NewLineOption> s_newLineOptionsMap =
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

        internal static bool DetermineIfSpaceOptionIsSet(string value, SpacingWithinParenthesesOption parenthesesSpacingOption)
            => (from v in value.Split(',').Select(v => v.Trim())
                let option = ConvertToSpacingOption(v)
                where option.HasValue && option.Value == parenthesesSpacingOption
                select option)
                .Any();

        private static SpacingWithinParenthesesOption? ConvertToSpacingOption(string value)
            => s_spacingWithinParenthesisOptionsMap.GetValueOrDefault(value);

        private static string GetSpacingWithParenthesesEditorConfigString(SpacingWithinParenthesesOption value)
            => s_spacingWithinParenthesisOptionsMap.TryGetKey(value, out string key) ? key : null;

        internal static BinaryOperatorSpacingOptions ParseEditorConfigSpacingAroundBinaryOperator(string binaryOperatorSpacingValue)
            => s_binaryOperatorSpacingOptionsMap.TryGetValue(binaryOperatorSpacingValue, out var value) ? value : BinaryOperatorSpacingOptions.Single;

        private static string GetSpacingAroundBinaryOperatorEditorConfigString(BinaryOperatorSpacingOptions value)
            => s_binaryOperatorSpacingOptionsMap.TryGetKey(value, out string key) ? key : null;

        internal static LabelPositionOptions ParseEditorConfigLabelPositioning(string labelIndentationValue)
            => s_labelPositionOptionsMap.TryGetValue(labelIndentationValue, out var value) ? value : LabelPositionOptions.NoIndent;
        private static string GetLabelPositionOptionEditorConfigString(LabelPositionOptions value)
            => s_labelPositionOptionsMap.TryGetKey(value, out string key) ? key : null;

        internal static bool DetermineIfNewLineOptionIsSet(string value, NewLineOption optionName)
        {
            var values = value.Split(',');

            if (values.Any(s => s.Trim() == "all"))
            {
                return true;
            }

            if (values.Any(s => s.Trim() == "none"))
            {
                return false;
            }

            return (from v in values
                    let option = ConvertToNewLineOption(v)
                    where option.HasValue && option.Value == optionName
                    select option)
                    .Any();
        }

        private static NewLineOption? ConvertToNewLineOption(string value)
            => s_newLineOptionsMap.GetValueOrDefault(value);
        private static string GetNewLineOptionEditorConfigString(NewLineOption value)
            => s_newLineOptionsMap.TryGetKey(value, out string key) ? key : null;

        internal static bool DetermineIfIgnoreSpacesAroundVariableDeclarationIsSet(string value)
            => value.Trim() == "ignore";

        internal enum SpacingWithinParenthesesOption
        {
            Expressions,
            TypeCasts,
            ControlFlowStatements
        }

        internal enum NewLineOption
        {
            Types,
            Methods,
            Properties,
            Indexers,
            Events,
            AnonymousMethods,
            ControlBlocks,
            AnonymousTypes,
            ObjectCollectionsArrayInitializers,
            Lambdas,
            LocalFunction,
            Accessors
        }
    }
}
