using MusePico.Dialogue;

namespace MuseXR.Interaction
{
    /// <summary>
    /// One journey record for the whole walk. Each chapter keeps its own <see cref="JourneyRecord"/> while
    /// it runs, but a chapter's objects are destroyed when the visitor leaves it, so what it saved is
    /// copied here: the closing roundtable reads every chapter's choice from this one place.
    /// </summary>
    public static class JourneyMemory
    {
        public static JourneyRecord Record { get; private set; } = new JourneyRecord();

        /// <summary>The visitor's own questions to the masters (her web's askedQuestions), for the round table.</summary>
        public static readonly System.Collections.Generic.List<string> Asked = new System.Collections.Generic.List<string>();

        public static void AddAsked(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return;
            Asked.Add(question.Trim());
            if (Asked.Count > 3) Asked.RemoveAt(0);   // VisitSession keeps three
        }

        public static void Reset() { Record = new JourneyRecord(); Asked.Clear(); }
    }
}
