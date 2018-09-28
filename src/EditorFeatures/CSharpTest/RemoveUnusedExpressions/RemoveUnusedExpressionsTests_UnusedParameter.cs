// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.Diagnostics;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;
using static Roslyn.Test.Utilities.TestHelpers;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.RemoveUnusedExpressions
{
    public partial class RemoveUnusedExpressionsTests : AbstractCSharpDiagnosticProviderBasedUserDiagnosticTest
    {
        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task Parameter_Unused_Suppressed()
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(int [|p|])
    {
    }
}", options: PreferNone);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_Used(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(int [|p|])
    {
        var x = p;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_Unused(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(int [|p|])
    {
    }
}", optionName,
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_WrittenOnly(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(int [|p|])
    {
        p = 1;
    }
}", optionName,
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_WrittenThenRead(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(int [|p|])
    {
        p = 1;
        var x = p;
    }
}", optionName,
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_WrittenOnAllControlPaths_BeforeRead(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(int [|p|], bool flag)
    {
        if (flag)
        {
            p = 0;
        }
        else
        {
            p = 1;
        }

        var x = p;
    }
}", optionName,
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_WrittenOnSomeControlPaths_BeforeRead(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(int [|p|], bool flag, bool flag2)
    {
        if (flag)
        {
            if (flag2)
            {
                p = 0;
            }
        }
        else
        {
            p = 1;
        }

        var x = p;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task OptionalParameter_Unused(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(int [|p|] = 0)
    {
    }
}", optionName,
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_UsedInConstructorInitializerOnly(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"
class B
{
    protected B(int p) { }
}

class C: B
{
    C(int [|p|])
    : base(p)
    {
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_NotUsedInConstructorInitializer_UsedInConstructorBody(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"
class B
{
    protected B(int p) { }
}

class C: B
{
    C(int [|p|])
    : base(0)
    {
        var x = p;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_UsedInConstructorInitializerAndConstructorBody(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"
class B
{
    protected B(int p) { }
}

class C: B
{
    C(int [|p|])
    : base(p)
    {
        var x = p;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task OptionalParameter_Used(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(int [|p = 0|])
    {
        var x = p;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task InParameter(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(in int [|p|])
    {
    }
}", optionName,
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task RefParameter_Unused(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(ref int [|p|])
    {
    }
}", optionName,
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task RefParameter_WrittenOnly(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(ref int [|p|])
    {
        p = 0;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task RefParameter_ReadOnly(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(ref int [|p|])
    {
        var x = p;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task RefParameter_ReadThenWritten(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(ref int [|p|])
    {
        var x = p;
        p = 1;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task RefParameter_WrittenAndThenRead(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(ref int [|p|])
    {
        p = 1;
        var x = p;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task RefParameter_WrittenTwiceNotRead(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(ref int [|p|])
    {
        p = 0;
        p = 1;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task OutParameter_Unused(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(out int [|p|])
    {
    }
}", optionName,
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task OutParameter_WrittenOnly(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(out int [|p|])
    {
        p = 0;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task OutParameter_WrittenAndThenRead(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(out int [|p|])
    {
        p = 0;
        var x = p;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task OutParameter_WrittenTwiceNotRead(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(out int [|p|])
    {
        p = 0;
        p = 1;
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_ExternMethod(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    [System.Runtime.InteropServices.DllImport(nameof(M))]
    static extern void M(int [|p|]);
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_AbstractMethod(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"abstract class C
{
    protected abstract void M(int [|p|]);
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_VirtualMethod(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    protected virtual void M(int [|p|])
    {
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_OverriddenMethod(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    protected virtual void M(int p)
    {
        var x = p;
    }
}

class D : C
{
    protected override void M(int [|p|])
    {
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_ImplicitInterfaceImplementationMethod(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"interface I
{
    void M(int p);
}
class C: I
{
    public void M(int [|p|])
    {
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_ExplicitInterfaceImplementationMethod(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"interface I
{
    void M(int p);
}
class C: I
{
    void I.M(int [|p|])
    {
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_IndexerMethod(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    int this[int [|p|]]
    {
        get { return 0; }
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_EventHandler_FirstParameter(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    public void MyHandler(object [|obj|], System.EventArgs args)
    {
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_EventHandler_SecondParameter(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    public void MyHandler(object obj, System.EventArgs [|args|])
    {
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_MethodUsedAsEventHandler(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"using System;

public delegate void MyDelegate(int x);

class C
{
    private event MyDelegate myDel;

    void M(C c)
    {
        c.myDel += Handler;
    }

    void Handler(int [|x|])
    {
    }
}", optionName);
        }
        
        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_CustomEventArgs(string optionName)
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    public class CustomEventArgs : System.EventArgs
    {
    }

    public void MyHandler(object [|obj|], CustomEventArgs args)
    {
    }
}", optionName);
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(@"[System.Diagnostics.Conditional(nameof(M))]")]
        [InlineData(@"[System.Obsolete]")]
        [InlineData(@"[System.Runtime.Serialization.OnDeserializingAttribute]")]
        [InlineData(@"[System.Runtime.Serialization.OnDeserializedAttribute]")]
        [InlineData(@"[System.Runtime.Serialization.OnSerializingAttribute]")]
        [InlineData(@"[System.Runtime.Serialization.OnSerializedAttribute]")]
        public async Task Parameter_MethodsWithSpecialAttributes(string attribute)
        {
            await TestDiagnosticMissingWithAllOptionsAsync(
$@"class C
{{
    {attribute}
    void M(int [|p|])
    {{
    }}
}}");
        }

        [ConditionalTheory(typeof(IsEnglishLocal)), Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Parameter_DiagnosticMessages(string optionName)
        {
            var source =
@"public class C
{
    // p1 is unused.
    // p2 is written before read.
    [|int M(int p1, int p2)
    {
        p2 = 0;
        return p2;
    }

    // p3 is unused parameter of a public API.
    // p4 is written before read parameter of a public API.
    public int M2(int p3, int p4)
    {
        p4 = 0;
        return p4;
    }|]
}";
            var testParameters = new TestParameters(options: GetOptions(optionName), retainNonFixableDiagnostics: true);
            using (var workspace = CreateWorkspaceFromOptions(source, testParameters))
            {
                var diagnostics = await GetDiagnosticsAsync(workspace, testParameters).ConfigureAwait(false);
                diagnostics.Verify(
                    Diagnostic("IDE0057", "p1").WithLocation(5, 15),
                    Diagnostic("IDE0057", "p2").WithLocation(5, 23),
                    Diagnostic("IDE0057", "p3").WithLocation(13, 23),
                    Diagnostic("IDE0057", "p4").WithLocation(13, 31));
                var sortedDiagnostics = diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToArray();

                Assert.Equal("Remove unused parameter 'p1'", sortedDiagnostics[0].GetMessage());
                Assert.Equal("Remove unused parameter 'p2', its initial value is never used", sortedDiagnostics[1].GetMessage());
                Assert.Equal("Remove unused parameter 'p3' if it is not part of a shipped public API", sortedDiagnostics[2].GetMessage());
                Assert.Equal("Remove unused parameter 'p4' if it is not part of a shipped public API, its initial value is never used", sortedDiagnostics[3].GetMessage());
            }
        }
    }
}
