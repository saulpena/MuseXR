namespace MuseXR.Interaction
{
    /// <summary>
    /// What the masters are talking about right now - the artwork, the option, the visitor's question - shown as
    /// the small heading on their dialogue card (Saul, 5 Oct). Whoever starts a round of lines sets it.
    /// </summary>
    public static class DialogueContext
    {
        public static string Current { get; private set; } = "";

        public static void Set(string context) => Current = context ?? "";

        /// <summary>A short line for the masters' card that is not a master speaking (the card listens: it lives in an
        /// assembly this one cannot see).</summary>
        public static event System.Action<string, string, float> Noticed;

        public static void Notice(string kicker, string text, float seconds = 4f) => Noticed?.Invoke(kicker, text, seconds);

        /// <summary>Take a note down if it is the one still showing (it no longer applies).</summary>
        public static event System.Action<string> Unnoticed;

        public static void Unnotice(string text) => Unnoticed?.Invoke(text);

        /// <summary>"On The Bedroom": the works and pieces they speak about.</summary>
        public static void On(string title) => Set(string.IsNullOrEmpty(title) ? "" : "On " + title);
    }
}
