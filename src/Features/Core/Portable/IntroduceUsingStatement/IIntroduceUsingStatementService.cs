// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;

namespace Microsoft.CodeAnalysis.IntroduceUsingStatement
{
    internal interface IIntroduceUsingStatementService : ILanguageService
    {
        /// <summary>
        /// Returns true if <paramref name="disposableCreation"/> is local declaration statement
        /// with a disposable object initializer OR an assignment statement
        /// with a local or a parameter as assignment target and a disposable object as assignment value,
        /// such that it can be wrapped inside a using statement/block.
        /// </summary>
        Task<bool> CanIntroduceUsingStatementAsync(Document document, SyntaxNode disposableCreation, CancellationToken cancellationToken);

        /// <summary>
        /// Wraps the given <paramref name="disposableCreation"/> within a using statement/block.
        /// Only applicable if <see cref="CanIntroduceUsingStatementAsync"/>
        /// returned <code>true</code>. If not, then the original document will be returned unchanged.
        /// </summary>
        Task<Document> IntroduceUsingStatementAsync(Document document, SyntaxNode disposableCreation, CancellationToken cancellationToken);
    }
}
