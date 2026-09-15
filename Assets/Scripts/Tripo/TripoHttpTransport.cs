using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MusePico.Tripo
{
    /// <summary>
    /// The real transport, and the only type in the project that reads the API key.
    ///
    /// The key comes from the <c>TRIPO_API_KEY</c> environment variable (User scope on Windows)
    /// and is never written to the project, a scene, a ScriptableObject or the console. It is put
    /// straight into an <c>Authorization</c> header and is not exposed by any property —
    /// <see cref="HasCredentials"/> answers the only question callers legitimately have.
    ///
    /// Unity reads the environment at launch, so a key set after the Editor started is invisible
    /// until the Editor is restarted. <see cref="DescribeKeySource"/> says so rather than
    /// reporting a bare "not configured".
    ///
    /// <c>HttpClient</c> rather than <c>UnityWebRequest</c>: this runs from an EditorWindow, where
    /// UnityWebRequest has no reliable pump outside Play Mode. Nothing here touches a Unity API,
    /// so it is free to complete on a worker thread. If runtime-on-device generation is ever
    /// wanted, a UnityWebRequest implementation of <see cref="ITripoTransport"/> drops in beside
    /// this one — that is what the interface is for.
    /// </summary>
    public sealed class TripoHttpTransport : ITripoTransport, IDisposable
    {
        static readonly HttpClient Shared = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        readonly string _apiKey;
        readonly HttpClient _http;

        /// <summary>Only this origin receives the key. See <see cref="AuthorizedOrigin"/>.</summary>
        readonly string _serviceUrl;

        public TripoHttpTransport(string apiKeyOverride = null, HttpClient httpClient = null,
            string serviceUrl = TripoApi.GlobalBaseUrl)
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKeyOverride) ? ReadKeyFromEnvironment() : apiKeyOverride.Trim();
            _http = httpClient ?? Shared;
            _serviceUrl = serviceUrl;
        }

        public bool HasCredentials => !string.IsNullOrEmpty(_apiKey);

        /// <summary>Where the key came from, without any part of its value.</summary>
        public static string DescribeKeySource()
        {
            var key = ReadKeyFromEnvironment();
            if (!string.IsNullOrEmpty(key))
                return "TRIPO_API_KEY is set (" + key.Length + " characters).";
            return "TRIPO_API_KEY is not set in this process. Set it at User scope, then restart " +
                   "Unity — the Editor only reads the environment at launch.";
        }

        static string ReadKeyFromEnvironment()
        {
            var key = Environment.GetEnvironmentVariable(TripoApi.ApiKeyEnvironmentVariable);
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }

        public async Task<TripoHttpResponse> SendAsync(TripoHttpRequest request, CancellationToken cancellationToken)
        {
            try
            {
                using (var message = BuildMessage(request))
                using (var response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken)
                           .ConfigureAwait(false))
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return new TripoHttpResponse
                    {
                        Status = (int)response.StatusCode,
                        Bytes = bytes,
                        // Model downloads are binary; decoding them as text would be wasteful and
                        // meaningless, so only plausible text bodies become Body.
                        Body = LooksTextual(response.Content.Headers.ContentType?.MediaType)
                            ? System.Text.Encoding.UTF8.GetString(bytes)
                            : null,
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Never surface ex.ToString() wholesale: a request message carries the
                // Authorization header, and some handlers include it in their diagnostics.
                return new TripoHttpResponse { TransportError = ex.GetType().Name + ": " + ex.Message };
            }
        }

        HttpRequestMessage BuildMessage(TripoHttpRequest request)
        {
            var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);

            // The key goes to the service origin only. CDN download URLs are pre-signed.
            if (HasCredentials && AuthorizedOrigin.ShouldAuthorize(request.Url, _serviceUrl))
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            if (request.Body == null) return message;

            if (!string.IsNullOrEmpty(request.MultipartFileName))
            {
                // POST /v3/files: one part, named "file".
                var form = new MultipartFormDataContent();
                var part = new ByteArrayContent(request.Body);
                part.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrEmpty(request.ContentType) ? "application/octet-stream" : request.ContentType);
                form.Add(part, "file", request.MultipartFileName);
                message.Content = form;
            }
            else
            {
                var content = new ByteArrayContent(request.Body);
                content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType ?? "application/json");
                message.Content = content;
            }

            return message;
        }

        static bool LooksTextual(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType)) return true;
            return mediaType.StartsWith("text/") || mediaType.Contains("json") || mediaType.Contains("xml");
        }

        public void Dispose()
        {
            if (!ReferenceEquals(_http, Shared)) _http.Dispose();
        }
    }
}
