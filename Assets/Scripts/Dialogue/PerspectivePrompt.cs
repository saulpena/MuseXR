using System.Collections.Generic;
using System.Text;

namespace MusePico.Dialogue
{
    /// <summary>What the visitor is standing in front of.</summary>
    public struct ArtworkContext
    {
        public string Title;
        public string Artist;
        public string Date;

        public static ArtworkContext Unknown => new ArtworkContext();
    }

    /// <summary>
    /// The prompt assembly, ported from muse-infinity's <c>server.mjs</c>.
    ///
    /// <b>Every rule here was arrived at by measurement, and several record a specific failure.
    /// Do not tidy this file.</b> The comments that explain why a clause is worded the way it is
    /// came from the original and are kept because the reasoning is the asset — the strings are
    /// just what the reasoning produced.
    ///
    /// The single most important structural rule: <b>shared rules reach the model exactly once,
    /// in the instructions, and appear in no master's block.</b> An earlier revision also
    /// prepended each master's <c>systemPrompt</c> to the instructions, so every shared clause
    /// baked into it arrived two to three times per call while the fields that actually separate
    /// the masters arrived once. Repetition is emphasis, so the model was emphatically told to be
    /// a careful museum voice and only briefly told to be THIS master.
    ///
    /// Pure string assembly, no Unity types, so the port is pinned by tests against the original.
    /// </summary>
    public static class PerspectivePrompt
    {
        /// <summary>
        /// The compliance framing every attributed voice carries, on every endpoint. Stated once
        /// so a new call site cannot ship a subtly weaker version of the claim the product makes.
        ///
        /// INVARIANT: a master's <c>systemPrompt</c> does not restate this, so any call that puts
        /// a lens in front of a model MUST carry this in its instructions.
        /// </summary>
        public const string InterpretiveFraming =
            "Every perspective is an explicitly interpretive AI reading — never an authentic quotation, " +
            "never an endorsement, never impersonation of the real historical person.";

        /// <summary>
        /// Constraints that bind every master identically. They reach the model exactly ONCE per
        /// call, here, and appear in no master's block — a rule repeated three times is a rule the
        /// model weighs three times, which is how "be careful and compliant" came to outweigh
        /// "be Monet".
        /// </summary>
        public const string SharedPerspectiveRules =
            InterpretiveFraming + " " +
            // The client renders this disclaimer beside every reading, so the claim is made to the
            // visitor either way. Making the model spend its words restating it is what produced
            // the "In this interpretive AI reading..." tic that opened replies in all three voices.
            "The interface prints that disclaimer beside every reading, so never write one into the reading " +
            "itself — restating it burns words and gives all three readings the same phrasing. " +
            "Ground the reading in the artwork named in the context and describe it accurately: name only " +
            "colours, objects, and placements it actually has. " +
            "Answer the visitor's specific question rather than restating the artwork. " +
            "Never open with a bare deictic imperative (\"Look\", \"Notice\", \"See\", \"Begin\", \"Observe\") " +
            "— every voice reaches for it, so it identifies none of them. Open as your shape demands. " +
            // 50, not 55. Restating 55 and hoping produced 4 breaches in 30 replies — one at 59
            // words and three landing on exactly 55, which is not "under 55". Asking for 50 leaves
            // the slack inside the constraint instead of outside it.
            "Reply in English, under 50 words.";

        /// <summary>
        /// Instructions for the one-master-per-call arm.
        ///
        /// This arm sees exactly one master, so it cannot be told to differ from the other two at
        /// write time. The separation therefore has to be carried entirely by that master's own
        /// SHAPE — which is why the shape is stated as the hardest constraint rather than as the
        /// sixth line of a description.
        /// </summary>
        public static string SoloInstructions(IReadOnlyList<string> effects) =>
            "You write ONE museum reading, in the voice of the single master described in the context. " +
            "His block opens with the SHAPE his reply must take — the hardest constraint here. Perform it in " +
            "every sentence, never as a clause tacked onto a description: the shape, not the word list, is " +
            "what stops three masters reading as one voice. " +
            "Use his vocabulary and none of his forbidden words. " +
            "Choose one visual effect from " + string.Join(", ", effects) + ". " +
            SharedPerspectiveRules;

        public static string DescribeArtwork(ArtworkContext artwork) =>
            "Artwork in focus: " + Or(artwork.Title, "unknown") +
            " by " + Or(artwork.Artist, "unknown") +
            " (" + Or(artwork.Date, "date unknown") + ")";

        /// <summary>
        /// The master's authored lens, verbatim — and the ONLY place a master's voice material is
        /// emitted.
        ///
        /// <c>systemPrompt</c> leads because it carries the SHAPE: the speech act this voice
        /// performs. Two masters told to "report what you see" with different nouns produce one
        /// voice in two costumes; the separation has to be a different rhetorical move, not a
        /// different word list.
        /// </summary>
        public static string DescribeMaster(MasterLens master, int index)
        {
            var lines = new List<string>
            {
                "--- PERSPECTIVE " + (index + 1) + " ---",
                "speakerId (copy verbatim): " + master.id,
                "speaker (copy verbatim): " + master.fullName,
                "THE SHAPE OF YOUR REPLY — the hardest constraint here, obey it in every sentence: " + master.systemPrompt,
                "The move this voice makes: " + master.questionStyle,
                "Lens: " + master.lens,
                "Attend only to: " + string.Join("; ", master.attention),
                "Draw on this vocabulary: " + string.Join(", ", master.vocabulary),
                "Never use these words: " + string.Join(", ", master.forbidden),
            };
            return string.Join("\n", lines);
        }

        /// <summary>The full input block for one call: the question, the artwork, then the masters.</summary>
        public static string BuildInput(string question, IReadOnlyList<MasterLens> masters, ArtworkContext artwork)
        {
            var sb = new StringBuilder();
            sb.Append("Visitor question: ").Append(Truncate(question, 1200)).Append('\n');
            sb.Append(DescribeArtwork(artwork)).Append('\n');
            sb.Append('\n');

            for (var i = 0; i < masters.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(DescribeMaster(masters[i], i));
            }

            return sb.ToString();
        }

        static string Or(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        static string Truncate(string value, int max)
        {
            value = value ?? string.Empty;
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
