using System.Collections.Generic;
using MuseXR.Interaction;
using MuseXR.Slots;
using MuseXR.Worlds;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MuseXR.UI
{
    /// <summary>
    /// The visible half of Skylar's hand interactions (her storyboards A, B and D, and 4.3), drawn in
    /// her UI kit on top of the MuseXR-B interactions API - which owns the logic and stays untouched:
    ///
    ///   - every slot: a flat ring on its surface and a label card above it, both from SlotLook.For
    ///     (dashed "Empty / Waiting", rose halo "Release / Aligned", gold "Saved / Placed");
    ///   - her confirm strip while a choice is pending: Board.Choice.StripLine, an undo countdown,
    ///     A Confirm / B Redo (wired to the station);
    ///   - the held-angle readout her storyboard draws in rose: "Stick rotate · 35°";
    ///   - a grab affordance: a rose halo under whatever a hand is pointing at, and "Hold Grip";
    ///   - the time-ring dial's three detents, each "text plus icon" (mist, sun, sunset).
    ///
    /// One per scene. It finds stations, pieces and dials itself and keeps up as they appear.
    /// </summary>
    public sealed class InteractionVisuals : MonoBehaviour
    {
        const float NearDistance = 1.2f;   // panels read at arm's length

        sealed class SlotView
        {
            public Transform Slot;
            public Transform Ring, Card;
            public SlotState Shown = (SlotState)(-1);
            public float ShownUndo = -1f;
        }

        sealed class StationView
        {
            public SlotStation Station;
            public readonly List<SlotView> Slots = new List<SlotView>();
            public RectTransform Strip;
            public ChoiceConfirm.Phase ShownPhase = (ChoiceConfirm.Phase)(-1);
            public TextMeshProUGUI Undo;
        }

        readonly Dictionary<SlotStation, StationView> _stations = new Dictionary<SlotStation, StationView>();
        readonly Dictionary<TimeRingDial, List<(Transform root, Image back, int detent)>> _dials =
            new Dictionary<TimeRingDial, List<(Transform, Image, int)>>();
        Transform _angle, _hoverHalo, _hoverTip;
        TextMeshProUGUI _angleText;
        Transform _eye;

        void Update()
        {
            if (_eye == null && Camera.main != null) _eye = Camera.main.transform;
            foreach (var s in FindObjectsByType<SlotStation>(FindObjectsSortMode.None))
                if (!_stations.ContainsKey(s)) _stations[s] = new StationView { Station = s };
            foreach (var d in FindObjectsByType<TimeRingDial>(FindObjectsSortMode.None))
                if (!_dials.ContainsKey(d)) _dials[d] = BuildDial(d);

            foreach (var v in _stations.Values) UpdateStation(v);
            foreach (var kv in _dials) UpdateDial(kv.Key, kv.Value);
            UpdateHeld();
            UpdateHover();
        }

        // ---- slots ------------------------------------------------------------------------------

        void UpdateStation(StationView v)
        {
            var st = v.Station;
            if (st == null || st.Board == null) return;
            while (v.Slots.Count < st.Slots.Count) v.Slots.Add(new SlotView { Slot = st.Slots[v.Slots.Count] });

            for (var i = 0; i < v.Slots.Count; i++)
            {
                var sv = v.Slots[i];
                var state = st.Board.StateOf(i);
                float undo = state == SlotState.Placed ? Mathf.Ceil(st.Board.Choice.UndoLeft) : 0f;
                if (state != sv.Shown || undo != sv.ShownUndo)
                {
                    RebuildSlot(sv, state, st.Board.Choice.UndoLeft, i < st.SlotNames.Count ? st.SlotNames[i] : null);
                    sv.Shown = state; sv.ShownUndo = undo;
                }
                Face(sv.Card);
            }

            var phase = st.Board.Choice.Current;
            if (phase != v.ShownPhase)
            {
                if (v.Strip != null) Destroy(v.Strip.gameObject);
                v.Strip = null; v.Undo = null;
                if (phase == ChoiceConfirm.Phase.Pending)
                {
                    var anchor = new GameObject("Confirm Strip Anchor").transform;
                    anchor.SetParent(transform, false);
                    anchor.position = Above(v.Slots.Count > 0 ? v.Slots[0].Slot.position : st.transform.position, 0.62f);
                    string detail = st.Board.Choice.Summary;
                    v.Strip = MuseScreens.ConfirmStrip(anchor, detail, NearDistance,
                                                       () => st.Confirm(), () => st.Undo(), st.Board.Choice.UndoFraction);
                    v.Undo = FindText(v.Strip, "Undo");
                }
                v.ShownPhase = phase;
            }
            if (v.Strip != null)
            {
                Face(v.Strip.parent);
                if (v.Undo != null) v.Undo.text = st.Board.Choice.CanRedo ? "Undo " + Mathf.CeilToInt(st.Board.Choice.UndoLeft) + "s" : "";
            }
        }

        void RebuildSlot(SlotView sv, SlotState state, float undoLeft, string slotName)
        {
            if (sv.Ring != null) Destroy(sv.Ring.gameObject);
            if (sv.Card != null) Destroy(sv.Card.gameObject);
            var look = MuseXR.Slots.SlotLook.For(state, undoLeft);

            // The ring on the surface: 24 cm across, her 12 cm snap radius made visible.
            var ringAnchor = new GameObject("Slot Ring").transform;
            ringAnchor.SetParent(transform, false);
            ringAnchor.SetPositionAndRotation(sv.Slot.position + Vector3.up * 0.004f, Quaternion.Euler(90f, 0f, 0f));
            var canvas = MuseUi.Canvas(ringAnchor, "Ring", NearDistance, 62f);
            canvas.localScale = Vector3.one * (0.24f / 62f);
            var holder = MuseUi.Row(canvas, 0f, TextAnchor.MiddleCenter);
            switch (look.Shape)
            {
                case SlotShape.SolidHalo:
                    MuseUi.Ring(holder, UiSprites.Ring(0.22f), MuseTheme.Rose, 62f); break;
                case SlotShape.Filled:
                    MuseUi.Ring(holder, UiSprites.Disc(), new Color(MuseTheme.GoldSoft.r, MuseTheme.GoldSoft.g, MuseTheme.GoldSoft.b, 0.85f), 62f);
                    break;
                default:
                    MuseUi.Ring(holder, UiSprites.Ring(0.05f, 18), MuseTheme.Ink3, 62f); break;
            }
            sv.Ring = ringAnchor;

            // The card above it, her 4.3 words.
            var cardAnchor = new GameObject("Slot Card").transform;
            cardAnchor.SetParent(transform, false);
            cardAnchor.position = Above(sv.Slot.position, 0.48f);   // clear of a seated piece (the crane stands ~0.3 m)
            var c = MuseUi.Canvas(cardAnchor, "Card", NearDistance, 150f);
            var card = MuseScreens.Slot(c, state == SlotState.Aligned ? SlotVisual.Aligned : state == SlotState.Placed ? SlotVisual.Placed : SlotVisual.Empty, 150f);
            // Her captions come from SlotLook (the placed one counts the undo down).
            var hint = FindText(card, "Hint"); if (hint != null) hint.text = look.Caption;
            if (!string.IsNullOrEmpty(slotName))
            {
                var title = FindText(card, "State");
                if (title != null) title.text = look.Title + " · " + slotName;
            }
            sv.Card = cardAnchor;
        }

        // ---- held angle --------------------------------------------------------------------------

        void UpdateHeld()
        {
            Holdable held = null;
            foreach (var h in FindObjectsByType<Holdable>(FindObjectsSortMode.None))
                if (h.State == Holdable.Mode.Held) { held = h; break; }
            if (held == null) { if (_angle != null) _angle.gameObject.SetActive(false); return; }
            if (_angle == null)
            {
                var anchor = new GameObject("Held Angle").transform; anchor.SetParent(transform, false);
                var c = MuseUi.Canvas(anchor, "Angle", NearDistance, 160f);
                var tag = MuseUi.Tag(c, "Stick rotate · 0°", Color.white, MuseTheme.Rose);
                _angleText = tag.GetComponentInChildren<TextMeshProUGUI>();
                _angleText.fontSize = 13f;
                _angle = anchor;
            }
            _angle.gameObject.SetActive(true);
            _angle.position = held.transform.position + Vector3.up * 0.16f;
            Face(_angle);
            int deg = Mathf.RoundToInt(Mathf.Repeat(held.StickDegrees, 360f));
            _angleText.text = "Stick rotate · " + deg + "°";
        }

        // ---- hover affordance --------------------------------------------------------------------

        void UpdateHover()
        {
            Component target = null; bool byRay = false;
            foreach (var hand in GripHand.All)
            {
                if (hand == null || hand.Held != null) continue;
                var t = hand.Target(out var r) as Component;
                if (t != null) { target = t; byRay = r; break; }
            }
            bool show = target != null;
            if (_hoverHalo == null && show)
            {
                var a = new GameObject("Hover Halo").transform; a.SetParent(transform, false);
                var c = MuseUi.Canvas(a, "Halo", NearDistance, 62f);
                c.localScale = Vector3.one * (0.22f / 62f);
                var row = MuseUi.Row(c, 0f, TextAnchor.MiddleCenter);
                MuseUi.Ring(row, UiSprites.Ring(0.18f), new Color(MuseTheme.Rose.r, MuseTheme.Rose.g, MuseTheme.Rose.b, 0.8f), 62f);
                _hoverHalo = a;
                var b = new GameObject("Hover Tip").transform; b.SetParent(transform, false);
                var tc = MuseUi.Canvas(b, "Tip", NearDistance, 200f);
                MuseUi.Tag(tc, "Hold Grip to pick up", Color.white, MuseTheme.Ink);
                _hoverTip = b;
            }
            if (_hoverHalo == null) return;
            _hoverHalo.gameObject.SetActive(show);
            _hoverTip.gameObject.SetActive(show);
            if (!show) return;
            var holdable = target as Holdable;
            var basePoint = holdable != null ? holdable.BasePoint : target.transform.position;
            _hoverHalo.SetPositionAndRotation(basePoint + Vector3.up * 0.006f, Quaternion.Euler(90f, 0f, 0f));
            _hoverTip.position = target.transform.position + Vector3.up * 0.22f;
            Face(_hoverTip);
            var tipText = _hoverTip.GetComponentInChildren<TextMeshProUGUI>();
            if (tipText != null) tipText.text = target is TimeRingDial ? "Grip the ring and turn" : (byRay ? "Hold Grip to pick it up" : "Hold Grip to pick up");
        }

        // ---- the time ring's detents --------------------------------------------------------------

        static readonly string[] DetentWord = { "Mist", "Afternoon", "Dusk" };
        static readonly string[] DetentIcon = { "mist", "sun", "sunset" };

        List<(Transform, Image, int)> BuildDial(TimeRingDial dial)
        {
            var list = new List<(Transform, Image, int)>();
            var ring = dial.Ring != null ? dial.Ring : dial.transform;
            var rend = ring.GetComponentInChildren<Renderer>();
            float radius = rend != null ? Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.z) : 0.25f;
            for (var i = 0; i < DialDetents.Count; i++)
            {
                var a = new GameObject("Detent " + DetentWord[i]).transform;
                a.SetParent(transform, false);
                var dir = Quaternion.AngleAxis(DialDetents.AngleOf(i), ring.up) * Vector3.ProjectOnPlane(-(_eye != null ? _eye.forward : Vector3.forward), ring.up).normalized;
                a.position = ring.position + dir * (radius + 0.09f) + ring.up * 0.02f;
                var c = MuseUi.Canvas(a, "Detent", NearDistance, 120f);
                var pill = MuseUi.Card(c, MuseTheme.Paper, 14f, MuseTheme.Line, 1f, padX: 8f, padY: 4f, gap: 0f, name: "Detent");
                var row = MuseUi.Row(pill, 6f, TextAnchor.MiddleCenter);
                MuseUi.Ring(row, UiSprites.Icon(DetentIcon[i]), MuseTheme.Ink2, 18f, "Icon");
                var t = MuseUi.Text(row, DetentWord[i], MuseUi.Face.SansSemi, 12f, MuseTheme.Ink, name: "Word");
                t.enableWordWrapping = false;
                list.Add((a, pill.GetComponent<Image>(), i));
            }
            return list;
        }

        void UpdateDial(TimeRingDial dial, List<(Transform root, Image back, int detent)> labels)
        {
            int current = dial.Detents.Detent;
            foreach (var (root, back, detent) in labels)
            {
                Face(root);
                back.color = detent == current ? MuseTheme.GoldSoft : MuseTheme.Paper;
            }
        }

        // ---- helpers -----------------------------------------------------------------------------

        void Face(Transform t)
        {
            if (t == null || _eye == null) return;
            var away = t.position - _eye.position; away.y = 0f;
            if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, Vector3.up);   // +Z away reads
        }

        static Vector3 Above(Vector3 p, float h) => p + Vector3.up * h;

        static TextMeshProUGUI FindText(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
