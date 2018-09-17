// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using Microsoft.CodeAnalysis.CodeStyle;

namespace Microsoft.CodeAnalysis.Options
{
    internal static class EditorConfigStorageLocation
    {
        public static EditorConfigStorageLocation<bool> ForBoolOption(string keyName)
            => new EditorConfigStorageLocation<bool>(keyName, s_parseBool, s_getBoolValueString);

        public static EditorConfigStorageLocation<int> ForInt32Option(string keyName)
            => new EditorConfigStorageLocation<int>(keyName, s_parseInt32, s_getInt32ValueString);

        public static EditorConfigStorageLocation<CodeStyleOption<bool>> ForBoolCodeStyleOption(string keyName)
            => new EditorConfigStorageLocation<CodeStyleOption<bool>>(keyName, s_parseBoolCodeStyleOption, s_getBoolCodeStyleOptionValueString);

        public static EditorConfigStorageLocation<CodeStyleOption<string>> ForStringCodeStyleOption(string keyName)
            => new EditorConfigStorageLocation<CodeStyleOption<string>>(keyName, s_parseStringCodeStyleOption, s_getStringCodeStyleOptionValueString);

        private static readonly Func<string, Optional<bool>> s_parseBool = ParseBool;
        private static Optional<bool> ParseBool(string str)
            => bool.TryParse(str, out var result) ? result : new Optional<bool>();
        private static readonly Func<bool, string> s_getBoolValueString = GetBoolValueString;
        private static string GetBoolValueString(bool value) => value.ToString();

        private static readonly Func<string, Optional<int>> s_parseInt32 = ParseInt32;
        private static Optional<int> ParseInt32(string str)
            => int.TryParse(str, out var result) ? result : new Optional<int>();
        private static readonly Func<int, string> s_getInt32ValueString = GetInt32ValueString;
        private static string GetInt32ValueString(int value) => value.ToString();

        private static readonly Func<string, Optional<CodeStyleOption<bool>>> s_parseBoolCodeStyleOption = ParseBoolCodeStyleOption;
        private static Optional<CodeStyleOption<bool>> ParseBoolCodeStyleOption(string str)
            => CodeStyleHelpers.TryParseBoolEditorConfigCodeStyleOption(str, out var result) ? result : new Optional<CodeStyleOption<bool>>();
        private static readonly Func<CodeStyleOption<bool>, string> s_getBoolCodeStyleOptionValueString = GetBoolCodeStyleOptionValueString;
        private static string GetBoolCodeStyleOptionValueString(CodeStyleOption<bool> value)
            => value.Value
               ? $"true:{value.Notification.ToString()}"
               : value.Notification != null ? $"false:{value.Notification.ToString()}" : "false";

        private static readonly Func<string, Optional<CodeStyleOption<string>>> s_parseStringCodeStyleOption = ParseStringCodeStyleOption;
        private static Optional<CodeStyleOption<string>> ParseStringCodeStyleOption(string str)
            => CodeStyleHelpers.TryParseStringEditorConfigCodeStyleOption(str, out var result) ? result : new Optional<CodeStyleOption<string>>();
        private static readonly Func<CodeStyleOption<string>, string> s_getStringCodeStyleOptionValueString = GetStringCodeStyleOptionValueString;
        private static string GetStringCodeStyleOptionValueString(CodeStyleOption<string> value) => value.Value;
    }
}
