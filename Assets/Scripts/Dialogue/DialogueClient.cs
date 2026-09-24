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
        /// <c>gpt-5.6</c> is an ALIAS for Sol — the deepest-reasoning, slowest, dearest tier.
        /// That is why muse-infinity measured the default blowing its own 15s timeout, and why a
        /// three-master turn timed 13.1s and 14.4s here. Luna is the same family's speed tier and
        /// is what this workload wants: three concurrent calls, ~450 tokens in, under 50 words out,
        /// against a strict schema.
        ///
        /// Do NOT copy muse-infinity's <c>gpt-5.3-codex-spark</c> pin — that is a Codex research
        /// preview and is not available on the API.
        /// </summary>
        public string Model = "gpt-5.6-luna";

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
        readonly ResponsesCall _call;
        readonly MasterRosterData _roster;

        public DialogueClient(ITripoTransport transport, MasterRosterData roster)
            : this(new ResponsesCall(transport), roster) { }

        /// <summary>
        /// Shares one <see cref="ResponsesCall"/> with the roundtable client, so the transport,
        /// retry policy, envelope walk and error mapping live in one place. The fan-out, the
        /// per-master prompt and the single-perspective schema stay here, because those are the
        /// only things that actually differ between the two callers.
        /// </summary>
        public DialogueClient(ResponsesCall call, MasterRosterData roster)
        {
            _call = call ?? throw new ArgumentNullException(nameof(call));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _call.Model = Model;
            _call.Endpoint = Endpoint;
            _call.RetryLimit = RetryLimit;
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
            // Keep the public knobs authoritative: they are Inspector-free plain fields, so a
            // caller may set them after construction.
            _call.Model = Model;
            _call.Endpoint = Endpoint;
            _call.RetryLimit = RetryLimit;
            _call.TimeoutSeconds = TimeoutSeconds;

            var text = await _call.SendAsync(instructions, input, PerspectiveSchemaJson(),
                raw => DescribeInvalid(raw), ct);

            return JsonUtility.FromJson<Perspective>(text);
        }

        /// <summary>
        /// Conformance for one perspective. Retryable by construction: the model can produce a
        /// conforming reading on a second attempt, and a non-conforming one must never reach a
        /// visitor.
        /// </summary>
        string DescribeInvalid(string rawJson)
        {
            Perspective parsed;
            try { parsed = JsonUtility.FromJson<Perspective>(rawJson); }
            catch (Exception ex) { return "output was not valid JSON: " + ex.Message; }

            if (parsed == null) return "output was not valid JSON";
            return PerspectiveValidation.DescribeInvalid(parsed, _roster.effects);
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

            return ResponsesCall.TextFormat("museum_perspective", schema);
        }

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
