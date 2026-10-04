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
        const float SlotReadDistance = 2.5f;   // slot plates read from the station's viewing mark

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
            public string ShownSummary;
            public TextMeshProUGUI Undo;
        }

        readonly Dictionary<SlotStation, StationView> _stations = new Dictionary<SlotStation, StationView>();
        readonly Dictionary<TimeRingDial, List<(Transform root, Image back, int detent)>> _dials =
            new Dictionary<TimeRingDial, List<(Transform, Image, int)>>();
        Transform _angle, _hoverHalo, _hoverTip;
        TextMeshProUGUI _angleText;
        Transform _eye;

        // LateUpdate, so a slot redraws in the frame its state changes rather than one after:
        // the station's Cue(Placed) lands in its own Update.
        void LateUpdate()
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
            if (st.HideStrip && phase == ChoiceConfirm.Phase.Pending) phase = ChoiceConfirm.Phase.Open;
            // Rebuilt when its words change too: a chapter retitles the pending choice (the Palace adds
            // the chosen reason), and a strip built once kept showing the old line.
            if (phase != v.ShownPhase || (phase == ChoiceConfirm.Phase.Pending && st.Board.Choice.Summary != v.ShownSummary))
            {
                if (v.Strip != null) Destroy(v.Strip.gameObject);
                v.Strip = null; v.Undo = null;
                if (phase == ChoiceConfirm.Phase.Pending)
                {
                    var anchor = new GameObject("Confirm Strip Anchor").transform;
                    anchor.SetParent(transform, false);
                    // Above every slot card, never across one: a placed card reads "Saved / Placed"
                    // and must stay visible beside "Keep this moment?".
                    anchor.position = Above(v.Slots.Count > 0 ? v.Slots[0].Slot.position : st.transform.position, 0.5f);
                    anchor.position = new Vector3(anchor.position.x, Mathf.Max(anchor.position.y, CardsTop(v) + StripGap), anchor.position.z);
                    string detail = st.Board.Choice.Summary;
                    v.Strip = MuseScreens.ConfirmStrip(anchor, detail, NearDistance,
                                                       () => st.Confirm(), () => st.Undo(), st.Board.Choice.UndoFraction);
                    v.Undo = FindText(v.Strip, "Undo");
                }
                v.ShownPhase = phase;
                v.ShownSummary = st.Board.Choice.Summary;
            }
            if (v.Strip != null)
            {
                var sp = v.Strip.parent.position;
                v.Strip.parent.position = new Vector3(sp.x, Mathf.Max(sp.y, CardsTop(v) + StripGap + HalfHeight(v.Strip)), sp.z);
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

            // Her 4.3 words, hung like a museum label: low on the plinth's front, toward the visitor.
            // Never above the piece - from the eye that puts the card on the line to the piece (or to
            // the cards beyond it), and a translucent card over a bronze makes it a ghost.
            var cardAnchor = new GameObject("Slot Card").transform;
            cardAnchor.SetParent(transform, false);
            var eye = _eye != null ? _eye : (Camera.main != null ? Camera.main.transform : null);
            var toEye = eye != null ? Vector3.ProjectOnPlane(eye.position - sv.Slot.position, Vector3.up) : Vector3.zero;
            toEye = toEye.sqrMagnitude > 1e-4f ? toEye.normalized : Vector3.back;
            var support = SupportUnder(sv.Slot.position);
            float forward = support.HasValue ? HalfDepthToward(support.Value, toEye) + 0.02f : LabelForward;
            // A post too thin to carry a card keeps its label at the same reading height but words only:
            // no ring, so the text takes the whole 0.3 m and reads from the spawn. (A plaque at the
            // post's foot was 3 m from the eye and unreadable - measured by musexr-b, c9b6eaa.)
            bool thin = support.HasValue && Mathf.Min(support.Value.size.x, support.Value.size.z) < ThinStand;
            float width = LabelWidth;
            cardAnchor.position = sv.Slot.position + toEye * forward + Vector3.down * LabelDrop;
            if (cardAnchor.position.y < LabelFloor) cardAnchor.position = new Vector3(cardAnchor.position.x, LabelFloor, cardAnchor.position.z);
            var c = MuseUi.Canvas(cardAnchor, "Card", SlotReadDistance, 150f);   // read from where the visitor stands, not arm's length
            var card = MuseScreens.Slot(c, state == SlotState.Aligned ? SlotVisual.Aligned : state == SlotState.Placed ? SlotVisual.Placed : SlotVisual.Empty, 150f);
            // Her captions come from SlotLook (the placed one counts the undo down).
            var hint = FindText(card, "Hint"); if (hint != null) { hint.text = look.Caption; hint.color = MuseTheme.Ink; }
            // At label size the word inside the ring is too small to read; the title already says it.
            var ringWord = FindText(card, "Word"); if (ringWord != null) ringWord.gameObject.SetActive(false);
            if (thin)
                foreach (var img in card.GetComponentsInChildren<Image>(true))
                    if (img.name == "Ring") img.gameObject.SetActive(false);
            // Opaque: a label in front of a plinth must not let the room swim through its words.
            foreach (var img in card.GetComponentsInChildren<Image>(true))
                if (img.color.a > 0.3f && img.color.a < 0.97f) { var col = img.color; col.a = 0.97f; img.color = col; }
            if (!string.IsNullOrEmpty(slotName))
            {
                var title = FindText(card, "State");
                if (title != null) title.text = look.Title + " · " + slotName;
            }
            FitWidth(cardAnchor, width);
            sv.Card = cardAnchor;
        }

        /// <summary>The bounds of the stand a slot sits on (the smallest renderer whose top is at the slot),
        /// or null - a slot can sit on nothing, or on something without a renderer.</summary>
        Bounds? SupportUnder(Vector3 slot)
        {
            Bounds? best = null;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(transform) || r is ParticleSystemRenderer) continue;
                var b = r.bounds;
                if (Mathf.Abs(b.max.y - slot.y) > 0.06f) continue;
                if (slot.x < b.min.x - 0.01f || slot.x > b.max.x + 0.01f || slot.z < b.min.z - 0.01f || slot.z > b.max.z + 0.01f) continue;
                if (b.size.y < 0.3f || b.size.x > 1.5f || b.size.z > 1.5f) continue;   // a stand, not a floor or a wall
                if (best == null || b.size.x * b.size.z < best.Value.size.x * best.Value.size.z) best = b;
            }
            return best;
        }

        static float HalfDepthToward(Bounds b, Vector3 dir) =>
            Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;

        /// <summary>Scale a built label so its widest panel is <paramref name="metres"/> across.</summary>
        static void FitWidth(Transform anchor, float metres)
        {
            Canvas.ForceUpdateCanvases();   // layout groups size their panels a frame late otherwise
            float widest = 0f;
            var corners = new Vector3[4];
            foreach (var rt in anchor.GetComponentsInChildren<RectTransform>())
            {
                rt.GetWorldCorners(corners);
                widest = Mathf.Max(widest, Vector3.Distance(corners[0], corners[3]));
            }
            if (widest > metres && widest > 1e-4f) anchor.localScale *= metres / widest;
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
                var tag = MuseUi.Tag(c, "Stick rotate · 0°", MuseTheme.Rose, Color.white);   // her rose pill, white words
                _angleText = tag.GetComponentInChildren<TextMeshProUGUI>();
                _angleText.fontSize = 13f;
                _angle = anchor;
            }
            _angle.gameObject.SetActive(true);
            _angle.position = TopOf(held) + Vector3.up * 0.07f;
            Face(_angle);
            // The station's own reading, so it agrees with the strip ("Crane · 250°"); the pieces
            // idle-spin, so the stick's own count starts anywhere.
            var station = StationOf(held);
            int deg = station != null ? station.YawOf(held) : Mathf.RoundToInt(Mathf.Repeat(held.StickDegrees, 360f));
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
            // Not while a choice is waiting on A/B: the strip says what to do, and the tip would sit under it.
            bool pending = false;
            foreach (var st in _stations.Keys)
                if (st != null && st.Board != null && st.Board.Choice.Current == ChoiceConfirm.Phase.Pending) pending = true;
            bool show = target != null && !pending;
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
                MuseUi.Tag(tc, "Hold Grip to pick up", MuseTheme.Ink, Color.white);   // dark pill, white words
                _hoverTip = b;
            }
            if (_hoverHalo == null) return;
            _hoverHalo.gameObject.SetActive(show);
            _hoverTip.gameObject.SetActive(show);
            if (!show) return;
            var holdable = target as Holdable;
            var basePoint = holdable != null ? holdable.BasePoint : target.transform.position;
            _hoverHalo.SetPositionAndRotation(basePoint + Vector3.up * 0.006f, Quaternion.Euler(90f, 0f, 0f));
            _hoverTip.position = TopOf(target) + Vector3.up * 0.09f;
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
                // The dial's convention (as its notches): local XY, +Z away from the visitor, +X their
                // right, a detent at (sin a, cos a) * r. Mist -60 upper left, Afternoon 0 top, Dusk +60.
                float ang = DialDetents.AngleOf(i) * Mathf.Deg2Rad;
                a.position = dial.transform.TransformPoint(new Vector3(Mathf.Sin(ang), Mathf.Cos(ang), 0f) * (LocalRadius(dial, radius) + 0.09f / Mathf.Max(1e-4f, dial.transform.lossyScale.x)));
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

        /// <summary>Past this much off the visitor, a panel turns to face them again; below it, it holds still.</summary>
        const float RefaceDegrees = 20f, TurnDegreesPerSecond = 120f;
        readonly HashSet<Transform> _faced = new HashSet<Transform>(), _turning = new HashSet<Transform>();

        /// <summary>
        /// Turn a panel to face the visitor (+Z away reads) - at once when it is new, then only once the
        /// visitor has moved more than RefaceDegrees round it, and then smoothly. Re-aiming every frame
        /// made the strip and labels swim with every small head movement (Saul, headset test).
        /// </summary>
        void Face(Transform t)
        {
            if (t == null || _eye == null) return;
            var away = t.position - _eye.position; away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            var want = Quaternion.LookRotation(away, Vector3.up);
            if (_faced.Add(t)) { t.rotation = want; if (_faced.Count > 128) _faced.RemoveWhere(x => x == null); return; }
            var off = Quaternion.Angle(t.rotation, want);
            if (off > RefaceDegrees) _turning.Add(t);
            if (!_turning.Contains(t)) return;
            t.rotation = Quaternion.RotateTowards(t.rotation, want, TurnDegreesPerSecond * Time.deltaTime);
            if (off < 1f) _turning.Remove(t);
        }

        static Vector3 Above(Vector3 p, float h) => p + Vector3.up * h;

        const float StripGap = 0.04f;
        const float LabelForward = 0.2f;    // proud of the slot when nothing is found beneath it
        const float LabelDrop = 0.3f;       // below the surface the piece stands on, like a plinth label
        const float LabelFloor = 0.35f;     // never down at the visitor's feet
        const float LabelWidth = 0.3f;      // a museum label, not a sign
        const float ThinStand = 0.2f;       // a stand narrower than this is a post


        /// <summary>The world top of a thing's renderers, centred over it - where a label clears it.</summary>
        static Vector3 TopOf(Component c)
        {
            var rs = c.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return c.transform.position;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        SlotStation StationOf(Holdable piece)
        {
            foreach (var st in _stations.Keys)
                if (st != null) foreach (var p in st.Pieces) if (p == piece) return st;
            return null;
        }

        static float CardsTop(StationView v)
        {
            float top = float.MinValue;
            foreach (var sv in v.Slots)
                if (sv.Card != null)
                    foreach (var rt in sv.Card.GetComponentsInChildren<RectTransform>())
                    {
                        var corners = new Vector3[4]; rt.GetWorldCorners(corners);
                        foreach (var c in corners) top = Mathf.Max(top, c.y);
                    }
            return top == float.MinValue ? (v.Station != null ? v.Station.transform.position.y : 0f) : top;
        }

        static float HalfHeight(RectTransform rt)
        {
            var corners = new Vector3[4]; rt.GetWorldCorners(corners);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var c in corners) { lo = Mathf.Min(lo, c.y); hi = Mathf.Max(hi, c.y); }
            return (hi - lo) * 0.5f;
        }

        static float LocalRadius(TimeRingDial dial, float worldRadius) =>
            worldRadius / Mathf.Max(1e-4f, dial.transform.lossyScale.x);

        static TextMeshProUGUI FindText(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
