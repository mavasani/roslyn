// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.Shared.Options
{
    internal enum BackgroundAnalysisScope
    {
        None,
        ActiveFile,
        OpenFiles,
        FullSolution,
    }
}
