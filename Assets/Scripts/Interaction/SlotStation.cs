using System;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her slot system in a scene: a <see cref="SlotBoard"/> joined to real slot points and real
    /// pieces. It measures, the board decides, and this turns each <see cref="SlotCue"/> into what
    /// the hand feels and hears (§3.1 Feedback: visual + sound + haptics):
    ///
    ///   Aligned    light haptic on the holding hand.
    ///   Placed     the piece snaps level onto the slot at its yaw, the chapter chime, confirm haptic.
    ///   FloatHome  the piece glides back to its plinth.
    ///   Undone     B inside the undo bar: the piece glides home.
    ///   Confirmed  A: the chapter chime again; the pieces stop spinning and stay put.
    ///
    /// What it draws is deliberately nothing: the slot rings, cards and confirm strip are the UI
    /// layer's, which reads <see cref="Board"/> (StateOf, Choice, Cue) and <see cref="SlotLook"/>.
    ///
    /// A and B go to the station whose choice is waiting (the last one placed), never to every
    /// station at once.
    /// </summary>
    public sealed class SlotStation : MonoBehaviour
    {
        public SlotBoard Board { get; private set; }
        public Chapter Chapter { get; private set; }
        public IReadOnlyList<Transform> Slots => _slots;
        public IReadOnlyList<Holdable> Pieces => _pieces;
        public IReadOnlyList<string> SlotNames => _slotNames;

        /// <summary>Kept with A: which piece, which slot, its yaw relative to the slot's facing (0..359).</summary>
        public event Action<SlotStation, int, int, int> Confirmed;

        /// <summary>Every cue as it lands, after its sound and haptic have fired.</summary>
        public event Action<SlotStation, SlotEvent> Cue;

        /// <summary>The station A and B speak to.</summary>
        public static SlotStation Focus { get; private set; }

        Transform[] _slots;
        Holdable[] _pieces;
        string[] _slotNames;
        readonly float[] _distances = new float[8];
        float[] _scratch;
        InputAction _confirm, _redo;

        /// <summary>
        /// Build a station. <paramref name="slotNames"/> name the slots in the strip ("Detail", "Whole");
        /// pass null for a single unnamed slot.
        /// </summary>
        public static SlotStation Make(GameObject host, Chapter chapter, Transform[] slots, Holdable[] pieces,
                                       string[] slotNames = null)
        {
            var s = host.AddComponent<SlotStation>();
            s.Chapter = chapter;
            s._slots = slots;
            s._pieces = pieces;
            s._slotNames = slotNames ?? new string[slots.Length];
            s._scratch = new float[slots.Length];
            s.Board = new SlotBoard(slots.Length, pieces.Length);
            s.Board.Cue += s.OnCue;
            foreach (var p in pieces)
            {
                p.CanGrab = s.CanGrab;
                p.Moved += s.OnMoved;
                p.Released += s.OnReleased;
            }
            return s;
        }

        void Awake()
        {
            _confirm = new InputAction("slot-confirm", InputActionType.Button);
            _confirm.AddBinding("<XRController>{RightHand}/primaryButton");      // A
            _confirm.AddBinding("<Keyboard>/enter");
            _redo = new InputAction("slot-redo", InputActionType.Button);
            _redo.AddBinding("<XRController>{RightHand}/secondaryButton");       // B
            _redo.AddBinding("<Keyboard>/backspace");
            _confirm.Enable(); _redo.Enable();
        }

        void OnDestroy()
        {
            _confirm?.Dispose(); _redo?.Dispose();
            if (Focus == this) Focus = null;
        }

        void Update()
        {
            if (Board == null) return;
            Board.Tick(Time.deltaTime);
            if (Focus != this) return;
            if (_confirm.WasPressedThisFrame()) Confirm();
            if (_redo.WasPressedThisFrame()) Undo();
        }

        public bool Confirm() => Board.Confirm();
        public bool Undo() => Board.Undo();

        /// <summary>Her roundtable edit: lift the kept piece out again.</summary>
        public void Reopen()
        {
            Board.Reopen();
            Focus = this;
        }

        /// <summary>The kept or pending piece's yaw relative to its slot's facing, 0..359.</summary>
        public int PlacedYaw => Board.PlacedPiece < 0 ? 0 : YawInSlot(_pieces[Board.PlacedPiece], Board.PlacedSlot);

        int YawInSlot(Holdable piece, int slot)
        {
            var reference = slot >= 0 ? _slots[slot].eulerAngles.y : 0f;
            return StickStepper.Display(piece.Yaw - reference);
        }

        /// <summary>The strip's words for a placement: "Crane · 35°" for one slot, "Lamp · Whole" for named slots.</summary>
        string Summary(int piece, int slot)
        {
            var name = _pieces[piece].Id;
            if (!string.IsNullOrEmpty(_slotNames[slot])) return name + " · " + _slotNames[slot];
            return name + " · " + YawInSlot(_pieces[piece], slot) + "°";
        }

        bool CanGrab(Holdable piece) => Board.Grab(Array.IndexOf(_pieces, piece));

        void OnMoved(Holdable piece) => Board.Move(Distances(piece));

        void OnReleased(Holdable piece)
        {
            var i = Array.IndexOf(_pieces, piece);
            Board.Release(Distances(piece), slot => Summary(i, slot));
        }

        IReadOnlyList<float> Distances(Holdable piece)
        {
            var b = piece.BasePoint;
            for (var i = 0; i < _slots.Length; i++) _scratch[i] = Vector3.Distance(b, _slots[i].position);
            return _scratch;
        }

        void OnCue(SlotEvent e)
        {
            var piece = e.Piece >= 0 ? _pieces[e.Piece] : null;
            var hand = piece != null && piece.LastHeldBy != null ? piece.LastHeldBy.Source : null;
            switch (e.Cue)
            {
                case SlotCue.Aligned:
                    hand?.Buzz(SlotRules.LightAmplitude, SlotRules.LightSeconds);
                    break;
                case SlotCue.Placed:
                    piece.SeatAt(_slots[e.Slot].position, piece.Yaw);
                    ChimePlayer.Play(SlotRules.SoundOf(Chapter), _slots[e.Slot].position);
                    hand?.Buzz(SlotRules.ConfirmAmplitude, SlotRules.ConfirmSeconds);
                    Focus = this;
                    break;
                case SlotCue.FloatHome:
                case SlotCue.Undone:
                    piece.FloatHome();
                    break;
                case SlotCue.Confirmed:
                    ChimePlayer.Play(SlotRules.SoundOf(Chapter), _slots[e.Slot].position, 0.5f);
                    foreach (var p in _pieces) p.IdleSpin = false;
                    Confirmed?.Invoke(this, e.Piece, e.Slot, YawInSlot(piece, e.Slot));
                    break;
            }
            Cue?.Invoke(this, e);
        }
    }
}
