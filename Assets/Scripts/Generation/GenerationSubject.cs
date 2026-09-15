using System;
using System.Collections.Generic;

namespace MusePico.Generation
{
    /// <summary>
    /// What kind of thing the visitor is asking for. The kind decides both the prompt scaffolding
    /// and the triangle budget, because "a bust of Monet" and "a cathedral" want very different
    /// requests even when the user typed a similar number of words.
    /// </summary>
    public enum SubjectKind
    {
        /// <summary>A person, as a museum figure. The MUSE cast lives here.</summary>
        Artist,
        /// <summary>A sculpture or artwork object.</summary>
        Artwork,
        /// <summary>Furniture, instruments, museum objects — the things that fill a room.</summary>
        Prop,
        /// <summary>One architectural piece: an arch, a column, a doorway. NOT a whole environment.</summary>
        EnvironmentPiece,
        /// <summary>Whatever the visitor said, unscaffolded.</summary>
        FreeForm,
    }

    /// <summary>
    /// Turns raw user input into a Tripo prompt.
    ///
    /// Two jobs, and the second is the one that earns its keep. The first is cosmetic: wrap a bare
    /// noun in enough context that a generator returns a museum object rather than a game asset.
    ///
    /// The second is to stop the request being wrong in a way that costs credits. A generated
    /// mesh is a single object with a single material — Tripo makes *things*, not *places*. Asked
    /// for "a Parisian street at dusk" it returns a diorama the size of a shoebox with the street
    /// baked into one lump, which is unusable and bills the same as a good result. Whole
    /// environments belong to the Gaussian-splat road (World Labs Marble) that already works in
    /// this project. <see cref="LooksLikeAnEnvironment"/> catches the common phrasings so the UI
    /// can say so before spending anything.
    ///
    /// Pure string work, no Unity types, so every rule here is pinned by a test.
    /// </summary>
    public static class GenerationSubject
    {
        /// <summary>Tripo's own cap on prompt length.</summary>
        public const int MaxPromptLength = 1024;

        /// <summary>
        /// Shared tail. "single object, plain background, full body visible" is the part that
        /// most reliably keeps a generator from returning a cropped bust or a scene.
        /// </summary>
        const string Common = "single complete object, centred, plain background, neutral even lighting";

        static readonly Dictionary<SubjectKind, string> Templates = new Dictionary<SubjectKind, string>
        {
            { SubjectKind.Artist, "a full-body museum statue of {0}, standing in a neutral pose, carved detail, " + Common },
            { SubjectKind.Artwork, "a museum sculpture of {0}, gallery exhibit piece, " + Common },
            { SubjectKind.Prop, "{0}, a museum gallery object, clean silhouette, " + Common },
            { SubjectKind.EnvironmentPiece, "a single architectural element: {0}, stone museum architecture, " + Common },
            { SubjectKind.FreeForm, "{0}" },
        };

        static readonly Dictionary<SubjectKind, VrBudgetTier> Tiers = new Dictionary<SubjectKind, VrBudgetTier>
        {
            { SubjectKind.Artist, VrBudgetTier.Exhibit },
            { SubjectKind.Artwork, VrBudgetTier.Exhibit },
            { SubjectKind.Prop, VrBudgetTier.Background },
            { SubjectKind.EnvironmentPiece, VrBudgetTier.Hero },
            { SubjectKind.FreeForm, VrBudgetTier.Exhibit },
        };

        /// <summary>
        /// Words that mean "a place", not "a thing". Matched on the raw user input, before the
        /// template wraps it — afterwards the scaffolding would trip the check on itself.
        /// </summary>
        static readonly string[] EnvironmentWords =
        {
            "room", "street", "landscape", "forest", "city", "interior", "environment", "scene",
            "hall", "gallery space", "courtyard", "garden", "field", "village", "town", "valley",
            "mountains", "beach", "square", "plaza", "corridor", "world",
        };

        public static VrBudgetTier TierFor(SubjectKind kind) =>
            Tiers.TryGetValue(kind, out var tier) ? tier : VrBudgetTier.Exhibit;

        /// <summary>
        /// Builds the prompt. Empty or whitespace input throws rather than producing a prompt made
        /// entirely of scaffolding, which would generate something and charge for it.
        /// </summary>
        public static string BuildPrompt(SubjectKind kind, string userInput)
        {
            var subject = Normalise(userInput);
            if (subject.Length == 0)
                throw new ArgumentException("Nothing was asked for.", nameof(userInput));

            var template = Templates.TryGetValue(kind, out var t) ? t : "{0}";
            var prompt = string.Format(template, subject);

            return prompt.Length <= MaxPromptLength ? prompt : prompt.Substring(0, MaxPromptLength);
        }

        /// <summary>
        /// True when the input asks for a place rather than an object. Advisory: the caller
        /// decides whether to warn, re-scope, or send it anyway.
        /// </summary>
        public static bool LooksLikeAnEnvironment(string userInput)
        {
            var subject = Normalise(userInput).ToLowerInvariant();
            if (subject.Length == 0) return false;

            foreach (var word in EnvironmentWords)
            {
                var at = subject.IndexOf(word, StringComparison.Ordinal);
                while (at >= 0)
                {
                    var beforeOk = at == 0 || !char.IsLetter(subject[at - 1]);
                    var end = at + word.Length;
                    var afterOk = end >= subject.Length || !char.IsLetter(subject[end]);
                    if (beforeOk && afterOk) return true;
                    at = subject.IndexOf(word, at + 1, StringComparison.Ordinal);
                }
            }
            return false;
        }

        /// <summary>
        /// Trims, collapses runs of whitespace, and drops the filler that dictation adds to the
        /// front of a sentence. A prompt is not a conversation, and "um, can you make me a"
        /// spends the same credits as the noun that follows it.
        /// </summary>
        public static string Normalise(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput)) return string.Empty;

            var collapsed = new System.Text.StringBuilder(userInput.Length);
            var lastWasSpace = false;
            foreach (var c in userInput.Trim())
            {
                var isSpace = char.IsWhiteSpace(c);
                if (isSpace && lastWasSpace) continue;
                collapsed.Append(isSpace ? ' ' : c);
                lastWasSpace = isSpace;
            }

            var text = collapsed.ToString();
            foreach (var filler in Fillers)
            {
                if (text.StartsWith(filler, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(filler.Length).TrimStart();
                    break;
                }
            }

            text = text.TrimEnd('.', ',', ' ');

            // "make me a" strips down to a bare article. That is not a subject, it is dictation
            // that got cut off — and left alone it produces a perfectly valid prompt ("a museum
            // statue of a") that generates something and charges for it. The article is only
            // dropped when NOTHING else is left, so "a brass telescope" still passes through whole.
            var lower = text.ToLowerInvariant();
            if (lower == "a" || lower == "an" || lower == "the") return string.Empty;

            return text;
        }

        static readonly string[] Fillers =
        {
            "can you make me a ", "can you make me ", "can you make a ", "can you make ",
            "i would like a ", "i would like ", "i want a ", "i want ",
            "please make a ", "please make ", "make me a ", "make me ", "make a ", "make ",
            "generate a ", "generate ", "create a ", "create ", "show me a ", "show me ",
        };
    }
}
