// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CodeFixes.Suppression
{
    internal abstract partial class AbstractSuppressionCodeFixProvider : ISuppressionFixProvider
    {
        internal abstract class AbstractGlobalSuppressMessageCodeAction : AbstractSuppressionCodeAction
        {
            private readonly Project _project;

            protected AbstractGlobalSuppressMessageCodeAction(AbstractSuppressionCodeFixProvider fixer, Project project)
                : base(fixer, title: FeaturesResources.SuppressWithGlobalSuppressMessage)
            {
                _project = project;
            }

            protected sealed override async Task<IEnumerable<CodeActionOperation>> ComputeOperationsAsync(CancellationToken cancellationToken)
            {
                var changedSuppressionDocument = await GetChangedSuppressionDocumentAsync(cancellationToken).ConfigureAwait(false);
                return new CodeActionOperation[]
                {
                    new ApplyChangesOperation(changedSuppressionDocument.Project.Solution),
                    new OpenDocumentOperation(changedSuppressionDocument.Id, activateIfAlreadyOpen: true),
                    new NavigationOperation(changedSuppressionDocument.Id, position: 0)
                };
            }

            protected abstract Task<Document> GetChangedSuppressionDocumentAsync(CancellationToken cancellationToken);
            public override bool SupportsFixAllOccurrences => true;

            protected async Task<Document> GetOrCreateSuppressionsDocumentAsync(CancellationToken c)
            {
                int index = 1;
                var suppressionsFileName = s_globalSuppressionsFileName + Fixer.DefaultFileExtension;

                Document suppressionsDoc = null;
                while (suppressionsDoc == null)
                {
                    var hasDocWithSuppressionsName = false;
                    foreach (var d in _project.Documents)
                    {
                        if (d.Name == suppressionsFileName)
                        {
                            // Existing global suppressions file, see if this file only has global assembly attributes.
                            hasDocWithSuppressionsName = true;

                            var t = await d.GetSyntaxTreeAsync(c).ConfigureAwait(false);
                            var r = await t.GetRootAsync(c).ConfigureAwait(false);
                            if (r.ChildNodes().All(n => Fixer.IsAttributeListWithAssemblyAttributes(n)))
                            {
                                suppressionsDoc = d;
                                break;
                            }
                        }
                    }

                    if (suppressionsDoc == null)
                    {
                        if (hasDocWithSuppressionsName)
                        {
                            index++;
                            suppressionsFileName = s_globalSuppressionsFileName + index.ToString() + Fixer.DefaultFileExtension;
                        }
                        else
                        {
                            // Create an empty global suppressions file.
                            suppressionsDoc = _project.AddDocument(suppressionsFileName, string.Empty);
                        }
                    }
                }

                return suppressionsDoc;
            }
        }
    }
}
