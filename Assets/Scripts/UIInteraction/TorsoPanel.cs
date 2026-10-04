using MuseXR.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MuseXR.UI
{
    /// <summary>
    /// The visitor's waist panel (Saul, 3 Oct): one panel instead of a subtitle over each master's head.
    /// It hangs off the body (<see cref="BodyFrame"/>), not the head - always in front of the torso, at
    /// waist height and tilted up to the eye, so it never blocks the view and does not swim with a glance.
    /// From the top: whoever is speaking, in her web dialogue card (.art-dialogue: paper, the master's
    /// round portrait, caps name, the line in Gilda); below it, at the waist, her web tour guide
    /// (.tour-guide: a dark pill, a teal arrow, "WALK TO STOP n / N", the target, its distance) pointing at
    /// the next thing to interact with (<see cref="CompassTarget"/>). Instructions stand above eye level
    /// elsewhere, so nothing sits in the straight-ahead view.
    /// </summary>
    public sealed class TorsoPanel : MonoBehaviour
    {
        // Distances from the eye, metres: the line card a little higher and further than the compass.
        public const float LineAhead = 0.5f, LineDrop = 0.5f, CompassAhead = 0.42f, CompassDrop = 0.74f;
        public const float TiltDegrees = 48f;   // the panel's face turned up toward the eye

        // Her web dialogue card.
        static readonly Color Paper = new Color32(255, 252, 245, 230);
        static readonly Color PaperEdge = new Color32(73, 58, 45, 46);
        static readonly Color NameInk = new Color32(88, 72, 59, 255);
        static readonly Color LineInk = new Color32(52, 45, 40, 255);
        static readonly Color KickerInk = new Color32(120, 122, 106, 255);
        static readonly Color PortraitRing = new Color32(208, 200, 187, 255);
        // Her web tour guide.
        static readonly Color Pill = new Color32(9, 8, 11, 133);
        static readonly Color Teal = new Color32(145, 186, 177, 255);
        static readonly Color Cream = new Color32(255, 250, 241, 255);
        static readonly Color Cream2 = new Color32(255, 250, 241, 173);

        Transform _lineAnchor, _compassAnchor, _arrow;
        GameObject _lineCard, _compassCard;
        TextMeshProUGUI _kicker, _speaker, _line, _hint, _stop, _target, _detail;
        RawImage _portrait;
        TMP_FontAsset _gilda;

        static TorsoPanel _instance;

        public static TorsoPanel Get()
        {
            if (_instance != null) return _instance;
            var body = BodyFrame.Get();
            if (body == null) return null;
            var go = new GameObject("Torso Panel");
            _instance = go.AddComponent<TorsoPanel>();
            _instance.Build();
            return _instance;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        /// <summary>Show a master's line: their portrait, name and what they say. <paramref name="hint"/>
        /// is the small "A next" note, or null.</summary>
        public void ShowLine(string masterId, string speaker, string line, string hint = null, string kicker = "Your companion answers")
        {
            _kicker.text = kicker ?? string.Empty;
            _speaker.text = speaker ?? string.Empty;
            _line.text = line ?? string.Empty;
            _hint.text = hint ?? string.Empty;
            _hint.gameObject.SetActive(!string.IsNullOrEmpty(hint));
            var tex = Portrait(masterId);
            _portrait.texture = tex;
            _portrait.transform.parent.gameObject.SetActive(tex != null);
            _lineCard.SetActive(true);
        }

        public void ClearLine() { if (_lineCard != null) _lineCard.SetActive(false); }

        static Texture2D Portrait(string masterId)
        {
            var slug = masterId switch
            {
                "monet" => "claude-monet", "van_gogh" => "vincent-van-gogh", "socrates" => "socrates",
                "frida_kahlo" => "frida-kahlo", "hilma_af_klint" => "hilma-af-klint", "berthe_morisot" => "berthe-morisot",
                "picasso" => "pablo-picasso", _ => null,
            };
            return slug != null ? Resources.Load<Texture2D>("Portraits/portrait-" + slug) : null;
        }

        TextMeshProUGUI Serif(Transform parent, string text, float px, Color colour, string name)
        {
            var t = MuseUi.Text(parent, text, MuseUi.Face.Serif, px, colour, lineHeight: 1.35f, name: name);
            if (_gilda != null) t.font = _gilda;
            return t;
        }

        void Build()
        {
            var fonts = MuseFonts.Get();
            _gilda = fonts != null ? fonts.display : null;
            // Her dialogue card.
            _lineAnchor = new GameObject("Line").transform; _lineAnchor.SetParent(transform, false);
            var lc = MuseUi.Canvas(_lineAnchor, "Line Canvas", 1.15f, 560f);   // sized as if 1.15 m away: legible at a glance down
            var card = MuseUi.Card(lc, Paper, 0f, PaperEdge, 1f, padX: 18f, padY: 14f, gap: 8f, name: "Line Card");
            var head = MuseUi.Row(card, 12f, TextAnchor.MiddleLeft, "Head");
            var ring = MuseUi.Card(head, PortraitRing, 26f, null, 0f, padX: 2f, padY: 2f, name: "Portrait");
            var rle = ring.gameObject.AddComponent<LayoutElement>(); rle.preferredWidth = rle.preferredHeight = rle.minWidth = rle.minHeight = 52f; rle.flexibleWidth = 0f;
            ring.GetComponent<VerticalLayoutGroup>().childControlHeight = true;
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(LayoutElement));
            mask.transform.SetParent(ring, false);
            var mi = mask.GetComponent<Image>(); mi.sprite = UiSprites.Disc(); mask.GetComponent<Mask>().showMaskGraphic = false;
            mask.GetComponent<LayoutElement>().preferredHeight = 48f;
            var pic = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            pic.transform.SetParent(mask.transform, false);
            var prt = (RectTransform)pic.transform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
            _portrait = pic.GetComponent<RawImage>();
            _portrait.uvRect = new Rect(0.1f, 0.25f, 0.8f, 0.6f);   // the face, not the whole sheet
            var names = MuseUi.Column(head, 2f, "Names");
            names.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _kicker = MuseUi.Text(names, "", MuseUi.Face.Sans, 7.5f, KickerInk, 0.16f, true, name: "Kicker");
            _speaker = MuseUi.Text(names, "", MuseUi.Face.SansSemi, 12f, NameInk, 0.1f, true, name: "Speaker");
            _hint = MuseUi.Text(head, "", MuseUi.Face.Sans, 9f, NameInk, 0.12f, true, name: "Hint");
            _hint.enableWordWrapping = false;
            _line = Serif(card, "", 17f, LineInk, "Words");
            _lineCard = card.gameObject;
            _lineCard.SetActive(false);

            // Her tour guide.
            _compassAnchor = new GameObject("Compass").transform; _compassAnchor.SetParent(transform, false);
            var cc = MuseUi.Canvas(_compassAnchor, "Compass Canvas", 1.15f, 360f);
            var cardC = MuseUi.Card(cc, Pill, 34f, null, 0f, padX: 18f, padY: 11f, gap: 0f, name: "Tour Guide");
            var row = MuseUi.Row(cardC, 14f, TextAnchor.MiddleLeft, "Row");
            var holder = new GameObject("Arrow Holder", typeof(RectTransform), typeof(LayoutElement));
            holder.transform.SetParent(row, false);
            var hle = holder.GetComponent<LayoutElement>(); hle.preferredWidth = hle.minWidth = 22f; hle.preferredHeight = 22f; hle.flexibleWidth = 0f;
            var arrow = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrow.transform.SetParent(holder.transform, false);
            var ai = arrow.GetComponent<Image>(); ai.sprite = ArrowSprite(); ai.color = Teal;
            var art = (RectTransform)arrow.transform; art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.sizeDelta = new Vector2(22f, 22f);
            _arrow = arrow.transform;
            var copy = MuseUi.Column(row, 1f, "Copy");
            copy.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _stop = MuseUi.Text(copy, "", MuseUi.Face.Mono, 8f, Teal, 0.22f, true, name: "Stop");
            _target = Serif(copy, "", 17f, Cream, "Target");
            _target.enableWordWrapping = false;
            _detail = MuseUi.Text(copy, "", MuseUi.Face.Mono, 8f, Cream2, 0.16f, false, name: "Detail");
            _compassCard = cardC.gameObject;
        }

        void LateUpdate()
        {
            var body = BodyFrame.Get();
            if (body == null || body.Head == null) return;
            body.Step(Time.deltaTime);
            var eye = body.Head.position;
            var fwd = body.Forward;
            var tilt = Quaternion.LookRotation(Quaternion.AngleAxis(TiltDegrees, body.Right) * fwd, Vector3.up);
            _lineAnchor.SetPositionAndRotation(eye + fwd * LineAhead - Vector3.up * LineDrop, tilt);
            _compassAnchor.SetPositionAndRotation(eye + fwd * CompassAhead - Vector3.up * CompassDrop, tilt);

            var target = CompassTarget.Current(body.Feet);
            _compassCard.SetActive(target != null);
            if (target == null) return;
            var to = target.transform.position - body.Feet; to.y = 0f;
            var angle = Vector3.SignedAngle(fwd, to, Vector3.up);   // + is to the right
            _arrow.localRotation = Quaternion.Euler(0f, 0f, -angle);
            CompassTarget.Progress(target, out var stop, out var stops);
            _stop.text = "Walk to stop " + stop + " / " + stops;
            _target.text = target.label;
            var metres = Mathf.RoundToInt(to.magnitude) + " M";
            _detail.text = string.IsNullOrEmpty(target.detail) ? metres : metres + "  ·  " + target.detail;
        }

        static Sprite _arrowSprite;

        /// <summary>Her tour arrow, an up-pointing arrow (shaft and head): generated, so there is no asset to ship.</summary>
        static Sprite ArrowSprite()
        {
            if (_arrowSprite != null) return _arrowSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "compass-arrow", hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n;   // v 0 bottom .. 1 top
                float du = Mathf.Abs(u);
                // Head: two strokes from the tip down to either side; shaft: a stroke down the middle.
                bool shaft = du < 0.045f && v > 0.08f && v < 0.9f;
                float headLine = 0.92f - du * 1.15f;   // the head's outer edge
                bool head = du < 0.36f && v < headLine && v > headLine - 0.12f;
                px[y * n + x] = shaft || head ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            tex.SetPixels32(px); tex.Apply();
            _arrowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            _arrowSprite.hideFlags = HideFlags.DontSave;
            return _arrowSprite;
        }
    }
}
