﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Peek
{
    internal class ExternalFilePeekableItem : PeekableItem
    {
        private readonly FileLinePositionSpan _span;
        private readonly IPeekRelationship _relationship;

        public ExternalFilePeekableItem(
            FileLinePositionSpan span,
            IPeekRelationship relationship,
            IPeekResultFactory peekResultFactory)
            : base(peekResultFactory)
        {
            _span = span;
            _relationship = relationship;
        }

        public override IEnumerable<IPeekRelationship> Relationships
        {
            get { return SpecializedCollections.SingletonEnumerable(_relationship); }
        }

        public override IPeekResultSource GetOrCreateResultSource(string relationshipName)
        {
            return new ResultSource(this);
        }

        private sealed class ResultSource : IPeekResultSource
        {
            private readonly ExternalFilePeekableItem _peekableItem;

            public ResultSource(ExternalFilePeekableItem peekableItem)
            {
                _peekableItem = peekableItem;
            }

            public void FindResults(string relationshipName, IPeekResultCollection resultCollection, CancellationToken cancellationToken, IFindPeekResultsCallback callback)
            {
                if (relationshipName != _peekableItem._relationship.Name)
                {
                    return;
                }

#pragma warning disable CA2000 // Dispose objects before losing scope - dispose ownership transfer to caller
                // Audit suppression: https://github.com/dotnet/roslyn/issues/25880
                resultCollection.Add(PeekHelpers.CreateDocumentPeekResult(_peekableItem._span.Path, _peekableItem._span.Span, _peekableItem._span.Span, _peekableItem.PeekResultFactory));
#pragma warning restore CA2000 // Dispose objects before losing scope
            }
        }
    }
}
