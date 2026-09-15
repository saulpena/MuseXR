using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// One master's authored lens, carried over from muse-infinity unchanged.
    ///
    /// Field names match <c>masters.json</c>, which is exported verbatim from
    /// <c>muse-infinity/config/museumAssets.js</c> and <c>config/masterVoices.js</c>. That is
    /// deliberate: this is the most expensive material in either project — six fields per master,
    /// tuned against measured output, with a recorded failure behind several of them — and it
    /// must stay re-syncable by re-running the export rather than by hand-porting prose into C#,
    /// where a dropped clause would be invisible.
    /// </summary>
    [Serializable]
    public class MasterLens
    {
        public string id;
        public string name;
        public string fullName;
        public string color;

        [Header("MiniMax casting")]
        public string voiceId;
        public float voiceSpeed = 1f;

        [Header("The lens")]
        [TextArea(2, 5)] public string lens;
        public string[] attention;
        [TextArea(2, 5)] public string questionStyle;
        public string[] vocabulary;
        public string[] forbidden;

        /// <summary>
        /// The SHAPE the reply must take — the speech act this voice performs. It leads the
        /// master's block because it is what separates the voices: two masters told to "report
        /// what you see" with different word lists produce one voice in two costumes.
        /// </summary>
        [TextArea(3, 10)] public string systemPrompt;

        public bool IsUsable =>
            !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(systemPrompt) &&
            attention != null && attention.Length > 0;
    }

    /// <summary>The whole exported document. Shape matches <c>masters.json</c> exactly.</summary>
    [Serializable]
    public class MasterRosterData
    {
        public string source;
        public string exported;
        public string ttsModel;
        public string narratorVoiceId;
        public string[] effects;
        public string[] defaultMasterIds;
        public MasterLens[] masters;
    }

    /// <summary>
    /// Loads and queries the roster.
    ///
    /// <see cref="Select"/> is a faithful port of the server's <c>selectMasters</c>, including the
    /// rule that matters most: <b>a participant with no lens is skipped entirely.</b> Three voices
    /// without three lenses are one voice three times, which is the failure the whole feature
    /// exists to avoid.
    /// </summary>
    public static class MasterRoster
    {
        /// <summary>One request returns this many parallel, non-interacting perspectives.</summary>
        public const int PerspectiveCount = 3;

        /// <summary>Loaded from <c>Assets/Dialogue/masters.json</c>, shipped as a TextAsset.</summary>
        public static MasterRosterData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("masters.json was empty.");
            var data = JsonUtility.FromJson<MasterRosterData>(json);
            if (data?.masters == null || data.masters.Length == 0)
                throw new ArgumentException("masters.json carried no masters.");
            return data;
        }

        /// <summary>
        /// Resolves the visitor's invited companions to authored masters, topping up to exactly
        /// three: invited first, then the authored defaults, then anyone else with a lens.
        /// </summary>
        public static List<MasterLens> Select(MasterRosterData roster, IEnumerable<string> invitedIds)
        {
            var chosen = new List<MasterLens>(PerspectiveCount);
            var seen = new HashSet<string>();

            void Take(MasterLens candidate)
            {
                if (candidate == null || !candidate.IsUsable) return;
                if (chosen.Count >= PerspectiveCount) return;
                if (!seen.Add(candidate.id)) return;
                chosen.Add(candidate);
            }

            if (invitedIds != null)
                foreach (var id in invitedIds) Take(Find(roster, id));

            if (roster?.defaultMasterIds != null)
                foreach (var id in roster.defaultMasterIds) Take(Find(roster, id));

            if (roster?.masters != null)
                foreach (var master in roster.masters) Take(master);

            return chosen;
        }

        public static MasterLens Find(MasterRosterData roster, string id)
        {
            if (roster?.masters == null || string.IsNullOrEmpty(id)) return null;
            foreach (var master in roster.masters)
                if (string.Equals(master.id, id, StringComparison.OrdinalIgnoreCase)) return master;
            return null;
        }

        /// <summary>
        /// Maps a returned speakerId back onto the master that was actually asked.
        ///
        /// The model is told to copy the id verbatim and usually does, but a mismatch must not
        /// put one master's words under another's name — so an unrecognised id falls back to the
        /// master at that position, which is the one the prompt described.
        /// </summary>
        public static MasterLens Resolve(IReadOnlyList<MasterLens> asked, string speakerId, int index)
        {
            if (asked == null || asked.Count == 0) return null;

            if (!string.IsNullOrEmpty(speakerId))
                foreach (var master in asked)
                    if (string.Equals(master.id, speakerId.Trim(), StringComparison.OrdinalIgnoreCase))
                        return master;

            return asked[Mathf.Clamp(index, 0, asked.Count - 1)];
        }
    }
}
