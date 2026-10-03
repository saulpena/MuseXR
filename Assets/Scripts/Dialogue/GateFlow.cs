using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Skylar's VR plan, stage 1 — the Gate ("Conservatory gate", 60 s; demo route 25 s).
    ///
    ///   Action:     on the pool-side walk, ray-select a sample question or hold X to speak your own.
    ///   Response:   the question is lettered above the arch like an exhibition title; the arch light
    ///               brightens.
    ///   Saved:      question.
    ///   Transition: doors open; the player walks into the first hall.
    ///
    /// With her four guarantees on the main action: an explicit prompt, instant feedback, a 3 s undo
    /// after choosing (and free retakes until the doors are walked through), recognisable later.
    ///
    /// Pure: no UnityEngine. <see cref="Tick"/> is fed time by the scene, so the whole beat - choose,
    /// undo window, doors, entering - runs in EditMode with no headset and no microphone.
    /// </summary>
    public sealed class GateFlow
    {
        public enum Phase { Asking, Chosen, DoorsOpen, Entered }

        /// <summary>Her demo route preselects "What is worth keeping?"; it leads, then her three.</summary>
        public static readonly IReadOnlyList<string> Samples = new List<string>
        {
            "What is worth keeping?",
            "What makes a life meaningful?",
            "How do I live with uncertainty?",
            "What should I keep, and what should I let go?",
        };

        /// <summary>Her question box's limit, kept for a spoken question too.</summary>
        public const int MaxChars = 240;

        /// <summary>Her undo bar: 3 s after placing.</summary>
        public const float UndoSeconds = 3f;

        public Phase Current { get; private set; } = Phase.Asking;
        public string Question { get; private set; } = string.Empty;
        public bool Spoken { get; private set; }

        /// <summary>Seconds left on the undo bar; 0 when it is not showing.</summary>
        public float UndoLeft { get; private set; }

        public bool CanUndo => Current == Phase.Chosen && UndoLeft > 0f;

        public event System.Action<Phase> PhaseChanged;

        public void ChooseSample(int index)
        {
            if (index < 0 || index >= Samples.Count) return;
            Choose(Samples[index], spoken: false);
        }

        /// <summary>A transcript from hold-X. Empty or whitespace is not a question and changes nothing.</summary>
        public bool SetSpoken(string transcript)
        {
            var text = Clean(transcript);
            if (text.Length == 0) return false;
            Choose(text, spoken: true);
            return true;
        }

        /// <summary>B during the undo bar: the lettering comes down and the walk asks again.</summary>
        public bool Undo()
        {
            if (!CanUndo) return false;
            Question = string.Empty;
            Spoken = false;
            UndoLeft = 0f;
            Go(Phase.Asking);
            return true;
        }

        /// <summary>Advance the undo bar; when it runs out the doors open.</summary>
        public void Tick(float deltaSeconds)
        {
            if (Current != Phase.Chosen) return;
            UndoLeft -= deltaSeconds < 0f ? 0f : deltaSeconds;
            if (UndoLeft > 0f) return;
            UndoLeft = 0f;
            Go(Phase.DoorsOpen);
        }

        /// <summary>The visitor walked through the open doors. Only possible once they are open.</summary>
        public bool Enter()
        {
            if (Current != Phase.DoorsOpen) return false;
            Go(Phase.Entered);
            return true;
        }

        /// <summary>The words on the arch: the question as asked, in her typographic quotes.</summary>
        public static string Lettering(string question) =>
            string.IsNullOrWhiteSpace(question) ? string.Empty : "“" + question.Trim() + "”";

        void Choose(string text, bool spoken)
        {
            // A retake is free until the doors are walked through - her "retrievable any time
            // before confirming". Choosing again restarts the undo bar and closes the doors.
            if (Current == Phase.Entered) return;
            Question = text;
            Spoken = spoken;
            UndoLeft = UndoSeconds;
            Go(Phase.Chosen);
        }

        void Go(Phase next)
        {
            if (Current == next && next != Phase.Chosen) return;
            Current = next;
            PhaseChanged?.Invoke(next);
        }

        static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var t = s.Trim();
            return t.Length > MaxChars ? t.Substring(0, MaxChars).TrimEnd() : t;
        }
    }
}
