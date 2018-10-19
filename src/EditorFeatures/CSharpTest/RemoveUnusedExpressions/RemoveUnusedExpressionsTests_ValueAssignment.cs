// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.RemoveUnusedExpressions;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.Diagnostics;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.RemoveUnusedExpressions
{
    public partial class RemoveUnusedExpressionsTests : AbstractCSharpDiagnosticProviderBasedUserDiagnosticTest
    {
        internal override (DiagnosticAnalyzer, CodeFixProvider) CreateDiagnosticProviderAndFixer(Workspace workspace)
            => (new CSharpRemoveUnusedExpressionsDiagnosticAnalyzer(), new CSharpRemoveUnusedExpressionsCodeFixProvider());

        private IDictionary<OptionKey, object> PreferNone =>
            Option(CodeStyleOptions.UnusedExpressionAssignment,
                   new CodeStyleOption<UnusedExpressionAssignmentPreference>(UnusedExpressionAssignmentPreference.None, NotificationOption.Suggestion));

        private IDictionary<OptionKey, object> PreferDiscard =>
            Option(CodeStyleOptions.UnusedExpressionAssignment,
                   new CodeStyleOption<UnusedExpressionAssignmentPreference>(UnusedExpressionAssignmentPreference.DiscardVariable, NotificationOption.Suggestion));

        private IDictionary<OptionKey, object> PreferUnusedLocal =>
            Option(CodeStyleOptions.UnusedExpressionAssignment,
                   new CodeStyleOption<UnusedExpressionAssignmentPreference>(UnusedExpressionAssignmentPreference.UnusedLocalVariable, NotificationOption.Suggestion));
        private IDictionary<OptionKey, object> GetOptions(string optionName)
        {
            switch (optionName)
            {
                case nameof(PreferDiscard):
                    return PreferDiscard;

                case nameof(PreferUnusedLocal):
                    return PreferUnusedLocal;

                default:
                    return PreferNone;
            }
        }

        // Helper to test all options - only used by tests which already have InlineData for custom input test code snippets.
        private async Task TestInRegularAndScriptWithAllOptionsAsync(string initialMarkup, string expectedMarkup)
        {
            foreach (var options in new[] { PreferDiscard, PreferUnusedLocal })
            {
                await TestInRegularAndScriptAsync(initialMarkup, expectedMarkup, options: options);
            }
        }

        // Helper to test all options - only used by tests which already have InlineData for custom input test code snippets.
        private async Task TestMissingInRegularAndScriptWithAllOptionsAsync(string initialMarkup)
        {
            foreach (var options in new[] { PreferDiscard, PreferUnusedLocal })
            {
                await TestMissingInRegularAndScriptAsync(initialMarkup, new TestParameters(options: options));
            }
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_Suppressed()
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int [|x|] = 1;
        x = 2;
        return x;
    }
}", new TestParameters(options: PreferNone));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Assignment_Suppressed()
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int x;
        [|x|] = 1;
        x = 2;
        return x;
    }
}", new TestParameters(options: PreferNone));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Initialization_ConstantValue(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int [|x|] = 1;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M()
    {
        int x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Assignment_ConstantValue(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int x;
        [|x|] = 1;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M()
    {
        int x;
        x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Initialization_NonConstantValue_ParameterReference(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M(int p)
    {
        int [|x|] = p;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M(int p)
    {
        int x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Assignment_NonConstantValue_ParameterReference(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M(int p)
    {
        int x;
        [|x|] = p;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M(int p)
    {
        int x;
        x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Initialization_NonConstantValue_LocalReference(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int local = 0;
        int [|x|] = local;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M()
    {
        int local = 0;
        int x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Assignment_NonConstantValue_LocalReference(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int local = 0;
        int x;
        [|x|] = local;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M()
    {
        int local = 0;
        int x;
        x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Initialization_NonConstantValue_FieldReferenceWithThisReceiver(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    private int field;
    int M()
    {
        int [|x|] = field;
        x = 2;
        return x;
    }
}",
@"class C
{
    private int field;
    int M()
    {
        int x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Assignment_NonConstantValue_FieldReferenceWithNullReceiver(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    private static int field;
    int M()
    {
        int x;
        [|x|] = field;
        x = 2;
        return x;
    }
}",
@"class C
{
    private static int field;
    int M()
    {
        int x;
        x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "int unused")]
        public async Task Assignment_NonConstantValue_FieldReferenceWithReceiver(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    private int field;
    int M(C c)
    {
        int x;
        [|x|] = c.field;
        x = 2;
        return x;
    }
}",
$@"class C
{{
    private int field;
    int M(C c)
    {{
        int x;
        {fix} = c.field;
        x = 2;
        return x;
    }}
}}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "int unused")]
        public async Task Initialization_NonConstantValue_PropertyReference(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    private int Property { get { throw new System.Exception(); } }
    int M()
    {
        int x;
        [|x|] = Property;
        x = 2;
        return x;
    }
}",
$@"class C
{{
    private int Property {{ get {{ throw new System.Exception(); }} }}
    int M()
    {{
        int x;
        {fix} = Property;
        x = 2;
        return x;
    }}
}}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "int unused")]
        public async Task Initialization_NonConstantValue_MethodInvocation(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int [|x|] = M2();
        x = 2;
        return x;
    }

    int M2() => 0;
}",
$@"class C
{{
    int M()
    {{
        {fix} = M2();
        int x = 2;
        return x;
    }}

    int M2() => 0;
}}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "int unused")]
        public async Task Assignment_NonConstantValue_MethodInvocation(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int x;
        [|x|] = M2();
        x = 2;
        return x;
    }

    int M2() => 0;
}",
$@"class C
{{
    int M()
    {{
        int x;
        {fix} = M2();
        x = 2;
        return x;
    }}

    int M2() => 0;
}}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "int unused")]
        public async Task Assignment_NonConstantValue_ImplicitConversion(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M(int x, short s)
    {
        [|x|] = s;
        x = 2;
        return x;
    }
}",
$@"class C
{{
    int M(int x, short s)
    {{
        {fix} = s;
        x = 2;
        return x;
    }}
}}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task NestedAssignment_ConstantValue(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M(int x, int y)
    {
        y = [|x|] = 1;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M(int x, int y)
    {
        y = 1;
        x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task NestedAssignment_NonConstantValue_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M(int x, int y)
    {
        y = [|x|] = M2();
        x = 2;
        return x;
    }

    int M2() => 0;
}",
@"class C
{
    int M(int x, int y)
    {
        y = _ = M2();
        x = 2;
        return x;
    }

    int M2() => 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task NestedAssignment_NonConstantValue_PreferUnusedLocal()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M(int x, int y)
    {
        y = [|x|] = M2();
        x = 2;
        return x;
    }

    int M2() => 0;
}",
@"class C
{
    int M(int x, int y)
    {
        int unused;
        y = unused = M2();
        x = 2;
        return x;
    }

    int M2() => 0;
}", options: PreferUnusedLocal);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_NoReferences_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M()
    {
        int [|x|] = M2();
    }

    int M2() => 0;
}",
@"class C
{
    void M()
    {
        _ = M2();
    }

    int M2() => 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_NoReferences_PreferUnusedLocal()
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    void M()
    {
        int [|x|] = M2();
    }

    int M2() => 0;
}", new TestParameters(options: PreferUnusedLocal));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_NoReadReferences_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M()
    {
        int [|x|] = M2();
        x = 0;
    }

    int M2() => 0;
}",
@"class C
{
    void M()
    {
        _ = M2();
        int x = 0;
    }

    int M2() => 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_NoReadReferences_PreferUnusedLocal()
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    void M()
    {
        int [|x|] = M2();
        x = 0;
    }

    int M2() => 0;
}", new TestParameters(options: PreferUnusedLocal));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Initialization_ConstantValue_FirstField(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int [|x|] = 1, y = 2;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M()
    {
        int y = 2;
        int x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Initialization_ConstantValue_MiddleField(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int i = 0, [|x|] = 1, y = 2;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M()
    {
        int i = 0, y = 2;
        int x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Initialization_ConstantValue_LastField(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int i = 0, y = 2, [|x|] = 1;
        x = 2;
        return x;
    }
}",
@"class C
{
    int M()
    {
        int i = 0, y = 2;
        int x = 2;
        return x;
    }
}", options: GetOptions(optionName));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_FirstField_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int [|x|] = M2(), y = 2;
        x = 2;
        return x;
    }

    void M2() => 0;
}",
@"class C
{
    int M()
    {
        _ = M2();
        int y = 2;
        int x = 2;
        return x;
    }

    void M2() => 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_FirstField_PreferUnusedLocal()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int [|x|] = M2(), y = 2;
        x = 2;
        return x;
    }

    void M2() => 0;
}",
@"class C
{
    int M()
    {
        int unused = M2(), y = 2;
        int x = 2;
        return x;
    }

    void M2() => 0;
}", options: PreferUnusedLocal);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_MiddleField_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int i = 0, [|x|] = M2(), y = 2;
        x = 2;
        return x;
    }

    void M2() => 0;
}",
@"class C
{
    int M()
    {
        int i = 0;
        _ = M2();
        int y = 2;
        int x = 2;
        return x;
    }

    void M2() => 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_MiddleField_PreferUnusedLocal()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int i = 0, [|x|] = M2(), y = 2;
        x = 2;
        return x;
    }

    void M2() => 0;
}",
@"class C
{
    int M()
    {
        int i = 0, unused = M2(), y = 2;
        int x = 2;
        return x;
    }

    void M2() => 0;
}", options: PreferUnusedLocal);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_LastField_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int i = 0, y = 2, [|x|] = M2();
        x = 2;
        return x;
    }

    void M2() => 0;
}",
@"class C
{
    int M()
    {
        int i = 0, y = 2;
        _ = M2();
        int x = 2;
        return x;
    }

    void M2() => 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Initialization_NonConstantValue_LastField_PreferUnusedLocal()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int i = 0, y = 2, [|x|] = M2();
        x = 2;
        return x;
    }

    void M2() => 0;
}",
@"class C
{
    int M()
    {
        int i = 0, y = 2, unused = M2();
        int x = 2;
        return x;
    }

    void M2() => 0;
}", options: PreferUnusedLocal);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Assignment_BeforeUseAsOutArgument(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int x;
        [|x|] = 1;
        M2(out x);
        return x;
    }

    void M2(out int x) => x = 0;
}",
@"class C
{
    int M()
    {
        int x;
        M2(out x);
        return x;
    }

    void M2(out int x) => x = 0;
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task NonRedundantAssignment_BeforeUseAsRefArgument(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int x;
        [|x|] = 1;
        M2(ref x);
        return x;
    }

    void M2(ref int x) => x = 0;
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task NonRedundantAssignment_BeforeUseAsInArgument(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int x;
        [|x|] = 1;
        M2(in x);
        return x;
    }

    void M2(in int x) { }
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task OutArgument_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int x;
        M2(out [|x|]);
        x = 1;
        return x;
    }

    void M2(out int x) => x = 0;
}",
@"class C
{
    int M()
    {
        int x;
        M2(out _);
        x = 1;
        return x;
    }

    void M2(out int x) => x = 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task OutArgument_PreferUnusedLocal()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        int x;
        M2(out [|x|]);
        x = 1;
        return x;
    }

    void M2(out int x) => x = 0;
}",
@"class C
{
    int M()
    {
        int x;
        int unused;
        M2(out unused);
        x = 1;
        return x;
    }

    void M2(out int x) => x = 0;
}", options: PreferUnusedLocal);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "unused")]
        public async Task OutDeclarationExpressionArgument(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        M2(out var [|x|]);
        x = 1;
        return x;
    }

    void M2(out int x) => x = 0;
}",
$@"class C
{{
    int M()
    {{
        M2(out var {fix});
        int x = 1;
        return x;
    }}

    void M2(out int x) => x = 0;
}}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task NonRedundantRefArgument(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    int M(int x)
    {
        M2(ref [|x|]);
        x = 1;
        return x;
    }

    void M2(ref int x) => x = 0;
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task NonRedundantInArgument(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    int M(int x)
    {
        M2(in [|x|]);
        x = 1;
        return x;
    }

    void M2(in int x) { }
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "unused")]
        public async Task DeconstructionDeclarationExpression(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        var ([|x|], y) = (1, 1);
        x = 1;
        return x;
    }
}",
$@"class C
{{
    int M()
    {{
        var ({fix}, y) = (1, 1);
        int x = 1;
        return x;
    }}
}}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "unused")]
        public async Task TupleExpressionWithDeclarationExpressions(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M()
    {
        (var [|x|], var y) = (1, 1);
        x = 1;
        return x;
    }
}",
$@"class C
{{
    int M()
    {{
        (var {fix}, var y) = (1, 1);
        int x = 1;
        return x;
    }}
}}", options: GetOptions(optionName));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task DeclarationPatternInSwitchCase_WithOnlyWriteReference_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M(object p)
    {
        switch (p)
        {
            case int [|x|]:
                x = 1;
                break;
        };
    }
}",
@"class C
{
    void M(object p)
    {
        switch (p)
        {
            case int _:
                int x;
                x = 1;
                break;
        };
    }
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task DeclarationPatternInSwitchCase_WithOnlyWriteReference_PreferUnusedLocal()
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    void M(object p)
    {
        switch (p)
        {
            case int [|x|]:
                x = 1;
                break;
        };
    }
}", new TestParameters(options: PreferUnusedLocal));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task UseInLambdaPassedAsArgument(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

class C
{
    Action M(object p)
    {
        [|p|] = null;
        M2(() =>
        {
            if (p != null)
            {
            }
        });
    }

    void M2(Action a) => a();
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task UseInDelegateCreationPassedAsArgument(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

class C
{
    Action M(object p)
    {
        [|p|] = null;
        M2(new Action(() =>
        {
            if (p != null)
            {
            }
        }));
    }

    void M2(Action a) => a();
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task UseInDelegatePassedAsArgument(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

class C
{
    Action M(object p)
    {
        Action local = () =>
        {
            if (p != null)
            {
            }
        };

        [|p|] = null;
        M2(local);
    }

    void M2(Action a) => a();
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task UseInNestedLambda(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

class C
{
    Action M(object p)
    {
        Action outerLambda = () =>
        {
            Action innerLambda = () =>
            {
                if (p != null)
                {
                }
            };

            innerLambda();
        });

        [|p|] = null;
        outerLambda();
    }

    void M2(Action a) => a();
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task UseInReturnedLambdaCreation(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

class C
{
    Action M(object p)
    {
        [|p|] = null;
        return new Action(() =>
        {
            if (p != null)
            {
            }
        });
    }
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task UseInReturnedDelegate(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

class C
{
    Action M(object p)
    {
        Action local = () =>
        {
            if (p != null)
            {
            }
        };

        [|p|] = null;
        return local;
    }
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task UseInReturnedDelegate_02(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"using System;

class C
{
    Action M(object p, bool flag)
    {
        Action local1 = () =>
        {
            if (p != null)
            {
            }
        };

        Action local2 = () => { };

        [|p|] = null;
        return flag ? local1 : local2;
    }
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard), "_")]
        [InlineData(nameof(PreferUnusedLocal), "unused")]
        public async Task DeclarationPatternInSwitchCase_WithReadAndWriteReferences(string optionName, string fix)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M(object p)
    {
        switch (p)
        {
            case int [|x|]:
                x = 1;
                p = x;
                break;
        }
    }
}",
$@"class C
{{
    void M(object p)
    {{
        switch (p)
        {{
            case int {fix}:
                int x;
                x = 1;
                p = x;
                break;
        }}
    }}
}}", options: GetOptions(optionName));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task IfElse_AssignedInCondition_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M(bool flag)
    {
        int x;
        if (M2(out [|x|]))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }
    }

    bool M2(out int x) => x = 0;
}",
@"class C
{
    void M(bool flag)
    {
        int x;
        if (M2(out _))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }
    }

    bool M2(out int x) => x = 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task IfElse_DeclaredInCondition_PreferDiscard()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M(bool flag)
    {
        if (M2(out var [|x|]))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }
    }

    bool M2(out int x) => x = 0;
}",
@"class C
{
    void M(bool flag)
    {
        int x;
        if (M2(out var _))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }
    }

    bool M2(out int x) => x = 0;
}", options: PreferDiscard);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task IfElseAssignedInCondition_ReadAfter_PreferUnusedLocal()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M(bool flag)
    {
        int x;
        if (M2(out [|x|]))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }

        return x;
    }

    bool M2(out int x) => x = 0;
}",
@"class C
{
    int M(bool flag)
    {
        int x;
        int unused;
        if (M2(out unused))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }

        return x;
    }

    bool M2(out int x) => x = 0;
}", options: PreferUnusedLocal);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task IfElse_AssignedInCondition_NoReads_PreferUnusedLocal()
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    void M(bool flag)
    {
        int x;
        if (M2(out [|x|]))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }
    }

    bool M2(out int x) => x = 0;
}", new TestParameters(options: PreferUnusedLocal));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task IfElse_DeclaredInCondition_ReadAfter_PreferUnusedLocal()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    int M(bool flag)
    {
        if (M2(out var [|x|]))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }

        return x;
    }

    bool M2(out int x) => x = 0;
}",
@"class C
{
    int M(bool flag)
    {
        int x;
        if (M2(out var unused))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }

        return x;
    }

    bool M2(out int x) => x = 0;
}", options: PreferUnusedLocal);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task IfElse_DeclaredInCondition_NoReads_PreferUnusedLocal()
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    void M(bool flag)
    {
        if (M2(out var [|x|]))
        {
            x = 2;
        }
        else
        {
            x = 3;
        }
    }

    bool M2(out int x) => x = 0;
}", new TestParameters(options: PreferUnusedLocal));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        // Simple if-else.
        [InlineData("x = 1;", "x = 2;")]
        // Nested if-else.
        [InlineData("if(flag) { x = 1; } else { x = 2; }",
                    "x = 3;")]
        // Multiple nested paths.
        [InlineData("if(flag) { x = 1; } else { x = 2; }",
                    "if(flag) { x = 3; } else { x = 4; }")]
        // Nested if-elseif-else.
        [InlineData("if(flag) { x = 1; } else if(flag2) { x = 2; } else { x = 3; }",
                    "if(flag) { x = 5; } else { x = 6; }")]
        //Multi-level nesting.
        [InlineData(@"if(flag) { x = 1; } else { if(flag2) { if(flag3) { x = 2; } else { x = 3; } } else { x = 4; } }",
                    @"x = 5;")]
        public async Task IfElse_OverwrittenInAllControlFlowPaths(string ifBranchCode, string elseBranchCode)
        {
            await TestInRegularAndScriptWithAllOptionsAsync(
$@"class C
{{
    int M(bool flag, bool flag2, bool flag3)
    {{
        int [|x|] = 1;
        if (flag4)
        {{
            {ifBranchCode}
        }}
        else
        {{
            {elseBranchCode}
        }}

        return x;
    }}
}}",
$@"class C
{{
    int M(bool flag, bool flag2, bool flag3)
    {{
        int x;
        if (flag4)
        {{
            {ifBranchCode}
        }}
        else
        {{
            {elseBranchCode}
        }}

        return x;
    }}
}}");
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        // Overwrite missing in if path.
        [InlineData(";", "x = 2;")]
        // Overwrite missing in else path.
        [InlineData("x = 2;", "")]
        // Overwrite missing in nested else path.
        [InlineData("if(flag) { x = 1; }",
                    "x = 2;")]
        // Overwrite missing in multiple nested paths.
        [InlineData("if(flag) { x = 1; }",
                    "if(flag) { x = 2; }")]
        // Overwrite missing with nested if-elseif-else.
        [InlineData("if(flag) { x = 1; } else if(flag2) { x = 2; }",
                    "if(flag) { x = 3; } else { x = 4; }")]
        // Overwrite missing in one path with multi-level nesting.
        [InlineData(@"if(flag) { x = 1; } else { if(flag2) { if(flag3) { x = 2; } } else { x = 3; } }",
                    @"x = 4;")]
        public async Task IfElse_OverwrittenInSomeControlFlowPaths(string ifBranchCode, string elseBranchCode)
        {
            await TestMissingInRegularAndScriptWithAllOptionsAsync(
$@"class C
{{
    int M(bool flag, bool flag2, bool flag3)
    {{
        int [|x|] = 1;
        if (flag4)
        {{
            {ifBranchCode}
        }}
        else
        {{
            {elseBranchCode}
        }}

        return x;
    }}
}}");
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        // Overitten in condition when true, overwritten in else code block when false.
        [InlineData("flag && M2(out x)", ";", "x = 2;")]
        // Overitten in condition when false, overwritten in if code block when true.
        [InlineData("flag || M2(out x)", "x = 2;", ";")]
        public async Task IfElse_Overwritten_CodeInOneBranch_ConditionInOtherBranch(string condition, string ifBranchCode, string elseBranchCode)
        {
            await TestInRegularAndScriptWithAllOptionsAsync(
$@"class C
{{
    int M(bool flag)
    {{
        int [|x|] = 1;
        if ({condition})
        {{
            {ifBranchCode}
        }}
        else
        {{
            {elseBranchCode}
        }}

        return x;
    }}

    bool M2(out int x) {{ x = 0; return true; }}
    int M3() => 0;
}}",
$@"class C
{{
    int M(bool flag)
    {{
        int x;
        if ({condition})
        {{
            {ifBranchCode}
        }}
        else
        {{
            {elseBranchCode}
        }}

        return x;
    }}

    bool M2(out int x) {{ x = 0; return true; }}
    int M3() => 0;
}}");
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        // Overwrite missing in condition when left of || is true.
        [InlineData("flag || M2(out x)")]
        // Overwrite missing in condition when left of && is true.
        [InlineData("flag && M2(out x)")]
        // Overwrite missing in condition when left of || is true, but both both sides of && have an overwrite.
        [InlineData("flag || M2(out x) && (x = M3()) > 0")]
        public async Task IfElse_MayBeOverwrittenInCondition_LogicalOperators(string condition)
        {
            await TestMissingInRegularAndScriptWithAllOptionsAsync(
$@"class C
{{
    int M(bool flag)
    {{
        int [|x|] = 1;
        if ({condition})
        {{
        }}
        else
        {{
        }}

        return x;
    }}

    bool M2(out int x) {{ x = 0; return true; }}
    int M3() => 0;
}}");
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData("M2(out x) || flag")]
        [InlineData("M2(out x) && flag")]
        [InlineData("M2(out x) || M2(out x)")]
        [InlineData("M2(out x) && M2(out x)")]
        [InlineData("flag && M2(out x) || (x = M3()) > 0")]
        [InlineData("(flag || M2(out x)) && (x = M3()) > 0")]
        [InlineData("M2(out x) && flag || (x = M3()) > 0")]
        [InlineData("flag && M2(out x) || (x = M3()) > 0 && flag")]
        public async Task IfElse_OverwrittenInCondition_LogicalOperators(string condition)
        {
            await TestInRegularAndScriptWithAllOptionsAsync(
$@"class C
{{
    int M(bool flag)
    {{
        int [|x|] = 1;
        if ({condition})
        {{
        }}
        else
        {{
        }}

        return x;
    }}

    bool M2(out int x) {{ x = 0; return true; }}
    int M3() => 0;
}}",
        $@"class C
{{
    int M(bool flag)
    {{
        int x;
        if ({condition})
        {{
        }}
        else
        {{
        }}

        return x;
    }}

    bool M2(out int x) {{ x = 0; return true; }}
    int M3() => 0;
}}");
            }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task ElselessIf(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    int M(bool flag)
    {
        int [|x|] = 1;
        if (flag)
        {
            x = 1;
        }

        return x;
    }
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        // For loop, assignment in body, read on back edge.
        [InlineData("for(i = 1; i < 10; i--)",
                        "M2(x); [|x|] = 1;")]
        // While loop, assignment in body, read on back edge.
        [InlineData("while(i++ < 10)",
                        "M2(x); [|x|] = 1;")]
        // Do loop, assignment in body, read on back edge.
        [InlineData("do",
                        "M2(x); [|x|] = 1;",
                    "while(i++ < 10);")]
        // Continue, read on back edge.
        [InlineData("while(i++ < 10)",
                        "M2(x); [|x|] = 1; if (flag) continue; x = 2;")]
        // Break.
        [InlineData(@"x = 0;
                      while(i++ < 10)",
                         "[|x|] = 1; if (flag) break; x = 2;")]
        // Assignment before loop, no overwrite on path where loop is never entered.
        [InlineData(@"[|x|] = 1;
                      while(i++ < 10)",
                         "x = 2;")]
        public async Task Loops_Overwritten_InSomeControlFlowPaths(string loopHeader, string loopBody, string loopFooter = null)
        {
            await TestMissingInRegularAndScriptWithAllOptionsAsync(
$@"class C
{{
    int M(int i, int x, bool flag)
    {{
        {loopHeader}
        {{
            {loopBody}
        }}
        {loopFooter ?? string.Empty}

        return x;
    }}

    void M2(int x) {{ }}
}}");
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        // For loop, assignment in body, re-assigned on back edge before read in loop and re-assigned at loop exit.
        [InlineData("for(i = 1; i < 10; i--)",
                        "x = 1; M2(x); [|x|] = 2;",
                    "x = 3;",
                    // Fixed code.
                    "for(i = 1; i < 10; i--)",
                        "x = 1; M2(x);",
                    "x = 3;")]
        // While loop, assignment in body, re-assigned on condition before read in loop and re-assigned at loop exit.
        [InlineData("while(i++ < (x = 10))",
                        "M2(x); [|x|] = 2;",
                    "x = 3;",
                    // Fixed code.
                    "while(i++ < (x = 10))",
                        "M2(x);",
                    "x = 3;")]
        // Assigned before loop, Re-assigned in continue, break paths and loop exit.
        [InlineData(@"[|x|] = 1;
                      i = 1;
                      while(i++ < 10)",
                        @"if(flag)
                            { x = 2; continue; }
                          else if(i < 5)
                            { break; }
                          else
                            { x = 3; }
                          M2(x);",
                      "x = 4;",
                    // Fixed code.
                    @"i = 1;
                      while(i++ < 10)",
                        @"if(flag)
                            { x = 2; continue; }
                          else if(i < 5)
                            { break; }
                          else
                            { x = 3; }
                          M2(x);",
                      "x = 4;")]
        public async Task Loops_Overwritten_InAllControlFlowPaths(string loopHeader, string loopBody, string loopFooter,
                                                                  string fixedLoopHeader, string fixedLoopBody, string fixedLoopFooter)
        {
            await TestInRegularAndScriptWithAllOptionsAsync(
$@"class C
{{
    int M(int i, int x, bool flag)
    {{
        {loopHeader}
        {{
            {loopBody}
        }}
        {loopFooter}

        return x;
    }}

    void M2(int x) {{ }}
}}",
$@"class C
{{
    int M(int i, int x, bool flag)
    {{
        {fixedLoopHeader}
        {{
            {fixedLoopBody}
        }}
        {fixedLoopFooter}

        return x;
    }}

    void M2(int x) {{ }}
}}");
        }
    }
}
