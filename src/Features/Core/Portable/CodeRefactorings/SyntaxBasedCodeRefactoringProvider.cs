// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading;

namespace Microsoft.CodeAnalysis.CodeRefactorings
{
    internal abstract class SyntaxBasedCodeRefactoringProvider : CodeRefactoringProvider
    {
        internal abstract bool IsRefactoringCandidate(SyntaxNode node, Document document, CancellationToken cancellationToken);
    }
}
