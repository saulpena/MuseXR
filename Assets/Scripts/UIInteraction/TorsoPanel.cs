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
        // About 29 deg below the eye line for the card and 43 deg for the compass: a natural glance down
        // (15-25 deg) brings the card into view; looking ahead it stays below the view. At 45 deg the
        // card needed a deliberate stare down and sat cut off at the frame's lower edge (review, 4 Oct).
        public const float LineAhead = 0.62f, LineDrop = 0.62f, CompassAhead = 0.52f, CompassDrop = 1.0f;   // Saul, 4 Oct: half a metre lower
        /// <summary>The panel re-centres on the view direction once the head is this far off it, easing over -
        /// following the body alone (40 deg dead zone) left it at the lower left of the view.</summary>
        public const float RecentreDegrees = 20f, RecentreDegreesPerSecond = 110f;
        /// <summary>Lines shown at once; longer readings page through (live readings run ~50 words).</summary>
        public const int MaxLines = 3;
        public const float SecondsPerWord = 0.36f, MinPageSeconds = 3.5f;

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
        float _yaw;
        float _eyeHeight; int _eyeSamples;
        Vector3 _anchorXZ, _lastRig; bool _anchorSet;
        /// <summary>How far the head may move (lean, nod) before the card and compass follow.</summary>
        public const float HeadSlack = 0.3f;
        bool _yawStarted;
        float _pageTimer;
        string _hintBase;
        TMP_FontAsset _gilda;

        static TorsoPanel _instance;

        /// <summary>The panel if one exists - for clearing it, which must never build one (say, at teardown).</summary>
        public static TorsoPanel Existing => _instance;

        public static TorsoPanel Get()
        {
            if (_instance != null) return _instance;
            var body = BodyFrame.Get();
            if (body == null) return null;
            var go = new GameObject("Torso Panel");
            _instance = go.AddComponent<TorsoPanel>();
            _instance.Build();
            var satchel = Satchel.Get();
            if (satchel != null) satchel.Added += item => _instance.Note("Replicated",
                item.ReplicaName + "  \u00b7  added to your satchel");
            return _instance;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        /// <summary>Show a master's line: their portrait, name and what they say. <paramref name="hint"/>
        /// is the small "A next" note, or null.</summary>
        /// <summary>
        /// Her roundtable rule: under each turn, which of the visitor's records it cites. While set, a master's
        /// line shows "Based on: ..." in place of the usual kicker. Cleared by whoever set it.
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<string, string> BasedOn =
            new System.Collections.Generic.Dictionary<string, string>();

        public void ShowLine(string masterId, string speaker, string line, string hint = null, string kicker = "Your companion answers")
        {
            if (masterId != null && kicker == "Your companion answers" && BasedOn.TryGetValue(masterId, out var basedOn)) kicker = basedOn;
            // Saul, 5 Oct: the heading says what they are talking about - the work, the option, the question.
            else if (masterId != null && kicker == "Your companion answers" && !string.IsNullOrEmpty(MuseXR.Interaction.DialogueContext.Current))
                kicker = MuseXR.Interaction.DialogueContext.Current;
            _kicker.text = kicker ?? string.Empty;
            _speaker.text = speaker ?? string.Empty;
            _line.text = line ?? string.Empty;
            _hintBase = hint ?? string.Empty;
            _hint.text = _hintBase;
            _hint.gameObject.SetActive(true);
            _line.pageToDisplay = 1;
            _pageTimer = 0f;
            var tex = Portrait(masterId);
            // The ring stays for a system note too (empty): the card keeps one size whoever is speaking.
            _portrait.texture = tex;
            _portrait.color = tex != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            Appear.Set(_lineCard, true, CardFade);   // eased in and out, never popped (Saul, 5 Oct)
        }

        const float CardFade = 0.25f;

        public void ClearLine() { if (_lineCard != null) Appear.Set(_lineCard, false, CardFade); }

        /// <summary>A short system line on the card (no portrait), cleared after <paramref name="seconds"/>
        /// unless a master's line has taken its place.</summary>
        public void Note(string kicker, string text, float seconds = 4f)
        {
            ShowLine(null, kicker, text, null, "");
            var shown = text;
            StartCoroutine(ClearAfter(seconds, shown));
        }

        System.Collections.IEnumerator ClearAfter(float seconds, string shown)
        {
            yield return new WaitForSeconds(seconds);
            if (_line != null && _line.text == shown) ClearLine();
        }

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
            _line.alignment = TextAlignmentOptions.Center;   // Saul, 5 Oct: centred
            // Saul, 4 Oct: the whole line in one card, no pages: a fixed box, and the text shrinks to fit it.
            _line.enableAutoSizing = true; _line.fontSizeMin = 9f; _line.fontSizeMax = 17f;
            _line.overflowMode = TextOverflowModes.Truncate;
            var wle = _line.gameObject.AddComponent<LayoutElement>();
            wle.preferredHeight = wle.minHeight = 17f * 1.35f * (MaxLines + 1) + 4f;
            wle.flexibleHeight = 0f;
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
            // A fixed width: the pill must not grow and shrink with each target's name.
            var cle = copy.gameObject.AddComponent<LayoutElement>(); cle.preferredWidth = cle.minWidth = 270f; cle.flexibleWidth = 0f;
            _stop = MuseUi.Text(copy, "", MuseUi.Face.Mono, 8f, Teal, 0.22f, true, name: "Stop");
            _target = Serif(copy, "", 17f, Cream, "Target");
            _target.enableWordWrapping = false;
            _target.overflowMode = TextOverflowModes.Ellipsis;
            _detail = MuseUi.Text(copy, "", MuseUi.Face.Mono, 8f, Cream2, 0.16f, false, name: "Detail");
            _stop.overflowMode = TextOverflowModes.Ellipsis; _stop.enableWordWrapping = false;
            _detail.overflowMode = TextOverflowModes.Ellipsis; _detail.enableWordWrapping = false;
            _compassCard = cardC.gameObject;
        }

        void LateUpdate()
        {
            var body = BodyFrame.Get();
            if (body == null || body.Head == null) return;
            body.Step(Time.deltaTime);
            var eye = body.Head.position;

            // Its own yaw: along the walk while walking; otherwise it stays put until the head is more than
            // RecentreDegrees off it, then eases over. A glance moves nothing; a turn is followed smoothly.
            var hf = body.Head.forward; hf.y = 0f;
            var headYaw = hf.sqrMagnitude > 1e-6f ? Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg : _yaw;
            if (!_yawStarted) { _yaw = headYaw; _yawStarted = true; }
            float targetYaw = _yaw;
            if (body.Velocity.magnitude > BodyFrame.WalkingSpeed) targetYaw = Mathf.Atan2(body.Forward.x, body.Forward.z) * Mathf.Rad2Deg;
            else if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > RecentreDegrees) targetYaw = headYaw;
            if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > 100f) _yaw = headYaw;   // a snap turn
            _yaw = Mathf.MoveTowardsAngle(_yaw, targetYaw, RecentreDegreesPerSecond * Time.deltaTime);
            var fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

            // Each card faces the eye squarely, so nothing reads keystoned (it was tilted 25-30 deg off).
            // Saul, 4 Oct: a fixed height - looking up or down never moves them. The eye height is measured over the
            // first second and held; only turning (the yaw above) and walking move them.
            var floor = body.Feet.y;
            if (_eyeSamples < 60) { _eyeHeight = Mathf.Max(_eyeHeight, eye.y - floor); _eyeSamples++; }
            // Nor forward and back with the head: leaning or nodding within HeadSlack moves nothing; walking drags
            // the anchor along behind the head, and a teleport moves it at once (Saul, 4 Oct).
            var flatEye = new Vector3(eye.x, 0f, eye.z);
            if (!_anchorSet || (flatEye - _anchorXZ).magnitude > 2f) { _anchorXZ = flatEye; _anchorSet = true; }
            var off = flatEye - _anchorXZ;
            if (off.magnitude > HeadSlack) _anchorXZ = flatEye - off.normalized * HeadSlack;
            // Walking is the RIG moving (the stick), never the head: then keep right up, so the compass does not tip
            // under you. The head's parent (the rig's camera offset) moves only with locomotion.
            var rig = body.Head.parent != null ? body.Head.parent.position : flatEye;
            var rigStep = new Vector3(rig.x - _lastRig.x, 0f, rig.z - _lastRig.z).magnitude;
            _lastRig = rig;
            if (rigStep > 0.002f && rigStep < 1f) _anchorXZ = Vector3.MoveTowards(_anchorXZ, flatEye, rigStep + 1.2f * Time.deltaTime);
            var held = new Vector3(_anchorXZ.x, floor + (_eyeHeight > 0.5f ? _eyeHeight : eye.y - floor), _anchorXZ.z);
            var linePos = held + fwd * LineAhead - Vector3.up * LineDrop;
            var compassPos = held + fwd * CompassAhead - Vector3.up * CompassDrop;
            _lineAnchor.SetPositionAndRotation(linePos, Quaternion.LookRotation(linePos - eye, Vector3.up));
            _compassAnchor.SetPositionAndRotation(compassPos, Quaternion.LookRotation(compassPos - eye, Vector3.up));

            Page();

            var target = CompassTarget.Current(body.Feet);
            Appear.Set(_compassCard, target != null, CardFade);
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

        /// <summary>Turn the line's pages on a reading clock and show "page / pages" beside the hint.</summary>
        void Page()
        {
            if (_lineCard == null || !_lineCard.activeSelf) return;
            _hint.text = _hintBase;   // one card holds the whole line: no pages to count
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
