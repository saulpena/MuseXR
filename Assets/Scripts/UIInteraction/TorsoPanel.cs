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
    /// From the top: whoever is speaking and their line; below it, at the waist, the compass pointing at
    /// the next thing to interact with (<see cref="CompassTarget"/>). Instructions stand above eye level
    /// elsewhere, so nothing sits in the straight-ahead view.
    /// </summary>
    public sealed class TorsoPanel : MonoBehaviour
    {
        // Distances from the eye, metres: the line card a little higher and further than the compass.
        public const float LineAhead = 0.5f, LineDrop = 0.5f, CompassAhead = 0.42f, CompassDrop = 0.72f;
        public const float TiltDegrees = 48f;   // the panel's face turned up toward the eye

        static readonly Color Ink = new Color32(238, 233, 223, 255);
        static readonly Color Ink2 = new Color32(238, 233, 223, 170);
        static readonly Color Accent = new Color32(158, 135, 170, 255);
        static readonly Color Glass = new Color32(8, 6, 10, 150);
        static readonly Color Line = new Color32(238, 233, 223, 46);

        Transform _lineAnchor, _compassAnchor, _arrow;
        GameObject _lineCard, _compassCard;
        TextMeshProUGUI _speaker, _line, _hint, _target;

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

        /// <summary>Show a master's line: their name and what they say. <paramref name="hint"/> is the
        /// small "A next" style note, or null.</summary>
        public void ShowLine(string speaker, string line, string hint = null)
        {
            _speaker.text = speaker ?? string.Empty;
            _line.text = line ?? string.Empty;
            _hint.text = hint ?? string.Empty;
            _hint.gameObject.SetActive(!string.IsNullOrEmpty(hint));
            _lineCard.SetActive(true);
        }

        public void ClearLine() { if (_lineCard != null) _lineCard.SetActive(false); }

        void Build()
        {
            _lineAnchor = new GameObject("Line").transform; _lineAnchor.SetParent(transform, false);
            var lc = MuseUi.Canvas(_lineAnchor, "Line Canvas", 1.15f, 520f);   // sized as if 1.15 m away: legible at a glance down
            var card = MuseUi.Card(lc, Glass, 10f, Line, 1f, padX: 22f, padY: 16f, gap: 6f, name: "Line Card");
            var top = MuseUi.Row(card, 10f, TextAnchor.MiddleLeft, "Top");
            _speaker = MuseUi.Text(top, "", MuseUi.Face.SansSemi, 10f, Accent, 0.2f, true, name: "Speaker");
            _speaker.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _hint = MuseUi.Text(top, "", MuseUi.Face.Sans, 9f, Ink2, 0.12f, true, name: "Hint");
            _hint.enableWordWrapping = false;
            _line = MuseUi.Text(card, "", MuseUi.Face.Sans, 15f, Ink, lineHeight: 1.4f, name: "Words");
            _lineCard = card.gameObject;
            _lineCard.SetActive(false);

            _compassAnchor = new GameObject("Compass").transform; _compassAnchor.SetParent(transform, false);
            var cc = MuseUi.Canvas(_compassAnchor, "Compass Canvas", 1.1f, 300f);
            var cardC = MuseUi.Card(cc, Glass, 26f, Line, 1f, padX: 14f, padY: 10f, gap: 0f, name: "Compass Card");
            var row = MuseUi.Row(cardC, 12f, TextAnchor.MiddleLeft, "Row");
            var dial = new GameObject("Dial", typeof(RectTransform), typeof(Image));
            dial.transform.SetParent(row, false);
            var di = dial.GetComponent<Image>(); di.sprite = UiSprites.Ring(0.08f); di.color = Line;
            var dle = dial.AddComponent<LayoutElement>(); dle.preferredWidth = 44f; dle.preferredHeight = 44f; dle.flexibleWidth = 0f;
            var arrow = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrow.transform.SetParent(dial.transform, false);
            var ai = arrow.GetComponent<Image>(); ai.sprite = ArrowSprite(); ai.color = Accent;
            var art = (RectTransform)arrow.transform; art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.sizeDelta = new Vector2(30f, 30f);
            _arrow = arrow.transform;
            _target = MuseUi.Text(row, "", MuseUi.Face.Sans, 11f, Ink, name: "Target");
            _target.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
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
            _target.text = target.label + "   <color=#EEE9DFAA>" + Mathf.RoundToInt(to.magnitude) + " m</color>";
        }

        static Sprite _arrowSprite;

        /// <summary>An arrowhead pointing up the sprite (+Y): generated, so there is no asset to ship.</summary>
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
                // A chevron: inside the triangle (tip at the top), outside a notch cut from the base.
                bool tri = v < 0.95f && Mathf.Abs(u) < (0.95f - v) * 0.42f;
                bool notch = v < 0.45f && Mathf.Abs(u) < (0.45f - v) * 0.42f;
                px[y * n + x] = tri && !notch ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            tex.SetPixels32(px); tex.Apply();
            _arrowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            _arrowSprite.hideFlags = HideFlags.DontSave;
            return _arrowSprite;
        }
    }
}
