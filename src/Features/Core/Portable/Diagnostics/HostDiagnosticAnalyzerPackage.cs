// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    /// <summary>
    /// Information on vsix that contains diagnostic analyzers
    /// </summary>
    internal class HostDiagnosticAnalyzerPackage
    {
        public readonly string Name;
        public readonly ImmutableArray<(string Assembly, bool IsOptional)> AssembliesWithOptionalFlag;

        public HostDiagnosticAnalyzerPackage(string name, ImmutableArray<(string assembly, bool isOptional)> assemblies)
        {
            this.Name = name;
            this.AssembliesWithOptionalFlag = assemblies;
        }
    }
}
