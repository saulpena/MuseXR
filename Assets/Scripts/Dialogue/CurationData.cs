using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MusePico.Dialogue
{
    /// <summary>A themed title for the visitor's question, and the three chapters it implies.</summary>
    public readonly struct Curation
    {
        public readonly string Title;
        public readonly IReadOnlyList<string> Chapters;
        public Curation(string title, IReadOnlyList<string> chapters) { Title = title; Chapters = chapters; }
        public override string ToString() => Title + " — " + string.Join(", ", Chapters);
    }

    /// <summary>
    /// Stage 03's "AI theme curation".
    ///
    /// <b>It is not a model call.</b> Hers is five regex branches over the lowercased question with
    /// a default (<c>app.js:313-321</c>), and the stage presents the result as if an AI had read
    /// the question. Ported verbatim, including the order — the branches overlap, so the FIRST
    /// match wins and reordering them changes the answer. "What should I let go of, and what
    /// remains meaningful?" matches both `meaning` and `let go`; hers returns the meaning branch,
    /// and so does this.
    ///
    /// Pure C#, no Unity: testable, and it must stay that way so the wording cannot drift silently.
    /// </summary>
    public static class CurationData
    {
        static readonly (Regex Pattern, string Title, string[] Chapters)[] Branches =
        {
            (new Regex("meaning|purpose|worth", RegexOptions.Compiled),
                "The Architecture of a Meaningful Life",
                new[] { "Attention", "Belonging", "What Remains" }),

            (new Regex("uncertain|unknown|future|fear", RegexOptions.Compiled),
                "The Beauty of Not Knowing",
                new[] { "Thresholds", "Changing Light", "Trusting the Unfinished" }),

            (new Regex("keep|let go|loss|leave", RegexOptions.Compiled),
                "What Memory Chooses to Keep",
                new[] { "Attachment", "Transformation", "Release" }),

            (new Regex("love|relationship|alone|belong", RegexOptions.Compiled),
                "The Distance Between Two People",
                new[] { "Recognition", "Intimacy", "Freedom" }),
        };

        /// <summary>Her default branch, used when nothing matches — including for an empty question.</summary>
        public static readonly Curation Default = new Curation(
            "A Museum Built Around Your Question",
            new[] { "How You See", "What You Feel", "What You Can Imagine" });

        /// <summary>
        /// The first matching branch, or <see cref="Default"/>. Matching is on the LOWERCASED
        /// question, as hers is, and is a substring test — "meaningful" matches `meaning`.
        /// </summary>
        public static Curation For(string question)
        {
            var q = (question ?? string.Empty).ToLowerInvariant();
            foreach (var b in Branches)
                if (b.Pattern.IsMatch(q)) return new Curation(b.Title, b.Chapters);
            return Default;
        }
    }
}
