using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Implementation.Classification;
using Microsoft.CodeAnalysis.Editor.Shared.Tagging;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Editor.Tagging;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Text.Shared.Extensions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Microsoft.CodeAnalysis.Classification;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.CobyIntegration
{
    [Export(typeof(ITaggerProvider))]
    [TagType(typeof(IClassificationTag))]
    [ContentType(ContentTypeNames.CSharpContentType)]
    [ContentType(ContentTypeNames.VisualBasicContentType)]
    internal class CobyTagger : ForegroundThreadAffinitizedObject, ITaggerProvider
    {
        private readonly ClassificationTypeMap _typeMap;

        [ImportingConstructor]
        public CobyTagger(
            IForegroundNotificationService notificationService,
            ClassificationTypeMap typeMap)
        {
            _typeMap = typeMap;
        }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            this.AssertIsForeground();
            return new Tagger(this, buffer) as ITagger<T>;
        }

        private class Tagger : ForegroundThreadAffinitizedObject, ITagger<IClassificationTag>
        {
            private readonly CobyTagger _owner;
            private readonly ITextBuffer _subjectBuffer;

            private TagSpanIntervalTree<IClassificationTag> _cachedTags_doNotAccessDirectly;
            private ITextSnapshot _cachedSnapshot_doNotAccessDirectly;

            public Tagger(CobyTagger owner, ITextBuffer subjectBuffer)
            {
                _owner = owner;
                _subjectBuffer = subjectBuffer;
            }

            private TagSpanIntervalTree<IClassificationTag> CachedTags
            {
                get
                {
                    this.AssertIsForeground();
                    return _cachedTags_doNotAccessDirectly;
                }

                set
                {
                    this.AssertIsForeground();
                    _cachedTags_doNotAccessDirectly = value;
                }
            }

            private ITextSnapshot CachedSnapshot
            {
                get
                {
                    this.AssertIsForeground();
                    return _cachedSnapshot_doNotAccessDirectly;
                }

                set
                {
                    this.AssertIsForeground();
                    _cachedSnapshot_doNotAccessDirectly = value;
                }
            }

            public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

            public IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
            {
                this.AssertIsForeground();

                if (spans.Count == 0)
                {
                    return Array.Empty<ITagSpan<IClassificationTag>>();
                }

                var firstSpan = spans.First();
                var snapshot = firstSpan.Snapshot;
                Debug.Assert(snapshot.TextBuffer == _subjectBuffer);

                var cachedSnapshot = this.CachedSnapshot;

                if (snapshot != cachedSnapshot)
                {
                    // Our cache is not there, or is out of date.  We need to compute the up to date 
                    // results.
                    var document = snapshot.GetOpenDocumentInCurrentContextWithChanges();
                    if (document == null)
                    {
                        return Array.Empty<ITagSpan<IClassificationTag>>();
                    }

                    Workspace workspace;
                    if (!Workspace.TryGetWorkspace(_subjectBuffer.AsTextContainer(), out workspace) ||
                        !(workspace is CobyWorkspace))
                    {
                        return Array.Empty<ITagSpan<IClassificationTag>>();
                    }

                    var url = CobyWorkspace.GetCompoundUrl(document);
                    var result = CobyServices.GetFileEntityAsync(Consts.CodeBase, url.fileUid, CancellationToken.None).WaitAndGetResult(CancellationToken.None);
                    if (result == null)
                    {
                        return Array.Empty<ITagSpan<IClassificationTag>>();
                    }

                    CachedSnapshot = snapshot;
                    CachedTags = new TagSpanIntervalTree<IClassificationTag>(
                        snapshot.TextBuffer, SpanTrackingMode.EdgeExclusive, result.referenceAnnotation.Where(a => a.symbolType == "type").Select(a => new TagSpan<IClassificationTag>(
                           GetTextSpan(a, snapshot), new ClassificationTag(_owner._typeMap.GetClassificationType(ClassificationTypeNames.ClassName)))));
                }

                if (this.CachedTags == null)
                {
                    return Array.Empty<ITagSpan<IClassificationTag>>();
                }

                return this.CachedTags.GetIntersectingTagSpans(spans);
            }

            private SnapshotSpan GetTextSpan(CobyServices.ReferenceAnnotation annotation, ITextSnapshot snapshot)
            {
                var range = annotation.range;
                var span = TextSpan.FromBounds(snapshot.GetPosition(Math.Max(range.startLineNumber - 1, 0), Math.Max(range.startColumn - 1, 0)), snapshot.GetPosition(Math.Max(range.endLineNumber - 1, 0), Math.Max(range.endColumn - 1, 0)));
                return span.ToSnapshotSpan(snapshot);
            }
        }
    }
}
