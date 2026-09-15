using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MusePico.Tripo
{
    /// <summary>
    /// A complete Tripo **v3** client. Everything here is protocol work — building a URL, building
    /// a body, reading the <c>{ code, data, message, suggestion }</c> envelope, mapping a failure
    /// to an exception — and none of it touches the network directly. That is
    /// <see cref="ITripoTransport"/>'s job, which is what makes this class testable.
    ///
    /// Nothing in this class reads or stores the API key.
    /// </summary>
    public sealed class TripoClient
    {
        readonly ITripoTransport _transport;
        readonly string _baseUrl;

        public TripoClient(ITripoTransport transport, string baseUrl = TripoApi.GlobalBaseUrl)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _baseUrl = (baseUrl ?? TripoApi.GlobalBaseUrl).TrimEnd('/');
        }

        public bool HasCredentials => _transport.HasCredentials;
        public string BaseUrl => _baseUrl;

        // ── Generation ────────────────────────────────────────────────────────────────────────

        public Task<string> TextToModelAsync(TextToModelRequest request, CancellationToken ct = default) =>
            CreateTaskAsync(request.Path, request.ToJson(), ct);

        public Task<string> ImageToModelAsync(ImageToModelRequest request, CancellationToken ct = default) =>
            CreateTaskAsync(request.Path, request.ToJson(), ct);

        public Task<string> MultiviewToModelAsync(MultiviewToModelRequest request, CancellationToken ct = default) =>
            CreateTaskAsync(request.Path, request.ToJson(), ct);

        // ── Post-processing and animation ─────────────────────────────────────────────────────

        public Task<string> RigCheckAsync(RigCheckRequest request, CancellationToken ct = default) =>
            CreateTaskAsync(request.Path, request.ToJson(), ct);

        public Task<string> RigAsync(RigRequest request, CancellationToken ct = default) =>
            CreateTaskAsync(request.Path, request.ToJson(), ct);

        public Task<string> RetargetAsync(RetargetRequest request, CancellationToken ct = default) =>
            CreateTaskAsync(request.Path, request.ToJson(), ct);

        public Task<string> DecimateAsync(DecimateRequest request, CancellationToken ct = default) =>
            CreateTaskAsync(request.Path, request.ToJson(), ct);

        public Task<string> ConvertAsync(ConvertRequest request, CancellationToken ct = default) =>
            CreateTaskAsync(request.Path, request.ToJson(), ct);

        // ── Tasks ─────────────────────────────────────────────────────────────────────────────

        /// <summary>POSTs a body to a generation endpoint and returns the new task id.</summary>
        public async Task<string> CreateTaskAsync(string path, string json, CancellationToken ct = default)
        {
            var body = await SendJsonAsync("POST", path, json, ct).ConfigureAwait(false);
            var envelope = JsonUtility.FromJson<TaskIdEnvelope>(body);
            var taskId = envelope?.data?.task_id;
            if (string.IsNullOrEmpty(taskId))
                throw new TripoException("Tripo accepted the request but returned no task_id.");
            return taskId;
        }

        public async Task<TripoTask> GetTaskAsync(string taskId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("A task id is required.", nameof(taskId));
            var body = await SendJsonAsync("GET", TripoApi.Paths.Tasks + "/" + Uri.EscapeDataString(taskId), null, ct)
                .ConfigureAwait(false);
            var envelope = JsonUtility.FromJson<TaskEnvelope>(body);
            if (envelope?.data == null) throw new TripoException("Tripo returned no task payload.");
            return envelope.data;
        }

        /// <summary>
        /// Polls until the task reaches a terminal state, then returns it — including a failed
        /// one, so the caller can read <c>error_msg</c>. Only a timeout or cancellation throws.
        /// </summary>
        /// <param name="pollSeconds">Tripo's own guidance is every 2s; generation takes 10–120s.</param>
        public async Task<TripoTask> WaitForTaskAsync(
            string taskId,
            IProgress<TripoTask> progress = null,
            float pollSeconds = 2f,
            float timeoutSeconds = 600f,
            CancellationToken ct = default)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var delay = Math.Max(250, (int)(pollSeconds * 1000));

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var task = await GetTaskAsync(taskId, ct).ConfigureAwait(false);
                progress?.Report(task);
                if (task.IsTerminal) return task;

                if (DateTime.UtcNow > deadline)
                    throw new TripoException("Timed out after " + timeoutSeconds + "s waiting for the Tripo task; it may still finish — poll the task id again.");

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        // ── Files ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>POST /v3/files</c>. Returns a <c>file_token</c> usable anywhere a
        /// <see cref="TripoFileRef"/> is accepted.
        ///
        /// This is the reason a Unity editor tool can do what muse-infinity's server could not:
        /// submit a local turnaround PNG without first deploying it to a public HTTPS host.
        /// </summary>
        public async Task<string> UploadFileAsync(byte[] data, string fileName, string contentType, CancellationToken ct = default)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("Nothing to upload.", nameof(data));

            var response = await _transport.SendAsync(new TripoHttpRequest
            {
                Method = "POST",
                Url = _baseUrl + TripoApi.Paths.Files,
                ContentType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType,
                Body = data,
                MultipartFileName = string.IsNullOrEmpty(fileName) ? "upload.bin" : fileName,
            }, ct).ConfigureAwait(false);

            var body = Unwrap(response);
            var envelope = JsonUtility.FromJson<FileTokenEnvelope>(body);
            var token = envelope?.data?.file_token;
            if (string.IsNullOrEmpty(token)) token = envelope?.data?.image_token;
            if (string.IsNullOrEmpty(token)) throw new TripoException("Tripo accepted the upload but returned no file_token.");
            return token;
        }

        /// <summary>
        /// Downloads a finished model. Output URLs are pre-signed and expire, so anything worth
        /// keeping has to be pulled down promptly rather than stored as a link.
        /// </summary>
        public async Task<byte[]> DownloadAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("A download URL is required.", nameof(url));
            var response = await _transport.SendAsync(new TripoHttpRequest { Method = "GET", Url = url }, ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(response.TransportError))
                throw new TripoException("Could not reach the Tripo CDN: " + response.TransportError);
            if (response.Status < 200 || response.Status >= 300)
                throw new TripoException("Downloading the model failed. The output URL may have expired.", 0, response.Status);
            if (response.Bytes == null || response.Bytes.Length == 0)
                throw new TripoException("The Tripo CDN returned an empty body.");
            return response.Bytes;
        }

        // ── Account ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>GET /v3/account/balance</c>. Free, and the cheapest proof that a key is valid and
        /// pointed at the right region.
        /// </summary>
        public async Task<TripoBalance> GetBalanceAsync(CancellationToken ct = default)
        {
            var body = await SendJsonAsync("GET", TripoApi.Paths.Balance, null, ct).ConfigureAwait(false);
            var envelope = JsonUtility.FromJson<BalanceEnvelope>(body);
            if (envelope?.data == null) throw new TripoException("Tripo returned no balance payload.");
            return envelope.data;
        }

        // ── Plumbing ──────────────────────────────────────────────────────────────────────────

        async Task<string> SendJsonAsync(string method, string path, string json, CancellationToken ct)
        {
            var response = await _transport.SendAsync(new TripoHttpRequest
            {
                Method = method,
                Url = _baseUrl + path,
                ContentType = json == null ? null : "application/json",
                Body = json == null ? null : System.Text.Encoding.UTF8.GetBytes(json),
            }, ct).ConfigureAwait(false);

            return Unwrap(response);
        }

        /// <summary>
        /// Turns a transport response into a body, or into the most specific exception available.
        ///
        /// Both layers can fail independently and mean different things: a 200 carrying
        /// <c>code != 0</c> is a Tripo-level rejection (bad parameter, no credit) while a 401 with
        /// no JSON at all is an auth problem. Collapsing the two loses the only useful diagnosis.
        /// </summary>
        static string Unwrap(TripoHttpResponse response)
        {
            if (!string.IsNullOrEmpty(response.TransportError))
                throw new TripoException("Could not reach Tripo: " + response.TransportError);

            var body = response.Body ?? string.Empty;
            ErrorEnvelope error = null;
            if (body.TrimStart().StartsWith("{"))
            {
                try { error = JsonUtility.FromJson<ErrorEnvelope>(body); }
                catch (Exception) { /* not the standard envelope; fall through to the status check */ }
            }

            if (error != null && error.code != 0)
            {
                throw new TripoException(
                    string.IsNullOrEmpty(error.message) ? "Tripo rejected the request." : error.message,
                    error.code, response.Status, error.suggestion);
            }

            if (response.Status < 200 || response.Status >= 300)
            {
                var hint = response.Status == 401 || response.Status == 403
                    ? "Check TRIPO_API_KEY, and that the key's region matches the base URL."
                    : null;
                var message = error != null && !string.IsNullOrEmpty(error.message)
                    ? error.message
                    : "Tripo returned HTTP " + response.Status + ".";
                throw new TripoException(message, 0, response.Status, hint);
            }

            if (string.IsNullOrEmpty(body)) throw new TripoException("Tripo returned an empty body.");
            return body;
        }

        // Concrete envelopes: JsonUtility has no generics, so each shape gets its own type.
        [Serializable] class ErrorEnvelope { public int code; public string message; public string suggestion; }
        [Serializable] class TaskIdEnvelope { public int code; public TaskIdData data; }
        [Serializable] class TaskIdData { public string task_id; }
        [Serializable] class TaskEnvelope { public int code; public TripoTask data; }
        [Serializable] class FileTokenEnvelope { public int code; public FileTokenData data; }
        [Serializable] class FileTokenData { public string file_token; public string image_token; }
        [Serializable] class BalanceEnvelope { public int code; public TripoBalance data; }
    }

    /// <summary>Account credit balance. 1 credit = $0.01 USD.</summary>
    [Serializable]
    public class TripoBalance
    {
        public float balance;
        public float frozen;

        public string Describe() =>
            balance.ToString("0.##") + " credits (~$" + (balance * (float)TripoApi.UsdPerCredit).ToString("0.00") + ")" +
            (frozen > 0 ? ", " + frozen.ToString("0.##") + " reserved by tasks in flight" : "");
    }
}
