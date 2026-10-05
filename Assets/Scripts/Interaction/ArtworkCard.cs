using MusePico.Dialogue;
using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her artwork card (MUSE-VR-design 4.1, "appears on point"): after 0.4 s of a ray on the frame, or on
    /// stepping within 1.2 m of its viewing mark, a card stands 0.3 m right of the frame at the same depth
    /// (below it, when the work is under 1 m wide), facing the viewing mark, with the six required fields
    /// read from artworks.json: title, artist, date, source, rights, source URL. AI studies carry a purple
    /// "AI" tag. Buttons show controller letters: A hears the companions on it, B closes. One card at a
    /// time; it goes when the visitor walks away or looks elsewhere for a while.
    /// </summary>
    public sealed class ArtworkCard : MonoBehaviour, IConfirmable
    {
        public const float Beside = 0.3f, NarrowWork = 1f, LeaveDistance = 4.5f, LookAwaySeconds = 6f;
        /// <summary>Saul, 5 Oct: with the ray on nothing at all for this long, the card goes.</summary>
        public const float PointAwaySeconds = 5f;
        float _pointedAway;
        static readonly Color AiPurple = new Color32(0x7a, 0x5c, 0xb8, 0xff);

        /// <summary>The open card, or a real null: a card destroyed without Close (Play stopped) never reads as one.</summary>
        public static ArtworkCard Current { get => _current != null ? _current : null; private set => _current = value; }
        static ArtworkCard _current;

        void OnDestroy() { if (_current == this) _current = null; }

        /// <summary>
        /// While true, no artwork card or reply chips open (and an open card closes): the Monet round table sets
        /// it for as long as it runs, so a work hung nearby does not talk over the companions.
        /// </summary>
        public static bool Hushed
        {
            get => _hushed;
            set { _hushed = value; if (value && Current != null) Current.Close(); }
        }
        static bool _hushed;

        ArtworkRecord _record;
        Transform _work;
        Vector3? _centre, _facing;
        bool _piece;
        InsightTarget _insight;
        float _away;
        bool _tookInput;

        /// <summary>Is this an AI interpretive study rather than a real work (artworks.json's own marking)?</summary>
        public static bool IsAiStudy(ArtworkRecord r) =>
            r != null && ((r.date ?? "").IndexOf("AI", System.StringComparison.Ordinal) >= 0 || (r.rights ?? "").StartsWith("AI-generated"));

        /// <summary>Where the card stands: right of the frame, or under it for a narrow work.</summary>
        public static Vector3 Offset(Vector2 workSize, float cardHeight) =>
            workSize.x < NarrowWork ? new Vector3(0f, -(workSize.y * 0.5f + Beside + cardHeight * 0.5f), 0f)
                                    : new Vector3(workSize.x * 0.5f + Beside + 0.32f, 0f, 0f);

        /// <summary>
        /// A 3D piece's card (Saul, 5 Oct: "make it uniform"): the same card, buttons and rules as a hung work's, built
        /// from what the piece knows (its name and maker), standing beside it at reading height and facing the visitor.
        /// </summary>
        public static ArtworkCard ShowFor(InsightTarget piece)
        {
            if (piece == null) return null;
            var rs = piece.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return null;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var eye = Camera.main != null ? Camera.main.transform.position : b.center - Vector3.forward;
            var toEye = eye - b.center; toEye.y = 0f;
            var facing = toEye.sqrMagnitude > 1e-4f ? toEye.normalized : Vector3.back;
            var right = Vector3.Cross(Vector3.up, -facing).normalized;
            // Its width across the visitor's view, never "narrow": a piece's card goes beside it, not down by the floor.
            var across = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(right.x), 0f, Mathf.Abs(right.z)))) * 2f;
            // At the piece's near face, but never closer than 1.5 m to the visitor (a 10 m group's near face is where they stand).
            var near = Mathf.Min(Mathf.Min(b.extents.x, b.extents.z), Mathf.Max(0f, toEye.magnitude - 1.5f));
            var centre = new Vector3(b.center.x, Mathf.Clamp(b.center.y, 1.1f, 1.7f), b.center.z) + facing * near;
            var record = new ArtworkRecord { id = piece.id, title = piece.title, artist = piece.artist };
            return Show(record, piece.transform, new Vector2(Mathf.Max(across, NarrowWork), b.size.y), piece, centre, facing, true);
        }

        public static ArtworkCard Show(ArtworkRecord record, Transform work, Vector2 workSize, InsightTarget insight) =>
            Show(record, work, workSize, insight, null, null, false);

        static ArtworkCard Show(ArtworkRecord record, Transform work, Vector2 workSize, InsightTarget insight, Vector3? centre, Vector3? facing, bool piece)
        {
            if (Hushed) return null;
            // Saul, 5 Oct: "Hear companions" while they are already on it reads as a second, different action.
            if (insight != null && MasterInsights.Ensure().Discussing(insight)) return null;
            if (Current != null)
            {
                if (Current._work == work) return Current;
                Current.Close();
            }
            var go = new GameObject("Artwork Card · " + record.title);
            var card = go.AddComponent<ArtworkCard>();
            card._record = record; card._work = work; card._insight = insight;
            card._centre = centre; card._facing = facing; card._piece = piece;
            card.Build(workSize);
            MasterInsights.Ensure().CardOpened(insight);   // only the newly pointed-at work keeps a panel
            Appear.In(go, 0.3f);   // eased, never popped (Saul, 5 Oct)
            Current = card;
            // A and B are the card's only while nothing else holds them (a chapter's own choice keeps its A).
            if (ConfirmInput.Focus == null) { ConfirmInput.Take(card); card._tookInput = true; }
            return card;
        }

        void Build(Vector2 workSize)
        {
            // The work's quad faces its -Z; "right of the frame" is right as the visitor faces the work.
            var facing = _facing ?? -_work.forward;
            var origin = _centre ?? _work.position;
            var right = Vector3.Cross(Vector3.up, -facing).normalized;
            var o = Offset(workSize, 0.42f);
            // The side with room (Saul, 5 Oct): at the Gate the easels stand close, and the right side put the card over
            // the next painting. Right by default; left when another work stands where the card would.
            if (o.x > 0f && Crowded(origin + right * o.x, _work))
                o = !Crowded(origin - right * o.x, _work) ? new Vector3(-o.x, o.y, 0f)
                    : _piece ? o : Offset(new Vector2(0f, workSize.y), 0.42f);   // both sides taken: under a hung work
            var at = origin + right * o.x + Vector3.up * o.y + facing * 0.02f;
            transform.SetPositionAndRotation(at, Quaternion.LookRotation(-facing, Vector3.up));   // +Z away from the viewer reads

            var c = MuseUi.Canvas(transform, "Card", 2.6f, 340f);   // read from the viewing mark, 2.1 m out and to the side
            // Her milk-glass with a gold hairline.
            var glass = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Gold, 1f, padX: 14f, padY: 12f, gap: 4f, name: "Card");
            if (IsAiStudy(_record))
            {
                var tag = MuseUi.Card(glass, AiPurple, 4f, null, 0f, padX: 6f, padY: 2f, name: "AI tag");
                tag.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0f;
                glass.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childForceExpandWidth = false;
                MuseUi.Text(tag, "AI", MuseUi.Face.SansSemi, 11f, Color.white, 0.2f, true, name: "AI");
            }
            var title = MuseUi.Text(glass, _record.title, MuseUi.Face.Serif, 22f, MuseTheme.Ink, name: "Title");
            var fonts = MuseFonts.Get(); if (fonts != null && fonts.display != null) title.font = fonts.display;
            MuseUi.Text(glass, string.IsNullOrEmpty(_record.date) ? _record.artist : _record.artist + "  ·  " + _record.date, MuseUi.Face.Sans, 14f, MuseTheme.Ink2, name: "Artist");
            var source = Join(_record.source, _record.rights);
            if (!string.IsNullOrEmpty(source)) MuseUi.Text(glass, source, MuseUi.Face.Sans, 11.5f, MuseTheme.Ink2, name: "Source");
            if (!string.IsNullOrEmpty(_record.sourceUrl))
                MuseUi.Text(glass, _record.sourceUrl.Replace("https://", "").Replace("http://", "").Replace("www.", ""), MuseUi.Face.Mono, 11f, MuseTheme.Ink2, name: "Url");
            var row = MuseUi.Row(glass, 8f, TextAnchor.MiddleLeft, "Buttons");
            var hear = MuseUi.Pill(row, "A", "Hear companions", true, () => Confirm());
            var close = MuseUi.Pill(row, "B", "Close", false, () => Redo());
            // uGUI buttons do not hear the trigger ray: each pill gets its own hit box, hover and click (Saul, 5 Oct:
            // "I had a hard time interacting with the panel").
            Canvas.ForceUpdateCanvases();
            Clickable(hear, "hear companions", () => Confirm());
            Clickable(close, "close card", () => Redo());
        }

        static void Clickable(UnityEngine.UI.Button pill, string id, System.Action act)
        {
            var rt = (RectTransform)pill.transform;
            var box = pill.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = rt.rect.center; box.size = new Vector3(rt.rect.width + 6f, rt.rect.height + 6f, 4f);
            var p = Pointable.Make(pill.gameObject, id);
            HoverTint.Bind(p, pill.targetGraphic);
            p.Selected += (_, __) => act();
        }

        /// <summary>Does another work or piece stand within a card's width of <paramref name="spot"/>?</summary>
        static bool Crowded(Vector3 spot, Transform own)
        {
            foreach (var t in InsightTarget.All)
            {
                if (t == null || t.transform == own || !t.gameObject.activeInHierarchy) continue;
                var d = t.transform.position - spot; d.y = 0f;
                if (d.magnitude < 0.75f) return true;
            }
            return false;
        }

        static string Join(string a, string b) =>
            string.IsNullOrWhiteSpace(a) ? b ?? "" : string.IsNullOrWhiteSpace(b) ? a : a + "  ·  " + b;

        /// <summary>A: the companions on this work (the masters' insight, as a tap on the work gives).</summary>
        public bool Confirm()
        {
            if (_insight != null) _insight.Speak();
            Close();
            return true;
        }

        /// <summary>B: close.</summary>
        public bool Redo() { Close(); return true; }

        public void Close()
        {
            if (_tookInput) ConfirmInput.Drop(this);
            if (Current == this) Current = null;
            _closing = true;
            if (this != null) Appear.Out(gameObject, 0.25f, destroy: true);
        }

        bool _closing;

        void Update()
        {
            if (_closing) return;
            if (_work == null) { Close(); return; }
            if (_insight != null && MasterInsights.Ensure().Discussing(_insight)) { Close(); return; }   // they took it up: the button is moot
            var eye = Camera.main != null ? Camera.main.transform : null;
            if (eye == null) return;
            var to = _work.position - eye.position;
            var flat = new Vector3(to.x, 0f, to.z);
            if (flat.magnitude > LeaveDistance) { Close(); return; }
            _away = Vector3.Angle(eye.forward, to) > 50f ? _away + Time.deltaTime : 0f;
            if (_away > LookAwaySeconds) Close();
            var pointing = false;
            foreach (var p in Pointer.All) if (p.isActiveAndEnabled && p.Hovered != null) { pointing = true; break; }
            _pointedAway = pointing ? 0f : _pointedAway + Time.deltaTime;
            if (_pointedAway > PointAwaySeconds) Close();
        }
    }
}
