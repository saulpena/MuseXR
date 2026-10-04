using System;
using System.Collections.Generic;

namespace MuseXR.Slots
{
    /// <summary>
    /// The rule (Saul, 3 Oct 2026): every painting and every interactable object can be clicked, and
    /// walking close to it does the same - a master gives an insight about it. Her web version
    /// (muse-infinity app.js focusArtwork / openArtDialogue) opens with ONE master's scripted line,
    /// rotating through the chosen companions, from config/companionDialogues.js; that is what this
    /// carries, verbatim. Her web version triggers on click only (arriving within 3.4 m only says
    /// "click the painting"); walking close is Saul's addition: within <see cref="ApproachMetres"/>,
    /// facing it within <see cref="ApproachDegrees"/>, once per object per visit. A click always speaks.
    /// Pure: the scene reports distances, angles and clicks; this says who speaks and what.
    /// </summary>
    public sealed class Insights
    {
        public const float ApproachMetres = 2.2f, ApproachDegrees = 40f;

        readonly List<string> _companions;
        readonly HashSet<string> _heard = new HashSet<string>();
        int _next;

        public Insights(IEnumerable<string> companions) { _companions = new List<string>(companions ?? Masters.DefaultTrio); }

        /// <summary>Walking close: true once per object, when near enough and turned towards it.</summary>
        public bool Approached(string objectId, float distance, float degreesOffGaze)
        {
            if (string.IsNullOrEmpty(objectId) || _heard.Contains(objectId)) return false;
            if (distance > ApproachMetres || degreesOffGaze > ApproachDegrees) return false;
            _heard.Add(objectId);
            return true;
        }

        /// <summary>A click: always (and it counts as heard, so walking up afterwards does not repeat it).</summary>
        public void Clicked(string objectId) { if (!string.IsNullOrEmpty(objectId)) _heard.Add(objectId); }

        public bool Heard(string objectId) => _heard.Contains(objectId);

        /// <summary>Her pickOpeningSpeaker: the companions take turns opening.</summary>
        public string NextSpeaker()
        {
            if (_companions.Count == 0) return null;
            var id = _companions[_next % _companions.Count];
            _next++;
            return id;
        }

        /// <summary>Her opening line for this master about this work (formatLine: "{title}", "{artist}").</summary>
        public static string Opening(string master, string title, string artist)
        {
            var t = Voices.TryGetValue(master ?? string.Empty, out var v) ? v : Fallback;
            return t.Replace("{title}", string.IsNullOrEmpty(title) ? "this work" : title)
                    .Replace("{artist}", string.IsNullOrEmpty(artist) ? "its maker" : artist);
        }

        /// <summary>muse-infinity config/companionDialogues.js companionVoices[*].opening, verbatim.</summary>
        static readonly Dictionary<string, string> Voices = new Dictionary<string, string>
        {
            { Masters.Socrates, "Tell me—when you look at “{title}”, do you see what {artist} made, or only what you were already prepared to find?" },
            { Masters.Monet, "Stand closer. “{title}” is not an object—it is a record of light deciding, moment by moment, what to become." },
            { Masters.VanGogh, "I cannot look at “{title}” calmly. Every mark insists that being alive is an urgent thing." },
            { Masters.Picasso, "Ask what “{title}” refused to show you. That refusal is where the painting actually lives." },
            { Masters.Frida, "“{title}” does not ask for your pity. It asks whether you have ever turned a wound into something that can speak." },
            { Masters.Hilma, "Look past the surface of “{title}”. Beneath every appearance there is a structure the painter felt before anyone could see it." },
            { Masters.Morisot, "Notice the quiet in “{title}”. The revolution is here—in attention paid to what everyone else walks past." },
        };

        const string Fallback = "“{title}” is waiting for your answer, not your admiration.";
    }
}
