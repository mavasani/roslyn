// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Suppression
{
    /// <summary>
    /// Service to allow adding or removing bulk suppressions (in source or suppressions file).
    /// </summary>
    internal interface IVisualStudioSuppressionFixService
    {
        void AddSuppressions(bool selectedErrorListEntriesOnly, bool suppressInSource);
        void RemoveSuppressions(bool selectedErrorListEntriesOnly);
    }
}
