﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem.Interop;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem.Legacy
{
    internal partial class AbstractLegacyProject : ICompilerOptionsHostObject
    {
        int ICompilerOptionsHostObject.SetCompilerOptions(string compilerOptions, out bool supported)
        {
            SetArgumentsAndUpdateOptions(compilerOptions);
            supported = true;
            return VSConstants.S_OK;
        }
    }
}
