using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>One of the visitor's three answers to a work, and what it adds to the score.</summary>
    public sealed class ArtworkChoice
    {
        public string Id;
        public string Label;
        public PhilosophyAxes Delta;
    }

    /// <summary>A master's scripted lines about a work: one opening, one reaction per answer.</summary>
    public sealed class ArtworkVoice
    {
        public string Opening;
        public Dictionary<string, string> Reactions;
    }

    /// <summary>
    /// Her gallery artwork popup ("game dialogue"), ported verbatim from
    /// <c>muse-infinity/config/companionDialogues.js</c> and the popup logic in <c>app.js</c>.
    ///
    /// Clicking a work: one invited master opens with a scripted line about it, the three give
    /// live readings, and the visitor answers with one of three FIXED choices. The choice feeds
    /// the same philosophy axes the stage-07 decision uses, so conversations in the gallery shape
    /// the final world too — her comment, and the reason the answers are constant.
    ///
    /// Every line is an AI interpretation grounded in the figure's documented themes, not an
    /// authentic quotation; <see cref="Disclaimer"/> says so on the panel.
    /// </summary>
    public static class ArtworkDialogue
    {
        public const string Disclaimer =
            "AI INTERPRETATION GROUNDED IN DOCUMENTED THEMES — NOT AN AUTHENTIC QUOTATION";

        public static readonly IReadOnlyList<ArtworkChoice> Choices = new List<ArtworkChoice>
        {
            new ArtworkChoice { Id = "perception",
                Label = "It changes how my eyes work. I will see differently when I leave.",
                Delta = new PhilosophyAxes(1, 0, 0) },
            new ArtworkChoice { Id = "emotion",
                Label = "It makes me feel something I don't have words for yet.",
                Delta = new PhilosophyAxes(0, 1, 0) },
            new ArtworkChoice { Id = "invention",
                Label = "It shows me a world that never existed—until someone made it.",
                Delta = new PhilosophyAxes(0, 0, 1) },
        };

        /// <summary>Which masters answer a choice most naturally, in preference order.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Champions = new Dictionary<string, string[]>
        {
            ["perception"] = new[] { "monet", "morisot", "hilma" },
            ["emotion"] = new[] { "van_gogh", "frida", "morisot" },
            ["invention"] = new[] { "picasso", "hilma", "socrates" },
        };

        static ArtworkVoice V(string opening, string perception, string emotion, string invention) =>
            new ArtworkVoice
            {
                Opening = opening,
                Reactions = new Dictionary<string, string>
                {
                    ["perception"] = perception, ["emotion"] = emotion, ["invention"] = invention,
                },
            };

        public static readonly IReadOnlyDictionary<string, ArtworkVoice> Voices = new Dictionary<string, ArtworkVoice>
        {
            ["socrates"] = V(
                "Tell me—when you look at “{title}”, do you see what {artist} made, or only what you were already prepared to find?",
                "Then be careful: eyes that have been changed can never again pretend they were innocent. Is that a gift, or a responsibility?",
                "A feeling without words is exactly where thinking should begin—not end. Stay with it a while longer.",
                "So the unreal can instruct the real. Then tell me—which of the two is your teacher now?"),
            ["monet"] = V(
                "Stand closer. “{title}” is not an object—it is a record of light deciding, moment by moment, what to become.",
                "Yes. Paint one haystack a hundred times and you learn: nothing is ordinary, only unobserved.",
                "What you feel is the weather of the painting. Let it pass through you the way light passes through mist.",
                "Every new world begins as a new way of seeing this one. The water lilies were always there—I only consented to see them."),
            ["van_gogh"] = V(
                "I cannot look at “{title}” calmly. Every mark insists that being alive is an urgent thing.",
                "Look until it costs you something. Seeing is not passive—the olive trees taught me that.",
                "Good. Do not tame it. A feeling with no name is the most honest visitor you will ever receive.",
                "I painted the stars not as they are, but as they insisted on being. Perhaps that is invention—or a deeper honesty."),
            ["picasso"] = V(
                "Ask what “{title}” refused to show you. That refusal is where the painting actually lives.",
                "Seeing differently is only the beginning. Walk out and find reality itself renegotiable.",
                "Feel it—then break it open. Sentiment kept whole becomes decoration.",
                "Now you understand. Art is the lie that tells the truth, and the lie must be built with total conviction."),
            ["frida"] = V(
                "“{title}” does not ask for your pity. It asks whether you have ever turned a wound into something that can speak.",
                "Then look also at what is difficult to look at. I painted my reality—never my dreams.",
                "Hold that feeling like a live bird. Naming it too quickly would break its wings.",
                "I invented nothing. I translated. Maybe that is what invention truly is: a refusal to stay silent."),
            ["hilma"] = V(
                "Look past the surface of “{title}”. Beneath every appearance there is a structure the painter felt before anyone could see it.",
                "What you see is a doorway, not a wall. The visible is the smallest part of any painting.",
                "That wordless feeling is a signal from a structure you cannot yet diagram. One day you will draw it.",
                "The temple was never built, and yet you have just walked through it. Unbuilt worlds still carry weight."),
            ["morisot"] = V(
                "Notice the quiet in “{title}”. The revolution is here—in attention paid to what everyone else walks past.",
                "Then begin at home. The cradle, the mirror, the open window—no one thought them worth seeing, and they were everything.",
                "A quiet feeling is not a small one. I spent my life proving that.",
                "New worlds are often made softly—an afternoon, a gaze, a brush held by someone told she should not hold it."),
        };

        public static readonly ArtworkVoice Fallback = V(
            "“{title}” is waiting for your answer, not your admiration.",
            "Then let it keep changing what you notice.",
            "Then let the feeling stay unnamed a little longer.",
            "Then carry that impossible world out with you.");

        public static ArtworkVoice VoiceFor(string masterId) =>
            masterId != null && Voices.TryGetValue(masterId, out var v) ? v : Fallback;

        /// <summary>Her <c>formatLine</c>, with her defaults for a work that lacks either field.</summary>
        public static string Format(string template, string title, string artist) =>
            (template ?? string.Empty)
                .Replace("{title}", string.IsNullOrEmpty(title) ? "this work" : title)
                .Replace("{artist}", string.IsNullOrEmpty(artist) ? "its maker" : artist);

        /// <summary>An object's display title from its asset name: "golden-buddha-statues" -> "Golden Buddha Statues".</summary>
        public static string TitleFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var words = name.Replace('_', ' ').Replace('-', ' ')
                .Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        public static ArtworkChoice ChoiceById(string id)
        {
            foreach (var c in Choices) if (c.Id == id) return c;
            return null;
        }

        /// <summary>
        /// Her <c>pickOpeningSpeaker</c>: the invited masters take turns opening, one per work.
        /// Returns null when nobody is invited.
        /// </summary>
        public static string PickOpening(IReadOnlyList<string> invited, int turn)
        {
            if (invited == null || invited.Count == 0) return null;
            var i = turn % invited.Count;
            return invited[i < 0 ? i + invited.Count : i];
        }

        /// <summary>
        /// Her <c>pickReactionSpeaker</c>: the first invited champion of the chosen axis who did not
        /// open, else any other invited master, else the opener.
        /// </summary>
        public static string PickReaction(string axis, string openerId, IReadOnlyList<string> invited)
        {
            if (invited == null || invited.Count == 0) return openerId;
            if (axis != null && Champions.TryGetValue(axis, out var champions))
                foreach (var id in champions)
                    if (id != openerId && Contains(invited, id)) return id;
            foreach (var id in invited) if (id != openerId) return id;
            return invited[0];
        }

        static bool Contains(IReadOnlyList<string> list, string id)
        {
            foreach (var x in list) if (x == id) return true;
            return false;
        }
    }
}
