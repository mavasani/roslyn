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
using static Roslyn.Test.Utilities.TestHelpers;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.RemoveUnusedExpressions
{
    public class RemoveUnusedExpressionsTests : AbstractCSharpDiagnosticProviderBasedUserDiagnosticTest
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

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task IfElse_OverrwrittenInBothBranches_AssignedBefore(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M(bool flag)
    {
        int [|x|] = 1;
        if (flag)
        {
            x = 2;
        }
        else
        {
            x = 3;
        }
    }
}",
@"class C
{
    void M(bool flag)
    {
        int x;
        if (flag)
        {
            x = 2;
        }
        else
        {
            x = 3;
        }
    }
}", options: GetOptions(optionName));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task IfElse_OverrwrittenInBothBranches_AssignedInCondition_PreferDiscard()
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
        public async Task IfElse_OverrwrittenInBothBranches_DeclaredInCondition_PreferDiscard()
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
        public async Task IfElse_OverrwrittenInBothBranches_AssignedInCondition_ReadAfter_PreferUnusedLocal()
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
        public async Task IfElse_OverrwrittenInBothBranches_AssignedInCondition_NoReads_PreferUnusedLocal()
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
        public async Task IfElse_OverrwrittenInBothBranches_DeclaredInCondition_ReadAfter_PreferUnusedLocal()
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
        public async Task IfElse_OverrwrittenInBothBranches_DeclaredInCondition_NoReads_PreferUnusedLocal()
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
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task IfElse_OverrwrittenInCondition(string optionName)
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M(bool flag)
    {
        int [|x|] = 1;
        if (M2(out x))
        {
        }
        else
        {
        }
    }

    bool M2(out int x) => x = 0;
}",
@"class C
{
    void M(bool flag)
    {
        int x;
        if (M2(out x))
        {
        }
        else
        {
        }
    }

    bool M2(out int x) => x = 0;
}", options: GetOptions(optionName));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task IfElse_OverrwrittenInIfBranch(string optionName)
        {
            await TestMissingInRegularAndScriptAsync(
@"class C
{
    int M(bool flag)
    {
        int [|x|] = 1;
        if (flag)
        {
            x = 2;
        }
        else
        {
        }

        return x;
    }
}", new TestParameters(options: GetOptions(optionName)));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData("flag && M2(out x)", "", "x = 2")]
        [InlineData("flag || M2(out x)", "x = 2", "")]
        public async Task IfElse_OverrwrittenInOneBranchCodeAndOtherBranchCondition(string condition, string ifBranchCode, string elseBranchCode)
        {
            foreach (var options in new[] { PreferDiscard, PreferUnusedLocal })
            {
                await TestInRegularAndScriptAsync(
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
}}", options: options);
            }
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData("flag || M2(out x)")]
        [InlineData("flag && M2(out x)")]
        [InlineData("flag || M2(out x) && (x = M3()) > 0")]
        public async Task IfElse_MayBeOverrwrittenInCondition_LogicalOperators(string condition)
        {
            foreach (var options in new[] { PreferDiscard, PreferUnusedLocal })
            {
                await TestMissingInRegularAndScriptAsync(
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
}}", new TestParameters(options: options));
        }
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
        public async Task IfElse_OverrwrittenInCondition_LogicalOperators(string condition)
        {
            foreach (var options in new[] { PreferDiscard, PreferUnusedLocal })
            {
                await TestInRegularAndScriptAsync(
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
}}", options: options);
            }
        }
    }
}
