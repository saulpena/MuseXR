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

        // ---- the visitor's reply (her artworkChoices, companionDialogues.js) ---------------------------

        /// <summary>Her three replies to a work, verbatim, each feeding one of her philosophy axes.</summary>
        public static readonly IReadOnlyList<(string axis, string label)> Replies = new List<(string, string)>
        {
            ("perception", "It changes how my eyes work. I will see differently when I leave."),
            ("emotion", "It makes me feel something I don't have words for yet."),
            ("invention", "It shows me a world that never existed—until someone made it."),
        };

        /// <summary>Her AXIS_CHAMPIONS: who answers a reply most naturally, in preference order.</summary>
        static readonly Dictionary<string, string[]> Champions = new Dictionary<string, string[]>
        {
            { "perception", new[] { Masters.Monet, Masters.Morisot, Masters.Hilma } },
            { "emotion", new[] { Masters.VanGogh, Masters.Frida, Masters.Morisot } },
            { "invention", new[] { Masters.Picasso, Masters.Hilma, Masters.Socrates } },
        };

        /// <summary>
        /// Her pickReactionSpeaker: the first champion of the axis among the companions who is not the
        /// one who opened; else any other companion; else the opener.
        /// </summary>
        public static string ReactionSpeaker(string axis, IReadOnlyList<string> companions, string openerId)
        {
            if (companions == null || companions.Count == 0) return openerId;
            if (Champions.TryGetValue(axis ?? "", out var order))
                foreach (var id in order) if (id != openerId && Contains(companions, id)) return id;
            foreach (var id in companions) if (id != openerId) return id;
            return companions[0];
        }

        static bool Contains(IReadOnlyList<string> list, string id) { foreach (var x in list) if (x == id) return true; return false; }

        /// <summary>Her scripted reaction of <paramref name="master"/> to a reply on <paramref name="axis"/>, verbatim.</summary>
        public static string Reaction(string master, string axis)
        {
            if (!ReactionLines.TryGetValue(master ?? "", out var r)) r = FallbackReactions;
            return axis == "perception" ? r[0] : axis == "emotion" ? r[1] : axis == "invention" ? r[2] : string.Empty;
        }

        /// <summary>companionVoices[*].reactions: perception, emotion, invention.</summary>
        static readonly Dictionary<string, string[]> ReactionLines = new Dictionary<string, string[]>
        {
            { Masters.Socrates, new[] { "Then be careful: eyes that have been changed can never again pretend they were innocent. Is that a gift, or a responsibility?", "A feeling without words is exactly where thinking should begin—not end. Stay with it a while longer.", "So the unreal can instruct the real. Then tell me—which of the two is your teacher now?" } },
            { Masters.Monet, new[] { "Yes. Paint one haystack a hundred times and you learn: nothing is ordinary, only unobserved.", "What you feel is the weather of the painting. Let it pass through you the way light passes through mist.", "Every new world begins as a new way of seeing this one. The water lilies were always there—I only consented to see them." } },
            { Masters.VanGogh, new[] { "Look until it costs you something. Seeing is not passive—the olive trees taught me that.", "Good. Do not tame it. A feeling with no name is the most honest visitor you will ever receive.", "I painted the stars not as they are, but as they insisted on being. Perhaps that is invention—or a deeper honesty." } },
            { Masters.Picasso, new[] { "Seeing differently is only the beginning. Walk out and find reality itself renegotiable.", "Feel it—then break it open. Sentiment kept whole becomes decoration.", "Now you understand. Art is the lie that tells the truth, and the lie must be built with total conviction." } },
            { Masters.Frida, new[] { "Then look also at what is difficult to look at. I painted my reality—never my dreams.", "Hold that feeling like a live bird. Naming it too quickly would break its wings.", "I invented nothing. I translated. Maybe that is what invention truly is: a refusal to stay silent." } },
            { Masters.Hilma, new[] { "What you see is a doorway, not a wall. The visible is the smallest part of any painting.", "That wordless feeling is a signal from a structure you cannot yet diagram. One day you will draw it.", "The temple was never built, and yet you have just walked through it. Unbuilt worlds still carry weight." } },
            { Masters.Morisot, new[] { "Then begin at home. The cradle, the mirror, the open window—no one thought them worth seeing, and they were everything.", "A quiet feeling is not a small one. I spent my life proving that.", "New worlds are often made softly—an afternoon, a gaze, a brush held by someone told she should not hold it." } },
        };

        static readonly string[] FallbackReactions =
        {
            "Then let it keep changing what you notice.", "Then let the feeling stay unnamed a little longer.", "Then carry that impossible world out with you.",
        };
    }
}
