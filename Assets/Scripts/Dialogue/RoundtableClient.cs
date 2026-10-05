using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MusePico.Tripo;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>One master's closing remark about this visitor's walk.</summary>
    [Serializable]
    public class RoundtableThread
    {
        public string speakerId;
        public string speaker;
        public string text;
        /// <summary>The id of the one record this remark answers (her "Based on: ..."), from the records sent; empty when none were.</summary>
        public string basedOn;
        public override string ToString() => speaker + ": " + text;
    }

    /// <summary>One thing the visitor did that a closing remark may cite: an id the model copies back, and the words.</summary>
    [Serializable]
    public struct RoundtableRecord
    {
        public string id;
        public string text;
        public RoundtableRecord(string id, string text) { this.id = id; this.text = text; }
    }

    /// <summary>The closing itself: a title for the visit, a synthesis, and one thread per master.</summary>
    [Serializable]
    public class RoundtableResult
    {
        public string worldTitle;
        public string synthesis;
        public List<RoundtableThread> threads = new List<RoundtableThread>();

        public bool Live;
        public string Model;
        public string Error;
        public float Seconds;

        public bool Success =>
            threads.Count > 0 &&
            !string.IsNullOrEmpty(worldTitle) &&
            !string.IsNullOrEmpty(synthesis) &&
            string.IsNullOrEmpty(Error);
    }

    // JsonUtility needs a concrete type to deserialise into; the wire shape has no wrapper.
    [Serializable]
    class RoundtablePayload
    {
        public string worldTitle;
        public string synthesis;
        public RoundtableThread[] threads;
    }

    /// <summary>
    /// The closing roundtable — stage 06.
    ///
    /// <b>One call returning three threads, NOT a fan-out.</b> This is the deliberate opposite of
    /// <see cref="DialogueClient"/>, and the reason is in the material: dialogue fans out because
    /// three masters look at one artwork <i>alone</i> and must not converge, whereas the roundtable
    /// synthesises a single trajectory and every thread needs the same view of it. Fanning this out
    /// would give three masters three private readings of a walk they all watched together.
    ///
    /// Shares <see cref="ResponsesCall"/> with the dialogue client, so the transport, retry policy,
    /// envelope walk and error mapping exist once.
    ///
    /// <b>No canned closing.</b> Her rule, carried over: a failure is reported as a failure. A
    /// visitor is better served by "the salon could not be reached" than by a synthesis invented in
    /// the masters' names about a walk nobody read.
    /// </summary>
    public sealed class RoundtableClient
    {
        readonly ResponsesCall _call;
        readonly MasterRosterData _roster;

        public string Model { get => _call.Model; set => _call.Model = value; }
        public string Endpoint { get => _call.Endpoint; set => _call.Endpoint = value; }
        public int RetryLimit { get => _call.RetryLimit; set => _call.RetryLimit = value; }

        public RoundtableClient(ITripoTransport transport, MasterRosterData roster)
            : this(new ResponsesCall(transport), roster) { }

        public RoundtableClient(ResponsesCall call, MasterRosterData roster)
        {
            _call = call ?? throw new ArgumentNullException(nameof(call));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        }

        /// <summary>
        /// The closing. With <paramref name="records"/>, each thread must cite exactly one of them by id
        /// (<see cref="RoundtableThread.basedOn"/>, her "Based on: ..." line): the schema allows only those ids,
        /// so a label can never name a record that does not exist, nor disagree with what the master said
        /// (it used to be assigned afterwards by each master's usual subject - Saul, 5 Oct).
        /// </summary>
        public async Task<RoundtableResult> AskAsync(
            VisitSession session,
            IReadOnlyList<MasterLens> masters,
            CancellationToken ct = default,
            IReadOnlyList<RoundtableRecord> records = null)
        {
            var result = new RoundtableResult { Model = _call.Model };
            var stopwatch = Stopwatch.StartNew();

            if (masters == null || masters.Count == 0)
            {
                result.Error = "No masters with a lens were available.";
                return result;
            }

            try
            {
                var ids = RecordIds(records);
                var instructions = RoundtablePrompt.Instructions(masters.Count) + CiteInstructions(ids, masters.Count);
                var input = RoundtablePrompt.BuildInput(session, masters) + RecordsInput(records);
                var format = SchemaFor(masters.Count, ids);

                var text = await _call.SendAsync(instructions, input, format,
                    raw => DescribeInvalid(raw, masters.Count, ids), ct);

                var parsed = JsonUtility.FromJson<RoundtablePayload>(text);
                result.worldTitle = (parsed.worldTitle ?? string.Empty).Trim();
                result.synthesis = (parsed.synthesis ?? string.Empty).Trim();

                // Re-anchor onto the masters actually asked, exactly as the dialogue client does:
                // the model copies ids back, and a copied id is not a trusted id.
                for (var i = 0; i < parsed.threads.Length; i++)
                {
                    var master = MasterRoster.Resolve(masters, parsed.threads[i].speakerId, i);
                    result.threads.Add(new RoundtableThread
                    {
                        speakerId = master.id,
                        speaker = master.fullName,
                        text = (parsed.threads[i].text ?? string.Empty).Trim(),
                        basedOn = (parsed.threads[i].basedOn ?? string.Empty).Trim(),
                    });
                }

                result.Live = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ResponsesCall.LogFailure("Roundtable", ex.Message);
            }

            stopwatch.Stop();
            result.Seconds = (float)stopwatch.Elapsed.TotalSeconds;
            return result;
        }

        /// <summary>
        /// Her <c>validate()</c>, ported. Every one of these is retryable — the model can produce a
        /// conforming closing on a second attempt, and a malformed one must never reach a visitor.
        /// </summary>
        static List<string> RecordIds(IReadOnlyList<RoundtableRecord> records)
        {
            var ids = new List<string>();
            if (records != null) foreach (var r in records) if (!string.IsNullOrWhiteSpace(r.id) && !ids.Contains(r.id)) ids.Add(r.id);
            return ids;
        }

        static string CiteInstructions(List<string> ids, int threads)
        {
            if (ids.Count == 0) return string.Empty;
            return " Each thread must answer ONE of the visitor's records listed under 'Records', and name it in basedOn by its id"
                 + (ids.Count >= threads ? "; no two threads may cite the same record" : string.Empty)
                 + ". The remark must clearly be about that record.";
        }

        static string RecordsInput(IReadOnlyList<RoundtableRecord> records)
        {
            if (records == null || records.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder("\nRecords (cite one per thread, by id):\n");
            foreach (var r in records) sb.Append("- ").Append(r.id).Append(": ").Append(r.text).Append('\n');
            return sb.ToString();
        }

        public static string DescribeInvalid(string rawJson, int expectedThreads) => DescribeInvalid(rawJson, expectedThreads, null);

        /// <summary>As above, and with <paramref name="recordIds"/> each thread must cite one of them - distinct ones when there are enough.</summary>
        public static string DescribeInvalid(string rawJson, int expectedThreads, IReadOnlyList<string> recordIds)
        {
            RoundtablePayload p;
            try { p = JsonUtility.FromJson<RoundtablePayload>(rawJson); }
            catch (Exception ex) { return "output was not valid JSON: " + ex.Message; }

            if (p == null) return "output was not valid JSON";
            if (string.IsNullOrWhiteSpace(p.worldTitle)) return "worldTitle was empty";
            if (string.IsNullOrWhiteSpace(p.synthesis)) return "synthesis was empty";
            if (p.threads == null || p.threads.Length != expectedThreads)
                return "expected " + expectedThreads + " threads, got " +
                       (p.threads == null ? 0 : p.threads.Length);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < p.threads.Length; i++)
            {
                var t = p.threads[i];
                if (t == null) return "thread " + i + " was null";
                if (string.IsNullOrWhiteSpace(t.speakerId)) return "thread " + i + " had no speakerId";
                if (string.IsNullOrWhiteSpace(t.text)) return "thread " + i + " had no text";
                if (!seen.Add(t.speakerId.Trim()))
                    return "two threads share the speakerId '" + t.speakerId.Trim() + "'";
            }

            if (recordIds != null && recordIds.Count > 0)
            {
                var cited = new HashSet<string>();
                for (var i = 0; i < p.threads.Length; i++)
                {
                    var b = (p.threads[i].basedOn ?? string.Empty).Trim();
                    var known = false; foreach (var id in recordIds) if (id == b) known = true;
                    if (!known) return "thread " + i + " cites '" + b + "', which is not one of the records";
                    if (recordIds.Count >= expectedThreads && !cited.Add(b)) return "two threads cite the same record '" + b + "'";
                }
            }

            return null;
        }

        /// <summary>
        /// The strict schema. <c>minItems</c>/<c>maxItems</c> pin the thread count at the API
        /// rather than only in <see cref="DescribeInvalid"/>, so a wrong count is usually refused
        /// before it costs a second attempt.
        /// </summary>
        static string SchemaFor(int threadCount, List<string> recordIds = null)
        {
            var cites = recordIds != null && recordIds.Count > 0;
            var threadProps = new JsonBuilder()
                .Add("speakerId", new JsonBuilder().Add("type", "string"))
                .Add("speaker", new JsonBuilder().Add("type", "string"))
                .Add("text", new JsonBuilder().Add("type", "string"));
            // Only the ids sent: a label can name nothing the visitor did not do.
            if (cites) threadProps.Add("basedOn", new JsonBuilder().Add("type", "string").AddStringArray("enum", recordIds.ToArray()));

            var thread = new JsonBuilder()
                .Add("type", "object")
                .Add("properties", threadProps)
                .AddStringArray("required", cites ? new[] { "speakerId", "speaker", "text", "basedOn" } : new[] { "speakerId", "speaker", "text" })
                .Add("additionalProperties", false);

            var threads = new JsonBuilder()
                .Add("type", "array")
                .Add("items", thread)
                .Add("minItems", threadCount)
                .Add("maxItems", threadCount);

            var properties = new JsonBuilder()
                .Add("worldTitle", new JsonBuilder().Add("type", "string"))
                .Add("synthesis", new JsonBuilder().Add("type", "string"))
                .Add("threads", threads);

            var schema = new JsonBuilder()
                .Add("type", "object")
                .Add("properties", properties)
                .AddStringArray("required", new[] { "worldTitle", "synthesis", "threads" })
                .Add("additionalProperties", false);

            return ResponsesCall.TextFormat("museum_roundtable", schema);
        }
    }
}
