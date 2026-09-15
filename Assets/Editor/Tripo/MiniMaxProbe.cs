using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using MusePico.Dialogue;
using UnityEditor;
using UnityEngine;

namespace MusePico.EditorTools
{
    /// <summary>
    /// Answers "does my MiniMax key give me the same voices as Skylar's?" by asking, rather than
    /// by reasoning about it.
    ///
    /// Two things could differ between two MiniMax accounts, and only one of them is guessable:
    ///
    ///   <b>Voices</b> — the seven masters are cast to <em>system</em> voices, not cloned ones, so
    ///   they should exist on every account. This checks each of the seven against the account's
    ///   own list instead of assuming.
    ///
    ///   <b>Models</b> — availability IS account-gated. muse-infinity's own casting file records
    ///   probing and finding <c>speech-2.5-turbo</c> absent while 2.8 / 2.6 / 02 were present.
    ///   That is exactly the sort of thing that shows up as a confusing failure at demo time.
    ///
    /// Free: <c>/v1/get_voice</c> lists, it does not synthesise. The synthesis check is separate
    /// and deliberately opt-in, because that one costs.
    ///
    /// Console only — a modal dialog in a menu item blocks Unity's main thread and with it the MCP
    /// bridge. That cost a hang once already.
    /// </summary>
    public static class MiniMaxProbe
    {
        const string VoiceListEndpoint = "https://api.minimax.io/v1/get_voice";

        /// <summary>
        /// The menu entry is synchronous, and hands only the HTTP call to a real
        /// <see cref="Task"/>. An <c>async void</c> menu item would be unobservable by
        /// construction — nothing can await it to see it throw.
        ///
        /// <b>Known, unexplained: this item cannot be driven through the Unity MCP bridge.</b>
        /// <c>execute_menu_item</c> reports success and the method never runs — no console output
        /// at all, not even the first synchronous line. Verified that the type and the menu string
        /// are both present in the compiled <c>MusePico.Tripo.Editor.dll</c>, and that a plain
        /// synchronous item in the same assembly (<see cref="KeyInjectionBuildStep.Report"/>) is
        /// driven fine from the same bridge. Rewriting this from <c>async void</c> to sync changed
        /// nothing, so async is NOT the cause despite the obvious suspicion.
        ///
        /// It works from the Unity menu. Click it rather than scripting it, until someone finds
        /// out why.
        /// </summary>
        [MenuItem("MusePico/Keys/Probe MiniMax voices")]
        public static void Probe()
        {
            var key = Environment.GetEnvironmentVariable("MINIMAX_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning("[MiniMax] MINIMAX_API_KEY is not set in this process. " +
                                 "Set it at User scope and restart Unity — the Editor reads the " +
                                 "environment only at launch.");
                return;
            }

            var roster = LoadRoster();
            if (roster == null) return;

            Debug.Log("[MiniMax] Asking the account which voices it has (free call)…");
            _ = ProbeAsync(key.Trim(), roster);
        }

        static async Task ProbeAsync(string key, MasterRosterData roster)
        {
            string body;
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, VoiceListEndpoint)
                    {
                        Content = new StringContent("{\"voice_type\":\"all\"}", Encoding.UTF8, "application/json"),
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

                    using (var response = await http.SendAsync(request))
                    {
                        body = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.LogError("[MiniMax] HTTP " + (int)response.StatusCode + ". " + Hint((int)response.StatusCode) +
                                           "\n" + Clip(body));
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Never the whole exception: the request carries the Authorization header and some
                // handlers include it in their diagnostics.
                Debug.LogError("[MiniMax] Could not reach the voice list: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            Report(body, roster);
        }

        static void Report(string body, MasterRosterData roster)
        {
            var available = ParseVoiceIds(body);
            var lines = new List<string> { "[MiniMax] Account reports " + available.Count + " voice id(s)." };

            if (available.Count == 0)
            {
                lines.Add("Could not read any ids out of the response — the shape may have changed.");
                lines.Add(Clip(body));
                Debug.LogWarning(string.Join("\n", lines));
                return;
            }

            var missing = 0;
            foreach (var master in roster.masters)
            {
                var present = available.Contains(master.voiceId);
                if (!present) missing++;
                lines.Add((present ? "  ok      " : "  MISSING ") + master.id.PadRight(10) + master.voiceId);
            }

            lines.Add(missing == 0
                ? "All seven cast voices exist on this account — the masters will sound the same as in muse-infinity."
                : missing + " cast voice(s) are not on this account. Re-cast those masters in " +
                  "Assets/Dialogue/masters.json against the list above.");

            lines.Add("Model in use: " + roster.ttsModel +
                      ". This call does not confirm model availability — that is account-gated " +
                      "separately, and only a synthesis call proves it.");

            if (missing == 0) Debug.Log(string.Join("\n", lines));
            else Debug.LogWarning(string.Join("\n", lines));
        }

        /// <summary>
        /// Pulls every <c>voice_id</c> out of the payload by scanning for the key.
        ///
        /// Deliberately not a typed parse: the response has three parallel arrays (system, cloned,
        /// generated) and the only thing wanted is the set of ids across all of them. Scanning is
        /// shorter than three DTOs and does not break when a fourth array appears.
        /// </summary>
        internal static HashSet<string> ParseVoiceIds(string json)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(json)) return found;

            const string key = "\"voice_id\"";
            var at = json.IndexOf(key, StringComparison.Ordinal);

            while (at >= 0)
            {
                var colon = json.IndexOf(':', at + key.Length);
                if (colon < 0) break;

                var open = json.IndexOf('"', colon + 1);
                if (open < 0) break;

                var close = json.IndexOf('"', open + 1);
                if (close < 0) break;

                var id = json.Substring(open + 1, close - open - 1);
                if (id.Length > 0) found.Add(id);

                at = json.IndexOf(key, close, StringComparison.Ordinal);
            }

            return found;
        }

        static MasterRosterData LoadRoster()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Dialogue/masters.json");
            if (asset == null)
            {
                Debug.LogError("[MiniMax] Assets/Dialogue/masters.json is missing — that is where the casting lives.");
                return null;
            }

            try { return MasterRoster.Parse(asset.text); }
            catch (Exception ex) { Debug.LogError("[MiniMax] masters.json unreadable: " + ex.Message); return null; }
        }

        static string Hint(int status)
        {
            switch (status)
            {
                case 401:
                case 403:
                    return "The key was rejected. Check it is a key from platform.minimax.io (global), " +
                           "not the mainland-China console — they are separate accounts with separate endpoints.";
                case 400:
                    return "Rejected as malformed. Some MiniMax setups expect a GroupId query parameter; " +
                           "muse-infinity's working client does not send one against api.minimax.io, so " +
                           "this is the first thing to try if it persists.";
                default:
                    return "";
            }
        }

        static string Clip(string body) =>
            string.IsNullOrEmpty(body) ? "(empty body)" : body.Length <= 400 ? body : body.Substring(0, 400) + "…";
    }
}
