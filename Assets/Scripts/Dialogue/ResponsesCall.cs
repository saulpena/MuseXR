using System;
using System.Threading;
using System.Threading.Tasks;
using MusePico.Tripo;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// One call to the OpenAI Responses API, with the retry policy, the envelope walk and the error
    /// mapping that every caller needs.
    ///
    /// <b>Extracted so the roundtable is not a copy of the dialogue client.</b> The two differ in
    /// exactly three things — the prompt, the schema, and one call versus a fan-out of three.
    /// Everything else was identical, and a second copy would have meant two places to fix a retry
    /// bug and two places holding a model string. The model string in particular: it had already
    /// gone wrong once, silently, because a value existed in more than one place (see
    /// <c>chatplan.md</c> §9.1).
    ///
    /// <b>Validation belongs to the caller, but retrying belongs here.</b> A schema breach is
    /// retryable — the model can produce a conforming answer on a second attempt — so the caller
    /// passes a validator that returns null for "fine" or a human-readable reason, and this class
    /// decides whether that is worth another attempt.
    ///
    /// Unity-coupled only through <c>Debug</c>; the transport is the <see cref="ITripoTransport"/>
    /// seam, so all of it is exercisable in EditMode against <c>FakeTripoTransport</c> with no key
    /// and no network.
    ///
    /// <b>No ConfigureAwait(false) anywhere, deliberately.</b> Added out of habit once before, it
    /// resumed every continuation on a worker thread and Unity API calls further up threw on
    /// device. Continuations here must come back to the main thread.
    /// </summary>
    public sealed class ResponsesCall
    {
        public const string DefaultEndpoint = "https://api.openai.com/v1/responses";

        /// <summary>
        /// <c>gpt-5.6</c> is an ALIAS for GPT-5.6 Sol — the slowest, dearest tier, and the reason a
        /// three-master turn measured 13-14s. Luna is the speed tier and is what this workload
        /// wants. Measured 22 Sep 2026: 6.55s for an artwork, 4.70s for a world.
        /// </summary>
        public string Model = "gpt-5.6-luna";

        public string Endpoint = DefaultEndpoint;

        /// <summary>One extra attempt, so at most two. No backoff: a visitor is waiting.</summary>
        public int RetryLimit = 1;

        public int TimeoutSeconds = 45;

        readonly ITripoTransport _transport;

        public ResponsesCall(ITripoTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public bool HasCredentials => _transport.HasCredentials;

        /// <summary>
        /// Send one request and return the model's <c>output_text</c>.
        ///
        /// <paramref name="describeInvalid"/> is given the raw text and returns null when it is
        /// acceptable, or a reason when it is not; a reason is treated as a retryable failure.
        ///
        /// Throws <see cref="DialogueException"/> when every attempt fails. Callers catch it and
        /// report the failure honestly rather than inventing prose.
        /// </summary>
        public async Task<string> SendAsync(
            string instructions,
            string input,
            string textFormatJson,
            Func<string, string> describeInvalid,
            CancellationToken ct = default)
        {
            Exception last = null;

            for (var attempt = 1; attempt <= RetryLimit + 1; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await RequestAsync(instructions, input, textFormatJson, describeInvalid, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (DialogueException ex)
                {
                    last = ex;
                    if (!ex.Retryable) break;
                }
            }

            throw last ?? new DialogueException("The model could not be reached.", true);
        }

        async Task<string> RequestAsync(
            string instructions,
            string input,
            string textFormatJson,
            Func<string, string> describeInvalid,
            CancellationToken ct)
        {
            var body = new JsonBuilder()
                .Add("model", Model)
                .Add("store", false)
                .Add("instructions", instructions)
                .Add("input", input)
                .AddRaw("text", textFormatJson)
                .ToString();

            var response = await _transport.SendAsync(new TripoHttpRequest
            {
                Method = "POST",
                Url = Endpoint,
                ContentType = "application/json",
                Body = System.Text.Encoding.UTF8.GetBytes(body),
            }, ct);

            if (!string.IsNullOrEmpty(response.TransportError))
                throw new DialogueException("transport failed: " + response.TransportError, true);

            if (response.Status < 200 || response.Status >= 300)
            {
                var detail = response.Body ?? string.Empty;
                if (detail.Length > 300) detail = detail.Substring(0, 300);
                throw new DialogueException("model returned " + response.Status + ": " + detail,
                    IsRetryableStatus(response.Status));
            }

            var text = ResponsesEnvelope.ExtractOutputText(response.Body);
            if (string.IsNullOrEmpty(text))
                throw new DialogueException("response carried no output_text content.", true);

            if (describeInvalid != null)
            {
                var problem = describeInvalid(text);
                if (problem != null)
                    throw new DialogueException("output failed conformance: " + problem, true);
            }

            return text;
        }

        /// <summary>
        /// 408, 429 and 5xx are worth another attempt. A plain 4xx is not — a malformed request
        /// will be malformed the second time too, and retrying it just doubles the bill.
        /// </summary>
        public static bool IsRetryableStatus(int status) =>
            status == 408 || status == 429 || status >= 500;

        /// <summary>
        /// The strict <c>json_schema</c> wrapper both callers need, built by hand.
        ///
        /// Hand-built rather than reflected because this ships IL2CPP on Android, where reflective
        /// serialization over anonymous types is exactly what managed code stripping breaks.
        /// </summary>
        public static string TextFormat(string name, JsonBuilder schema)
        {
            var format = new JsonBuilder()
                .Add("type", "json_schema")
                .Add("name", name)
                .Add("strict", true)
                .Add("schema", schema);

            return new JsonBuilder().Add("format", format).ToString();
        }

        internal static void LogFailure(string what, string message) =>
            Debug.LogWarning("[" + what + "] failed after retries: " + message);
    }
}
