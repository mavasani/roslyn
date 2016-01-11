using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

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
        public static class EntityTypes
        {
            public const string File = "File";
            public const string Symbol = "Symbol";
        }

        public static async Task<IEnumerable<SymbolReference>> GetEntityReferencesAsync(string codeBase, string uid, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/ArcusGraphReferences?codeBase={WebUtility.UrlEncode(codeBase)}&uid={WebUtility.UrlEncode(uid)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<IEnumerable<SymbolReference>>().ConfigureAwait(false);
            }
        }

        public static async Task<IEnumerable<FileResult>> GetFileEntitiesAsync(string codeBase, string entityType, string repository, string branch, string filePath, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/ArcusGraphEntities?codeBase={WebUtility.UrlEncode(codeBase)}&entityType={WebUtility.UrlEncode(entityType)}&repository={WebUtility.UrlEncode(repository)}&version={WebUtility.UrlEncode(branch)}&filePath={WebUtility.UrlEncode(filePath)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<IEnumerable<FileResult>>().ConfigureAwait(false);
            }
        }

        public static async Task<FileResult> GetFileEntityAsync(string codeBase, string uid, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/ArcusGraphEntities?codeBase={WebUtility.UrlEncode(codeBase)}&uid={WebUtility.UrlEncode(uid)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<FileResult>().ConfigureAwait(false);
            }
        }

        public static async Task<SourceResult> GetContentAsync(string codeBase, string repository, string branch, string filePath, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/CobySource?codeBase={WebUtility.UrlEncode(codeBase)}&repository={WebUtility.UrlEncode(repository)}&branch={WebUtility.UrlEncode(branch)}&filePath={WebUtility.UrlEncode(filePath)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<SourceResult>().ConfigureAwait(false);
            }
        }

        public static async Task<SourceResult> GetContentAsync(string codeBase, string fileUid, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/CobySource?codeBase={WebUtility.UrlEncode(codeBase)}&fileUid={WebUtility.UrlEncode(fileUid)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<SourceResult>().ConfigureAwait(false);
            }
        }

        public static async Task<IEnumerable<SearchResult>> SearchAsync(string codeBase, string entityType, string term, CancellationToken cancellationToken)
        {
            using (var client = CreateClient())
            {
                var url = $"api/CobySearch?codeBase={WebUtility.UrlEncode(codeBase)}&entityType={WebUtility.UrlEncode(entityType)}&term={WebUtility.UrlEncode(term)}";
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsAsync<IEnumerable<SearchResult>>().ConfigureAwait(false);
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.BaseAddress = new Uri("https://cobyservices.cloudapp.net");

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }

        public class SearchResult
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

        public class SourceResult
        {
            public string url;
            public CompoundUrl compoundUrl;
            public string displayName;
            public string mimeType;
            public string contents;
        }

        public class FileResult
        {
            public string uid;
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
