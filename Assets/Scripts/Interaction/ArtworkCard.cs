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
        static readonly Color AiPurple = new Color32(0x7a, 0x5c, 0xb8, 0xff);

        public static ArtworkCard Current { get; private set; }

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

        public static ArtworkCard Show(ArtworkRecord record, Transform work, Vector2 workSize, InsightTarget insight)
        {
            if (Hushed) return null;
            if (Current != null)
            {
                if (Current._work == work) return Current;
                Current.Close();
            }
            var go = new GameObject("Artwork Card · " + record.title);
            var card = go.AddComponent<ArtworkCard>();
            card._record = record; card._work = work; card._insight = insight;
            card.Build(workSize);
            Appear.In(go, 0.3f);   // eased, never popped (Saul, 5 Oct)
            Current = card;
            // A and B are the card's only while nothing else holds them (a chapter's own choice keeps its A).
            if (ConfirmInput.Focus == null) { ConfirmInput.Take(card); card._tookInput = true; }
            return card;
        }

        void Build(Vector2 workSize)
        {
            // The work's quad faces its -Z; "right of the frame" is right as the visitor faces the work.
            var facing = -_work.forward;
            var right = Vector3.Cross(Vector3.up, -facing).normalized;
            var o = Offset(workSize, 0.42f);
            // The side with room (Saul, 5 Oct): at the Gate the easels stand close, and the right side put the card over
            // the next painting. Right by default; left when another work stands where the card would.
            if (o.x > 0f && Crowded(_work.position + right * o.x, _work))
                o = !Crowded(_work.position - right * o.x, _work) ? new Vector3(-o.x, o.y, 0f)
                                                                   : Offset(new Vector2(0f, workSize.y), 0.42f);   // both sides taken: under it
            var at = _work.position + right * o.x + Vector3.up * o.y + facing * 0.02f;
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
            MuseUi.Text(glass, Join(_record.source, _record.rights), MuseUi.Face.Sans, 11.5f, MuseTheme.Ink2, name: "Source");
            if (!string.IsNullOrEmpty(_record.sourceUrl))
                MuseUi.Text(glass, _record.sourceUrl.Replace("https://", "").Replace("http://", "").Replace("www.", ""), MuseUi.Face.Mono, 11f, MuseTheme.Ink2, name: "Url");
            var row = MuseUi.Row(glass, 8f, TextAnchor.MiddleLeft, "Buttons");
            MuseUi.Pill(row, "A", "Hear companions", true, () => Confirm());
            MuseUi.Pill(row, "B", "Close", false, () => Redo());
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
            var eye = Camera.main != null ? Camera.main.transform : null;
            if (eye == null) return;
            var to = _work.position - eye.position;
            var flat = new Vector3(to.x, 0f, to.z);
            if (flat.magnitude > LeaveDistance) { Close(); return; }
            _away = Vector3.Angle(eye.forward, to) > 50f ? _away + Time.deltaTime : 0f;
            if (_away > LookAwaySeconds) Close();
        }
    }
}
