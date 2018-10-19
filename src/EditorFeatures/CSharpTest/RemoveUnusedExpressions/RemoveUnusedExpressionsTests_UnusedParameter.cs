// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.Diagnostics;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;
using static Roslyn.Test.Utilities.TestHelpers;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.RemoveUnusedExpressions
{
    public partial class RemoveUnusedExpressionsTests : AbstractCSharpDiagnosticProviderBasedUserDiagnosticTest
    {
        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        public async Task UnusedParameter_Suppressed()
        {
            await TestDiagnosticMissingAsync(
@"class C
{
    void M(int [|p|])
    {
    }
}", new TestParameters(options: PreferNone));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Used_Parameter(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(int [|p|])
    {
        var x = p;
    }
}", new TestParameters(options: GetOptions(optionName)),
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Unused_RefParameter(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(ref int [|p|])
    {
    }
}", new TestParameters(options: GetOptions(optionName)),
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Unused_OutParameter(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(out int [|p|])
    {
    }
}", new TestParameters(options: GetOptions(optionName)),
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Unused_Parameter(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(int [|p|])
    {
    }
}", new TestParameters(options: GetOptions(optionName)),
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task In_Parameter(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(in int [|p|])
    {
    }
}", new TestParameters(options: GetOptions(optionName)),
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsRemoveUnusedExpressions)]
        [InlineData(nameof(PreferDiscard))]
        [InlineData(nameof(PreferUnusedLocal))]
        public async Task Unread_Parameter(string optionName)
        {
            await TestDiagnosticsAsync(
@"class C
{
    void M(int [|p|])
    {
        p = 1;
    }
}", new TestParameters(options: GetOptions(optionName)),
    Diagnostic(IDEDiagnosticIds.ParameterCanBeRemovedDiagnosticId));
        }

    }
}
