using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace MuseXR.DynamicWorlds
{
    /// <summary>
    /// The World Labs calls over UnityWebRequest — the path Unity supports on an Android IL2CPP
    /// player (same reasoning as TripoWebRequestTransport). Main thread only; awaits resume there.
    /// The key goes on API calls only: asset downloads are pre-signed CDN URLs and never carry it.
    /// Nothing here retries. A retried worlds:generate is a second paid world.
    /// </summary>
    public sealed class MarbleWebClient
    {
        readonly string _key;
        public int TimeoutSeconds { get; set; } = 180;

        public MarbleWebClient(string key) => _key = key;

        public Task<string> PostJsonAsync(string path, string body)
        {
            var req = new UnityWebRequest(MarbleWire.BaseUrl + path, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader(MarbleWire.KeyHeader, _key);
            return SendText(req);
        }

        public Task<string> GetJsonAsync(string path)
        {
            var req = UnityWebRequest.Get(MarbleWire.BaseUrl + path);
            req.SetRequestHeader(MarbleWire.KeyHeader, _key);
            return SendText(req);
        }

        /// <summary>Downloads a pre-signed asset URL. No key.</summary>
        public async Task<byte[]> DownloadAsync(string url)
        {
            using var req = UnityWebRequest.Get(url);
            req.timeout = TimeoutSeconds;
            await Send(req);
            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"download failed: {req.responseCode} {req.error}");
            return req.downloadHandler.data;
        }

        async Task<string> SendText(UnityWebRequest req)
        {
            using (req)
            {
                req.timeout = TimeoutSeconds;
                await Send(req);
                string text = req.downloadHandler?.text ?? "";
                if (req.result != UnityWebRequest.Result.Success)
                    throw new Exception($"{req.method} {req.url.Replace(MarbleWire.BaseUrl, "")} -> {req.responseCode} {req.error}: {Trim(text)}");
                return text;
            }
        }

        static Task Send(UnityWebRequest req)
        {
            var done = new TaskCompletionSource<bool>();
            req.SendWebRequest().completed += _ => done.TrySetResult(true);
            return done.Task;
        }

        static string Trim(string s) => s.Length > 300 ? s.Substring(0, 300) + "…" : s;
    }
}
