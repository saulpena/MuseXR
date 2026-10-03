using System;

namespace MuseXR.Slots
{
    /// <summary>
    /// Her confirm strip and undo bar, as logic (UI 4.3: "Keep this moment? Dusk · Water Lilies",
    /// A Confirm / B Redo). Every chapter's choice goes through one of these: the slot board owns one,
    /// and the Van Gogh stroke and the Monet ring, which have no slot, use one directly.
    ///
    ///   Open      nothing chosen yet.
    ///   Pending   a choice is made. The undo bar runs for 3 s; the strip shows A Confirm / B Redo.
    ///   Confirmed A was pressed. Reopen() makes it editable again (her roundtable edit).
    ///
    /// Her check (§3.1): placing, then B at 2.9 s restores the prior state; at 3.1 s it does not.
    /// Chapters whose retakes are unlimited (Van Gogh "B redraws", Monet "B turns the ring again")
    /// pass <c>redoAnyTime</c>, and B then works until A.
    /// Pure: no UnityEngine. The scene feeds it time.
    /// </summary>
    public sealed class ChoiceConfirm
    {
        public enum Phase { Open, Pending, Confirmed }

        readonly float _undoSeconds;
        readonly bool _redoAnyTime;

        public ChoiceConfirm(float undoSeconds = SlotRules.UndoSeconds, bool redoAnyTime = false)
        {
            _undoSeconds = undoSeconds > 0f ? undoSeconds : 0f;
            _redoAnyTime = redoAnyTime;
        }

        public Phase Current { get; private set; } = Phase.Open;

        /// <summary>What was chosen, as the strip names it: "Crane · 35°", "Dusk · Water Lilies".</summary>
        public string Summary { get; private set; } = string.Empty;

        /// <summary>Seconds left on the undo bar; 0 when it is not running.</summary>
        public float UndoLeft { get; private set; }

        /// <summary>The bar's fill, 1 when just placed, 0 when run out.</summary>
        public float UndoFraction => _undoSeconds > 0f ? UndoLeft / _undoSeconds : 0f;

        public bool CanRedo => Current == Phase.Pending && (_redoAnyTime || UndoLeft > 0f);
        public bool CanConfirm => Current == Phase.Pending;
        public bool IsConfirmed => Current == Phase.Confirmed;

        public event Action<Phase> PhaseChanged;

        /// <summary>A choice is made (or remade): the strip appears and the bar starts full.</summary>
        public void Make(string summary)
        {
            if (Current == Phase.Confirmed) return;
            Summary = summary ?? string.Empty;
            UndoLeft = _undoSeconds;
            Go(Phase.Pending);
        }

        /// <summary>B. Returns false, and changes nothing, once the bar has run out (unless redoAnyTime).</summary>
        public bool Redo()
        {
            if (!CanRedo) return false;
            Clear();
            return true;
        }

        /// <summary>The choice was taken back by hand (lifted out of the slot): no time limit before A.</summary>
        public void Clear()
        {
            if (Current != Phase.Pending) return;
            Summary = string.Empty;
            UndoLeft = 0f;
            Go(Phase.Open);
        }

        /// <summary>A.</summary>
        public bool Confirm()
        {
            if (!CanConfirm) return false;
            UndoLeft = 0f;
            Go(Phase.Confirmed);
            return true;
        }

        /// <summary>Her "still editable at the roundtable": back to Pending, with no undo bar.</summary>
        public void Reopen()
        {
            if (Current != Phase.Confirmed) return;
            UndoLeft = 0f;
            Go(Phase.Pending);
        }

        public void Tick(float deltaSeconds)
        {
            if (Current != Phase.Pending || UndoLeft <= 0f) return;
            UndoLeft -= deltaSeconds > 0f ? deltaSeconds : 0f;
            if (UndoLeft < 0f) UndoLeft = 0f;
        }

        /// <summary>Her strip's question with the choice after it.</summary>
        public string StripLine => "Keep this moment? " + Summary;

        void Go(Phase next)
        {
            Current = next;
            PhaseChanged?.Invoke(next);
        }
    }
}
