// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Suppression
{
    internal interface IVisualStudioBulkSuppressionService
    {
        void AddSuppressions(bool selectedErrorListEntriesOnly, bool suppressInSuppressionFile);
        void RemoveSuppressions(bool selectedErrorListEntriesOnly);
    }
}
