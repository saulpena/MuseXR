using System.Collections.Generic;

namespace MuseXR.Interaction
{
    /// <summary>
    /// What the compass says below its arrow while a choice is being prepared (Saul, 5 Oct: "get rid of that card
    /// entirely and combine its functionality in the compass. The compass can grow bigger downward"). A
    /// <see cref="ChoicePreview"/> posts it; the waist panel's compass reads it every frame and grows to show it.
    /// Pure data, no Unity: the compass lives in an assembly that depends on this one, so it reads, never the
    /// other way round.
    /// </summary>
    public static class CompassBrief
    {
        public struct Row
        {
            public string Label;
            public bool Done;
            public string State;
        }

        static object _owner;

        /// <summary>A small heading: "Before you choose".</summary>
        public static string Kicker { get; private set; }
        /// <summary>The instruction, in her serif.</summary>
        public static string Title { get; private set; }
        public static readonly List<Row> Rows = new List<Row>();
        /// <summary>Changes on every post, so a reader redraws only when something changed.</summary>
        public static int Version { get; private set; }
        /// <summary>Bumped by <see cref="Nudge"/>: the visitor reached for the choice too early.</summary>
        public static int Nudges { get; private set; }
        public static bool Showing => _owner != null;

        public static void Show(object owner, string kicker, string title, IEnumerable<Row> rows)
        {
            _owner = owner; Kicker = kicker; Title = title;
            Rows.Clear(); if (rows != null) Rows.AddRange(rows);
            Version++;
        }

        /// <summary>Take it down, if <paramref name="owner"/> is still the one showing it.</summary>
        public static void Hide(object owner)
        {
            if (_owner != owner || _owner == null) return;
            _owner = null; Kicker = Title = null; Rows.Clear();
            Version++;
        }

        public static void Nudge(object owner) { if (_owner == owner && owner != null) Nudges++; }

        /// <summary>For tests and a fresh journey.</summary>
        public static void Reset() { _owner = null; Kicker = Title = null; Rows.Clear(); Version++; }
    }
}
