// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;
using Xunit;

using static Microsoft.CodeAnalysis.CommonDiagnosticAnalyzers;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public partial class DiagnosticSuppressorTests : CompilingTestBase
    {
        [Fact, WorkItem(20242, "https://github.com/dotnet/roslyn/issues/20242")]
        public void TestUnusedFieldDiagnosticSuppressor()
        {
            string source = @"
internal class C
{
    // warning CS0169: The field 'C.field' is never used
    private readonly int f;
}";

            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CreateCompilationWithMscorlib45(new[] { tree });
            compilation.VerifyDiagnostics();

            var analyzers = new DiagnosticAnalyzer[] { new UnusedFieldDiagnosticSuppressor() };
            compilation.VerifyAnalyzerDiagnostics(analyzers);
        }
    }
}
