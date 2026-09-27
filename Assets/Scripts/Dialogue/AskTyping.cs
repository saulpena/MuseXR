namespace MusePico.Dialogue
{
    /// <summary>
    /// Typing a question into the ask form on desktop. Engine-free, so it is EditMode-testable.
    ///
    /// The form opens pre-filled ("What do you see in …?"), the way her input opens selected: the
    /// first key the visitor types REPLACES the suggestion rather than appending to it. After the
    /// masters answer, the question is marked as a suggestion again, so the next question starts
    /// clean instead of being typed onto the end of the last one.
    /// </summary>
    public static class AskTyping
    {
        public const int MaxLength = 200;

        /// <param name="text">The question currently in the box.</param>
        /// <param name="isSuggestion">True while the box holds text the visitor did not type.</param>
        /// <param name="typed">Characters typed this frame, control characters included.</param>
        /// <param name="backspaces">Backspace presses this frame.</param>
        public static string Apply(string text, ref bool isSuggestion, string typed, int backspaces)
        {
            text ??= string.Empty;
            typed ??= string.Empty;

            if (isSuggestion && (backspaces > 0 || HasPrintable(typed)))
            {
                text = string.Empty;
                isSuggestion = false;
                backspaces = 0;                 // clearing the suggestion IS the backspace
            }

            if (backspaces > 0)
                text = text.Substring(0, System.Math.Max(0, text.Length - backspaces));

            foreach (var c in typed)
            {
                if (char.IsControl(c)) continue;
                if (text.Length >= MaxLength) break;
                text += c;
            }
            return text;
        }

        static bool HasPrintable(string s)
        {
            foreach (var c in s) if (!char.IsControl(c)) return true;
            return false;
        }
    }
}
