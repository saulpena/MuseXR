using System;
using System.Collections.Generic;

namespace MuseXR.Slots
{
    /// <summary>
    /// Her chapter A, Palace · Court of Keeping, as one flow (her storyboard A, chatplan §2.4):
    ///
    ///   Choosing   lift a miniature, turn it, bring it to the court.
    ///   Placed     it snapped in (bronze bell, the court lights, the companions respond in turn).
    ///              Now "pick a reason (3 chips or voice)". A is refused until there is one.
    ///   Ready      a reason is chosen; the strip reads "Keep this moment? Crane · 35° · It still looks up".
    ///   Saved      A: palace{object, yaw, reason, mode} goes into the record.
    ///
    /// B inside the undo bar, or lifting the piece back out, returns to Choosing and forgets the
    /// reason. The card fallback is the same flow with mode "card" and no yaw.
    /// Pure: the scene reports placements and choices; this says what may happen next.
    /// </summary>
    public sealed class PalaceFlow
    {
        public enum Phase { Choosing, Placed, Ready, Saved }
        public enum Mode { Miniature, Card }

        /// <summary>
        /// Her storyboard gives one reason in words, "It still looks up" (the crane's). The other
        /// chips are DRAFTS written for the test scene, to be replaced by hers.
        /// </summary>
        public static IReadOnlyList<string> ReasonsFor(string piece) =>
            string.Equals(piece, "Turtle", StringComparison.OrdinalIgnoreCase)
                ? new[] { "It holds steady", "It outlasts what hurries", "It carries its home" }
                : new[] { "It still looks up", "It is ready to fly", "It keeps its balance" };

        public Phase Current { get; private set; } = Phase.Choosing;
        public Mode Kind { get; private set; } = Mode.Miniature;
        public string Piece { get; private set; } = string.Empty;
        public int YawDeg { get; private set; }
        public string Reason { get; private set; } = string.Empty;
        public bool ReasonSpoken { get; private set; }

        public event Action<Phase> PhaseChanged;

        /// <summary>The piece is in the court (or the card face up).</summary>
        public void Placed(string piece, int yawDeg, Mode mode = Mode.Miniature)
        {
            if (Current == Phase.Saved) return;
            Piece = piece ?? string.Empty;
            YawDeg = mode == Mode.Card ? 0 : yawDeg;
            Kind = mode;
            Reason = string.Empty;
            ReasonSpoken = false;
            Go(Phase.Placed);
        }

        /// <summary>One of the three chips. Returns false when nothing is placed.</summary>
        public bool ChooseReason(string reason) => SetReason(reason, spoken: false);

        /// <summary>Hold-X speech. Empty or whitespace is not a reason.</summary>
        public bool SpeakReason(string transcript) => SetReason(transcript, spoken: true);

        bool SetReason(string reason, bool spoken)
        {
            if (Current != Phase.Placed && Current != Phase.Ready) return false;
            var r = (reason ?? string.Empty).Trim();
            if (r.Length == 0) return false;
            Reason = r.Length > 160 ? r.Substring(0, 160) : r;   // her lineChars cap
            ReasonSpoken = spoken;
            Go(Phase.Ready);
            return true;
        }

        /// <summary>A. Only once a reason is chosen.</summary>
        public bool CanSave => Current == Phase.Ready;

        public bool Save()
        {
            if (!CanSave) return false;
            Go(Phase.Saved);
            return true;
        }

        /// <summary>B inside the bar, or the piece lifted out: back to choosing, reason forgotten.</summary>
        public void Unplaced()
        {
            if (Current == Phase.Saved) return;
            Piece = string.Empty; Reason = string.Empty; YawDeg = 0; ReasonSpoken = false;
            Go(Phase.Choosing);
        }

        /// <summary>The strip's line: "Crane · 35° · It still looks up".</summary>
        public string Summary =>
            Piece + (Kind == Mode.Miniature ? " · " + YawDeg + "°" : "") + (Reason.Length > 0 ? " · " + Reason : "");

        public string ModeName => Kind == Mode.Card ? "card" : "miniature";

        void Go(Phase p)
        {
            Current = p;
            PhaseChanged?.Invoke(p);
        }
    }
}
