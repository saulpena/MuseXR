using System;
using System.Collections.Generic;

namespace MuseXR.Slots
{
    /// <summary>Something the board did that the scene must show, sound or buzz.</summary>
    public enum SlotCue
    {
        /// <summary>A held piece came within the snap radius of a free slot. Light haptic.</summary>
        Aligned,
        /// <summary>It left again without being released.</summary>
        Unaligned,
        /// <summary>Released inside the radius: snap, chapter chime, confirm haptic, strip appears.</summary>
        Placed,
        /// <summary>Released outside every slot: the piece floats back to its plinth.</summary>
        FloatHome,
        /// <summary>A placed piece was picked up again before A: the slot empties, the strip goes.</summary>
        Lifted,
        /// <summary>B inside the undo bar: the piece floats home and the slot empties.</summary>
        Undone,
        /// <summary>A: the choice is kept.</summary>
        Confirmed,
    }

    public readonly struct SlotEvent
    {
        public readonly SlotCue Cue;
        public readonly int Slot;
        public readonly int Piece;
        public SlotEvent(SlotCue cue, int slot, int piece) { Cue = cue; Slot = slot; Piece = piece; }
        public override string ToString() => Cue + " slot " + Slot + " piece " + Piece;
    }

    /// <summary>
    /// Her shared slot system (chatplan §3.2) as logic: slots, the pieces that can go in them, and
    /// one choice per chapter. Palace is two pieces (crane, turtle) and one court slot; Grotto is one
    /// lamp and two sockets ("detail", "whole"). Both are this class.
    ///
    /// The scene measures and the board decides: every call takes the held piece's distance to each
    /// slot, in metres, so the rules (the 12 cm radius, nearest free slot wins, one placed piece per
    /// board, float home on a miss) run in EditMode with no headset.
    ///
    /// One choice per chapter: while a piece is placed no other slot is free, so a second piece
    /// brought to the court does not align and floats home. Lifting the placed piece out frees it.
    /// </summary>
    public sealed class SlotBoard
    {
        readonly int _slots, _pieces;
        readonly float _radius;

        public SlotBoard(int slots, int pieces, float snapRadius = SlotRules.SnapRadius,
                         float undoSeconds = SlotRules.UndoSeconds)
        {
            if (slots < 1) throw new ArgumentOutOfRangeException(nameof(slots));
            if (pieces < 1) throw new ArgumentOutOfRangeException(nameof(pieces));
            _slots = slots; _pieces = pieces; _radius = snapRadius;
            Choice = new ChoiceConfirm(undoSeconds);
        }

        public int SlotCount => _slots;
        public int PieceCount => _pieces;

        /// <summary>The confirm strip and undo bar for this board's one choice.</summary>
        public ChoiceConfirm Choice { get; }

        public int Held { get; private set; } = -1;
        public int AlignedSlot { get; private set; } = -1;
        public int PlacedSlot { get; private set; } = -1;
        public int PlacedPiece { get; private set; } = -1;

        /// <summary>Every cue, in order, as it happens. The scene turns each into shape, sound and haptics.</summary>
        public event Action<SlotEvent> Cue;

        public SlotState StateOf(int slot)
        {
            if (slot == PlacedSlot) return SlotState.Placed;
            if (slot == AlignedSlot) return SlotState.Aligned;
            return SlotState.Empty;
        }

        public bool IsFree(int slot) => PlacedSlot < 0 && slot >= 0 && slot < _slots;

        /// <summary>Grip on a piece. Refused once the choice is confirmed (until Reopen).</summary>
        public bool Grab(int piece)
        {
            if (piece < 0 || piece >= _pieces || Choice.IsConfirmed) return false;
            if (Held >= 0 && Held != piece) return false;   // one piece in hand at a time
            Held = piece;
            if (piece == PlacedPiece)
            {
                var slot = PlacedSlot;
                PlacedSlot = PlacedPiece = -1;
                Choice.Clear();
                Raise(SlotCue.Lifted, slot, piece);
            }
            return true;
        }

        /// <summary>
        /// The held piece moved. <paramref name="distances"/> is its base's distance to each slot.
        /// Returns the slot it is aligned with, or -1.
        /// </summary>
        public int Move(IReadOnlyList<float> distances)
        {
            if (Held < 0) return -1;
            var next = Nearest(distances);
            if (next != AlignedSlot)
            {
                var was = AlignedSlot;
                AlignedSlot = next;
                if (was >= 0) Raise(SlotCue.Unaligned, was, Held);
                if (next >= 0) Raise(SlotCue.Aligned, next, Held);
            }
            return AlignedSlot;
        }

        /// <summary>
        /// Let go. Inside the radius of a free slot it is placed there and the choice is made with
        /// <paramref name="summary"/>; anywhere else it floats home. Returns the slot, or -1.
        /// </summary>
        public int Release(IReadOnlyList<float> distances, string summary = null) =>
            Release(distances, _ => summary);

        /// <summary>As above, with the summary written once the slot is known ("Lamp · Whole").</summary>
        public int Release(IReadOnlyList<float> distances, Func<int, string> summaryForSlot)
        {
            if (Held < 0) return -1;
            var piece = Held;
            Held = -1;
            var slot = Nearest(distances);
            AlignedSlot = -1;
            if (slot < 0)
            {
                Raise(SlotCue.FloatHome, -1, piece);
                return -1;
            }
            PlacedSlot = slot;
            PlacedPiece = piece;
            Choice.Make(summaryForSlot?.Invoke(slot));
            Raise(SlotCue.Placed, slot, piece);
            return slot;
        }

        /// <summary>B. Inside the undo bar the piece floats home and the slot empties.</summary>
        public bool Undo()
        {
            if (PlacedPiece < 0 || !Choice.Redo()) return false;
            var slot = PlacedSlot; var piece = PlacedPiece;
            PlacedSlot = PlacedPiece = -1;
            Raise(SlotCue.Undone, slot, piece);
            return true;
        }

        /// <summary>A.</summary>
        public bool Confirm()
        {
            if (PlacedPiece < 0 || !Choice.Confirm()) return false;
            Raise(SlotCue.Confirmed, PlacedSlot, PlacedPiece);
            return true;
        }

        /// <summary>The roundtable's edit: the piece can be lifted out again.</summary>
        public void Reopen() => Choice.Reopen();

        public void Tick(float deltaSeconds) => Choice.Tick(deltaSeconds);

        int Nearest(IReadOnlyList<float> distances)
        {
            if (distances == null || PlacedSlot >= 0) return -1;
            var best = -1;
            var bestD = float.MaxValue;
            var n = Math.Min(distances.Count, _slots);
            for (var i = 0; i < n; i++)
            {
                var d = distances[i];
                if (float.IsNaN(d) || d > _radius || d >= bestD) continue;
                best = i; bestD = d;
            }
            return best;
        }

        void Raise(SlotCue cue, int slot, int piece) => Cue?.Invoke(new SlotEvent(cue, slot, piece));
    }
}
