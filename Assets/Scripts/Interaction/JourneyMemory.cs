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

        public static void Reset() => Record = new JourneyRecord();
    }
}
