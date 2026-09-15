using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MusePico.Tripo
{
    /// <summary>
    /// The on-device transport: the same <see cref="ITripoTransport"/> contract as
    /// <see cref="TripoHttpTransport"/>, but over <see cref="UnityWebRequest"/>.
    ///
    /// Why two. <c>HttpClient</c> is the right tool in an EditorWindow, where UnityWebRequest has
    /// no reliable pump outside Play Mode. On an Android IL2CPP build the trade reverses:
    /// UnityWebRequest is the path Unity actually tests on device, it survives managed code
    /// stripping without a link.xml, and it reports download progress — which matters because a
    /// generated GLB is megabytes over a headset's Wi-Fi and a progress bar is the difference
    /// between "working" and "frozen" to someone wearing it.
    ///
    /// Must be called from the main thread: UnityWebRequest is a Unity API. The awaited
    /// continuations resume on Unity's synchronization context, so callers stay on the main
    /// thread too and may touch the scene directly.
    /// </summary>
    public sealed class TripoWebRequestTransport : ITripoTransport
    {
        readonly string _apiKey;

        /// <summary>
        /// The service this key belongs to. Requests to any other origin go unauthenticated — a
        /// pre-signed CDN download must never carry a provider key.
        /// </summary>
        readonly string _serviceUrl;

        /// <summary>Fraction of the current body transferred, 0..1. Only meaningful during a download.</summary>
        public float Progress { get; private set; }

        /// <summary>Seconds before a single request is abandoned. Generation is polled, so this covers one call only.</summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <param name="serviceUrl">
        /// Any URL on the service this key authenticates against — the base URL or an endpoint.
        /// Only requests to that same origin receive the Authorization header. <b>Required in
        /// substance:</b> the default is Tripo's, so a caller for another provider that forgets
        /// this gets 401s that look like a bad key rather than a missing header.
        /// </param>
        public TripoWebRequestTransport(string apiKey, string serviceUrl = TripoApi.GlobalBaseUrl)
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            _serviceUrl = serviceUrl;
        }

        public bool HasCredentials => !string.IsNullOrEmpty(_apiKey);

        public async Task<TripoHttpResponse> SendAsync(TripoHttpRequest request, CancellationToken cancellationToken)
        {
            using (var www = Build(request))
            {
                www.timeout = TimeoutSeconds;

                var operation = www.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);

                using (cancellationToken.Register(() =>
                {
                    // Abort() makes the operation complete with ConnectionError, which unblocks
                    // the await; the OperationCanceledException below is what the caller sees.
                    if (!www.isDone) www.Abort();
                }))
                {
                    while (!operation.isDone)
                    {
                        Progress = www.downloadProgress;
                        await completion.Task.ConfigureAwait(true);
                    }
                }

                Progress = 1f;
                cancellationToken.ThrowIfCancellationRequested();

                if (www.result == UnityWebRequest.Result.ConnectionError ||
                    www.result == UnityWebRequest.Result.DataProcessingError)
                {
                    return new TripoHttpResponse { TransportError = www.error ?? "connection failed" };
                }

                var bytes = www.downloadHandler?.data;
                var contentType = www.GetResponseHeader("Content-Type");

                return new TripoHttpResponse
                {
                    Status = (int)www.responseCode,
                    Bytes = bytes,
                    // A model download is binary; decoding megabytes of GLB as UTF-8 would cost
                    // real time and produce nothing anyone reads.
                    Body = LooksTextual(contentType) ? www.downloadHandler?.text : null,
                };
            }
        }

        UnityWebRequest Build(TripoHttpRequest request)
        {
            UnityWebRequest www;

            if (request.Body != null && !string.IsNullOrEmpty(request.MultipartFileName))
            {
                var form = new WWWForm();
                form.AddBinaryData("file", request.Body, request.MultipartFileName,
                    string.IsNullOrEmpty(request.ContentType) ? "application/octet-stream" : request.ContentType);
                www = UnityWebRequest.Post(request.Url, form);
            }
            else if (request.Body != null)
            {
                www = new UnityWebRequest(request.Url, request.Method)
                {
                    uploadHandler = new UploadHandlerRaw(request.Body),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                www.SetRequestHeader("Content-Type", request.ContentType ?? "application/json");
            }
            else
            {
                www = new UnityWebRequest(request.Url, request.Method)
                {
                    downloadHandler = new DownloadHandlerBuffer(),
                };
            }

            // The key goes to the service's own origin and nowhere else. CDN download URLs are
            // pre-signed and must not carry it.
            if (HasCredentials && AuthorizedOrigin.ShouldAuthorize(request.Url, _serviceUrl))
                www.SetRequestHeader("Authorization", "Bearer " + _apiKey);

            return www;
        }

        static bool LooksTextual(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType)) return true;
            return mediaType.StartsWith("text/") || mediaType.Contains("json") || mediaType.Contains("xml");
        }
    }
}
