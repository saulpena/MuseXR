using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MusePico.Tripo;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Asks three masters the same question at once.
    ///
    /// <b>Three concurrent calls, not one call returning three readings.</b> That is not a style
    /// preference — muse-infinity measured both arms (n=32, strictly interleaved, one request in
    /// flight, question varied to defeat prompt caching):
    ///
    /// <code>
    ///            P50      P95      max      min
    ///   one call  6.40s   14.07s   19.00s   4.30s
    ///   fan-out   3.30s    7.62s   13.00s   2.41s
    /// </code>
    ///
    /// The gap is structural: one call decodes three readings serially, the fan-out makes wall
    /// clock the max of three rather than the sum. Distinctiveness was measured too and is
    /// unaffected — the authored lens prompts carry the separation on their own. The one-call arm
    /// also ran hot against the request timeout, which in front of an audience is a live failure
    /// risk rather than a slow reply.
    ///
    /// <b>That ~3.3s median is what makes the Tripo wait designable</b> — but 3.3s is the model
    /// thinking, not the visitor listening. MEASURED 14 Sep, end to end:
    ///
    ///   transcription ~1s, dialogue ~3.3s, MiniMax synthesis 1.5-2.4s per line (and the three
    ///   can be synthesised concurrently), then ~4-5s of audio per master played in sequence.
    ///
    /// So one question answered by three masters is roughly <b>20 seconds</b>, of which about 14
    /// is speech. Against a 45-90s generation that is <b>three to six exchanges</b>, not fifteen —
    /// an earlier note here said fifteen by counting only the model's latency and forgetting that
    /// someone has to listen to the answer.
    ///
    /// Reuses <see cref="ITripoTransport"/> as the HTTP seam. The name is wrong for this use and
    /// the interface is not Tripo-specific in substance — a deliberate trade, taken so the
    /// dialogue could be built without re-verifying the 92 tests that already sit on it. Worth
    /// renaming to a neutral seam when there is a quiet moment, not mid-feature.
    /// </summary>
    public class DialogueClient
    {
        public const string DefaultEndpoint = "https://api.openai.com/v1/responses";

        /// <summary>
        /// Not verified against this account. muse-infinity pins its model in <c>.env.example</c>
        /// after measuring it, and records that the default was too slow for its own timeout —
        /// so treat this as a starting point to measure, not a setting to trust.
        /// </summary>
        public string Model = "gpt-5.6";

        public string Endpoint = DefaultEndpoint;

        /// <summary>One retry, as the original. A conformance failure is retryable; a 4xx is not.</summary>
        public int RetryLimit = 1;

        public int TimeoutSeconds = 45;

        // NOTE: nothing in this class uses ConfigureAwait(false), deliberately.
        //
        // It is the right habit in a general-purpose library and the wrong one here: the
        // continuations touch Unity APIs (JsonUtility, and whatever the caller does with the
        // result), and Unity APIs are main-thread only. The sibling voice client learned this
        // on a device — every await resumed on a worker thread, so the readings arrived, the
        // audio synthesised, and AudioClip.Create threw. These are IO waits that never block
        // the main thread, so there is nothing to win by hopping off it.
        readonly ITripoTransport _transport;
        readonly MasterRosterData _roster;

        public DialogueClient(ITripoTransport transport, MasterRosterData roster)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        }

        /// <summary>
        /// Asks every master in <paramref name="masters"/> concurrently and reconciles the
        /// replies back onto the masters that were actually asked.
        ///
        /// Never throws for a model failure: a dialogue turn happens in a headset with a visitor
        /// waiting, and an exception there is a dead app. The failure comes back on the result,
        /// and it comes back honestly — <b>there is no canned prose dressed up as a live
        /// answer</b>, which is the rule the original states in as many words.
        /// </summary>
        public async Task<DialogueResult> AskAsync(
            string question,
            IReadOnlyList<MasterLens> masters,
            ArtworkContext artwork,
            CancellationToken ct = default)
        {
            var result = new DialogueResult { Model = Model };
            var stopwatch = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(question))
            {
                result.Error = "A question is required.";
                return result;
            }
            if (masters == null || masters.Count == 0)
            {
                result.Error = "No masters with a lens were available.";
                return result;
            }

            var instructions = PerspectivePrompt.SoloInstructions(_roster.effects);

            try
            {
                var calls = new Task<Perspective>[masters.Count];
                for (var i = 0; i < masters.Count; i++)
                {
                    // One master per call, so the input describes exactly one.
                    var single = new[] { masters[i] };
                    var input = PerspectivePrompt.BuildInput(question, single, artwork);
                    calls[i] = AskOneAsync(instructions, input, ct);
                }

                var replies = await Task.WhenAll(calls);

                for (var i = 0; i < replies.Length; i++)
                {
                    var master = MasterRoster.Resolve(masters, replies[i].speakerId, i);
                    result.Perspectives.Add(new Perspective
                    {
                        speakerId = master.id,
                        speaker = master.fullName,
                        text = replies[i].text.Trim(),
                        effect = replies[i].effect,
                    });
                }

                result.Live = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Debug.LogWarning("[Dialogue] failed after retries: " + ex.Message +
                                 " (question chars=" + question.Length +
                                 ", masters=" + string.Join(",", Ids(masters)) + ")");
            }

            stopwatch.Stop();
            result.Seconds = (float)stopwatch.Elapsed.TotalSeconds;
            return result;
        }

        async Task<Perspective> AskOneAsync(string instructions, string input, CancellationToken ct)
        {
            Exception last = null;

            for (var attempt = 1; attempt <= RetryLimit + 1; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await RequestAsync(instructions, input, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (DialogueException ex)
                {
                    last = ex;
                    if (!ex.Retryable) break;
                }
            }

            throw last ?? new DialogueException("The dialogue model could not be reached.", true);
        }

        async Task<Perspective> RequestAsync(string instructions, string input, CancellationToken ct)
        {
            var body = new JsonBuilder()
                .Add("model", Model)
                .Add("store", false)
                .Add("instructions", instructions)
                .Add("input", input)
                .AddRaw("text", PerspectiveSchemaJson())
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
                var detail = (response.Body ?? string.Empty);
                if (detail.Length > 300) detail = detail.Substring(0, 300);
                throw new DialogueException("model returned " + response.Status + ": " + detail,
                    IsRetryableStatus(response.Status));
            }

            var text = ResponsesEnvelope.ExtractOutputText(response.Body);
            if (string.IsNullOrEmpty(text))
                throw new DialogueException("response carried no output_text content.", true);

            Perspective parsed;
            try { parsed = JsonUtility.FromJson<Perspective>(text); }
            catch (Exception ex) { throw new DialogueException("output was not valid JSON: " + ex.Message, true); }

            var problem = PerspectiveValidation.DescribeInvalid(parsed, _roster.effects);
            if (problem != null)
                throw new DialogueException("output failed conformance: " + problem, true);

            return parsed;
        }

        /// <summary>
        /// The strict json_schema block for one perspective, built by hand.
        ///
        /// Hand-built rather than reflected because this project is IL2CPP on Android, where
        /// reflective serialization over anonymous types is exactly the pattern that breaks under
        /// managed code stripping — a lesson already paid for in PersonalAssistant.
        /// </summary>
        string PerspectiveSchemaJson()
        {
            var effectEnum = new JsonBuilder()
                .Add("type", "string")
                .AddStringArray("enum", _roster.effects);

            var properties = new JsonBuilder()
                .Add("speakerId", new JsonBuilder().Add("type", "string"))
                .Add("speaker", new JsonBuilder().Add("type", "string"))
                .Add("text", new JsonBuilder().Add("type", "string"))
                .Add("effect", effectEnum);

            var schema = new JsonBuilder()
                .Add("type", "object")
                .Add("properties", properties)
                .AddStringArray("required", new[] { "speakerId", "speaker", "text", "effect" })
                .Add("additionalProperties", false);

            var format = new JsonBuilder()
                .Add("type", "json_schema")
                .Add("name", "museum_perspective")
                .Add("strict", true)
                .Add("schema", schema);

            return new JsonBuilder().Add("format", format).ToString();
        }

        static bool IsRetryableStatus(int status) =>
            status == 408 || status == 429 || status >= 500;

        static IEnumerable<string> Ids(IReadOnlyList<MasterLens> masters)
        {
            foreach (var master in masters) yield return master.id;
        }
    }

    public class DialogueException : Exception
    {
        public bool Retryable { get; }

        public DialogueException(string message, bool retryable) : base(message) => Retryable = retryable;
    }
}
