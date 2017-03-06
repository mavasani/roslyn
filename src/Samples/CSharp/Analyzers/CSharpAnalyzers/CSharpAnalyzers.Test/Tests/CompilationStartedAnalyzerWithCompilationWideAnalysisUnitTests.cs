// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestHelper;

namespace CSharpAnalyzers.Test
{
    [TestClass]
    public class CompilationStartedAnalyzerWithCompilationWideAnalysisUnitTests
        : DiagnosticVerifier
    {
        [TestMethod]
        public void Test1()
        {
            var test = @"
namespace MyNamespace
{
    public class InsecureMethodAttribute : System.Attribute { }

    public interface ISecureType { }

    public interface IInsecureInterface
    {
        [InsecureMethodAttribute]
        void F();
    }

    class MyInterfaceImpl1 : IInsecureInterface
    {
        public void F() {}
    }

    class MyInterfaceImpl2 : IInsecureInterface, ISecureType
    {
        public void F() {}
    }

    class MyInterfaceImpl3 : ISecureType
    {
        public void F() {}
    }
}";
            var expected = new DiagnosticResult
            {
                Id = DiagnosticIds.CompilationStartedAnalyzerWithCompilationWideAnalysisRuleId,
                Message = string.Format(Resources.CompilationStartedAnalyzerWithCompilationWideAnalysisMessageFormat, "MyInterfaceImpl2", CompilationStartedAnalyzerWithCompilationWideAnalysis.SecureTypeInterfaceName, "IInsecureInterface"),
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[]
                    {
                        new DiagnosticResultLocation("Test0.cs", 19, 11)
                    }
            };

            VerifyCSharpDiagnostic(test, expected);
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new CompilationStartedAnalyzerWithCompilationWideAnalysis();
        }
    }
}