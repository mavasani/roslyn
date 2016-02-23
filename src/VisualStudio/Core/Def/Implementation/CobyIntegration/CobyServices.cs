using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Linq;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.CobyIntegration
{
    /// <summary>
    /// service to ask Coby.
    /// 
    /// REVIEW: we need API to get entity from symbol without knowing id. 
    ///         also, we need a service to find out code base from given symbol to properly support GoToDefinition from Roslyn world. (gotodef from roslyn file not coby file)
    /// </summary>
    internal static class CobyServices
    {
        public enum SearchType
        {
            File,
            Symbol
        }

        public static async Task<IEnumerable<SymbolReference>> GetEntityReferencesAsync(string repo, string id, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/v1.0/reference?repo={WebUtility.UrlEncode(repo)}&id={WebUtility.UrlEncode(id)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<IEnumerable<SymbolReference>>().ConfigureAwait(false);
            }
        }

        public static async Task<FileResponse> GetFileEntityAsync(string repo, string filePath, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/v1.0/entities?repo={WebUtility.UrlEncode(repo)}&filePath={WebUtility.UrlEncode(filePath)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                // REVIEW: How can we have multiple files with same ID in the repo?
                var fileResponses = await response.Content.ReadAsAsync<List<FileResponse>>().ConfigureAwait(false);
                return fileResponses.FirstOrDefault();
            }
        }

        public static async Task<SourceResponse> GetContentBySymbolIdAsync(string repo, string symbolId, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/v1.0/source?repo={WebUtility.UrlEncode(repo)}&symbolId={WebUtility.UrlEncode(symbolId)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<SourceResponse>().ConfigureAwait(false);
            }
        }

        public static async Task<SourceResponse> GetContentByFileIdAsync(string repo, string fileId, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/v1.0/source?repo={WebUtility.UrlEncode(repo)}&fileId={WebUtility.UrlEncode(fileId)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<SourceResponse>().ConfigureAwait(false);
            }
        }

        public static async Task<SourceResponse> GetContentByFilePathAsync(string repo, string version, string filePath, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/v1.0/source?repo={WebUtility.UrlEncode(repo)}&branch={WebUtility.UrlEncode(version)}&filePath={WebUtility.UrlEncode(filePath)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<SourceResponse>().ConfigureAwait(false);
            }
        }

        public static async Task<IEnumerable<SearchResponse>> SearchAsync(string repo, SearchType searchType, string term, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/v1.0/search?repo={WebUtility.UrlEncode(repo)}&searchType={WebUtility.UrlEncode(searchType.ToString())}&term={WebUtility.UrlEncode(term)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<IEnumerable<SearchResponse>>().ConfigureAwait(false);
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.BaseAddress = new Uri("https://semanticstoreservice20160104030741.azurewebsites.net");

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }

        public static bool IsVisualBasicProject(CompoundUrl url)
        {
            var extension = PathUtilities.GetExtension(url.filePath);
            return extension != null && extension.Equals(".vb", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFileResult(SearchResponse response)
        {
            return response.resultType == "file";
        }

        public static Task<FileResponse> GetFileEntityAsync(Document document, CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(document.Project.Solution.Workspace is CobyWorkspace);

            var url = CobyWorkspace.GetCompoundUrl(document);
            return GetFileEntityAsync(Consts.Repo, url.filePath, CancellationToken.None);
        }

        public static Annotation GetMatchingAnnotation(Document document, int position, CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(document.Project.Solution.Workspace is CobyWorkspace);

            var fileResult = GetFileEntityAsync(document, cancellationToken).WaitAndGetResult(cancellationToken);
            if (fileResult == null)
            {
                return null;
            }

            return GetMatchingAnnotation(document, position, fileResult, cancellationToken);
        }

        public static Annotation GetMatchingAnnotation(Document document, int position, FileResponse fileResult, CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(document.Project.Solution.Workspace is CobyWorkspace);
            Contract.ThrowIfNull(fileResult);

            // REVIEW: all of these WaitAndGetResult is really bad thing to do.
            var text = document.GetTextAsync(cancellationToken).WaitAndGetResult(cancellationToken);

            var zeroBasedPosition = text.Lines.GetLinePosition(position);
            var oneBasedPosition = new LinePosition(zeroBasedPosition.Line + 1, zeroBasedPosition.Character + 1);

            Func<IEnumerable<Annotation>, Annotation> getMatchingAnnotation = annotations =>
                annotations.FirstOrDefault(a => new LinePosition(a.range.startLineNumber, a.range.startColumn) <= oneBasedPosition && oneBasedPosition <= new LinePosition(a.range.endLineNumber, a.range.endColumn));

            return getMatchingAnnotation(fileResult.referenceAnnotation) ?? getMatchingAnnotation(fileResult.declarationAnnotation);
        }

        public static TextSpan GetSourceSpan(SymbolReference referenceResponse, Func<Document> getDocument)
        {
            return GetSourceSpan(referenceResponse.trange, getDocument);
        }

        public static TextSpan GetSourceSpan(SourceResponse sourceResponse, Func<Document> getDocument)
        {
            return GetSourceSpan((sourceResponse?.range).GetValueOrDefault(), getDocument);
        }

        public static TextSpan GetSourceSpan(Range range, Func<Document> getDocument)
        {
            // REVIEW: this is expensive, but there is no other way in current coby design. we need stream based point as well as linecolumn based range.
            if (range.Equals(default(Range)))
            {
                return new TextSpan(0, 0);
            }
            else
            {
                // REVIEW: this is bad.
                var document = getDocument();
                var text = document.State.GetText(CancellationToken.None);
                if (text?.Length == 0)
                {
                    return new TextSpan(0, 0);
                }

                // Coby is 1 based. Roslyn is 0 based.
                return text.Lines.GetTextSpan(
                    new LinePositionSpan(
                        new LinePosition(Math.Max(range.startLineNumber - 1, 0), Math.Max(range.startColumn - 1, 0)),
                        new LinePosition(Math.Max(range.endLineNumber - 1, 0), Math.Max(range.endColumn - 1, 0))));
            }
        }

        public class SearchResponse
        {
            public string id;
            public string name;
            public string fullName;
            public string resultType;
            public string url;
            public CompoundUrl compoundUrl;
            public Range range;
            public string fileName;
            public string tags;
        }

        public class SourceResponse
        {
            public string url;
            public CompoundUrl compoundUrl;
            public Range range;
            public string displayName;
            public string mimeType;
            public string contents;            
        }

        public class FileResponse
        {
            public string id;
            public string filePath;
            public string repository;
            public string version;
            public List<DeclarationAnnotation> declarationAnnotation;
            public List<ReferenceAnnotation> referenceAnnotation;
        }

        public struct Range
        {
            public int startLineNumber;
            public int startColumn;
            public int endLineNumber;
            public int endColumn;
        }

        public abstract class Annotation
        {
            public string symbolId;
            public string symbolType;
            public string refType;
            public string declAssembly;
            public string label;
            public string hover;
            public Range range;
        }

        public class DeclarationAnnotation : Annotation
        {
            public string depth;
            public string glyph;
        }

        public class ReferenceAnnotation : Annotation
        {
            public string declFile;
        }

        public struct SymbolReference
        {
            public string refType;
            public string tref;
            public Range trange;
            public string preview;
        }

        public struct CompoundUrl
        {
            public string filePath;
            public string repository;
            public string version;
            public string fileUid;
        }
    }
}
