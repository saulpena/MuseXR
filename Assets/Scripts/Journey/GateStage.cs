using System;
using MusePico.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using MuseXR.UI;
using UnityEngine.UI;

namespace MusePico.Journey
{
    /// <summary>
    /// Skylar's Gate, in the world (stage 1 of her VR plan; logic in <see cref="GateFlow"/>).
    ///
    /// The visitor stands at the head of the pool-side walk of grand-conservatory-with-lush-gardens,
    /// looking down it to the domed pavilion - her hero image. Four sample questions stand on the
    /// walk as pointable plates; holding X records a question of their own (release sends it). The
    /// chosen question is lettered above the pavilion's arch like an exhibition title, the doorway
    /// glow brightens, a chime plays and the controller buzzes. B undoes for 3 s; after that the
    /// doors are open and walking (teleporting) into the doorway ends the stage.
    ///
    /// Placement is measured, not guessed: from the world's playtested spawn, the doorway is 3
    /// degrees left of the spawn's facing, the wall above the arch 50.6 m away at 7-9 m high
    /// (collider rays, 3 Oct 2026). Everything is built relative to <see cref="spawn"/>.
    /// </summary>
    public sealed class GateStage : MonoBehaviour
    {
        // Tuning values are [NonSerialized] on purpose: a serialized public field is saved into the
        // scene and the scene's copy silently beats the code (the MuseumDialogue.dialogueModel trap).
        [Header("Where")]
        [Tooltip("The visitor's spawn on the walk, at floor height, facing down the walk.")]
        public Transform spawn;
        [Tooltip("The visitor's eye, for 'have they reached the doors'. Defaults to the main camera.")]
        public Transform eye;
        [Tooltip("The pavilion doorway, at floor height, in world space. Measured once from the world's collider; " +
                 "it belongs to the world, not to where the visitor stands. Zero = derive from yaw offset and distance.")]
        public Vector3 doorwayWorld;
        [System.NonSerialized] public float archYawOffset = -3f;
        [System.NonSerialized] public float archDistance = 50f;
        [System.NonSerialized] public float letteringHeight = 14f;   // above the masters' name cards and clear of the company panel (Saul, 4 Oct)
        [Tooltip("Cap height of the lettering, metres. Her rule: body text >= 1 degree; at 50 m that is 0.87 m.")]
        [System.NonSerialized] public float letteringCapHeight = 1.0f;   // her original size: only the height moved (Saul, 4 Oct)
        [System.NonSerialized] public float letteringWidth = 30f;   // one line, so it stays above the name tags
        [Tooltip("Within this many metres of the doorway (horizontally) counts as walking through it.")]
        [System.NonSerialized] public float doorwayRadius = 4f;

        [Tooltip("Where the question plates start and how far apart they stand, degrees right of the walk.")]
        [System.NonSerialized] public float questionsFrom = 18f;
        [System.NonSerialized] public float questionsStep = 25f;

        [Header("Look")]
        public TMP_FontAsset titleFont;
        [Tooltip("Her title ink: dark on the pale stone, as on her threshold.")]
        public Color letteringInk = new Color(51 / 255f, 42 / 255f, 35 / 255f);
        public Color glowColour = new Color(1f, 0.82f, 0.55f);

        [Header("Speech")]
        [Tooltip("Optional. Without it hold-X says so on the prompt instead of silently doing nothing.")]
        public MuseumDialogue dialogue;

        [Header("Editor")]
        [Tooltip("F1-F4 choose a sample, X held speaks, B undoes, G walks through the doors.")]
        public bool editorKeys = true;

        public GateFlow Flow { get; } = new GateFlow();

        /// <summary>The doorway the question is lettered over and the doors close (world point on the floor).</summary>
        public Vector3 Doorway => _doorway;

        /// <summary>Clear the Gate's own prompt and undo pill once the journey has moved on: a panel still
        /// offering "Hold X to speak" after the question is answered is a dead end (headset test, 3 Oct).</summary>
        public void HidePrompt()
        {
            _promptHidden = true;
            if (_promptRoot != null) _promptRoot.SetActive(false);
            // The faded question cards still took the trigger, so pointing at a master behind them
            // re-chose the question and shut the doors (headset test). They go with the prompt.
            foreach (var a in _plateAnchors) if (a != null) a.gameObject.SetActive(false);
            if (_undoPill != null) _undoPill.SetActive(false);
        }
        bool _promptHidden;

        /// <summary>Take the question off the arch once the curation lanterns take over: at 50 m it sat
        /// behind the lantern labels and showed through the moon gate's opening.</summary>
        public void HideLettering()
        {
            if (_lettering != null) _lettering.gameObject.SetActive(false);
            if (_plaqueT != null) _plaqueT.gameObject.SetActive(false);
        }

        /// <summary>Raised once, when the visitor walks through the open doors.</summary>
        public event Action<string> Entered;

        // TMP world fontSize is neither metres nor points: measured, 0.6 renders ~16 mm of cap height.
        const float FontSizePerMetreOfCap = 0.6f / 0.016f;

        InputAction _talk, _undo;
        TextMeshPro _lettering;
        // Her UI kit (section 04): the prompt is her glass panel, the questions her numbered option cards.
        TextMeshProUGUI _promptTitle, _promptHint, _undoLabel;
        GameObject _promptRoot, _undoPill, _meter;
        RectTransform _meterFill;
        bool _listening;
        const float PlateDistance = 2.2f, PromptDistance = 2.3f;
        Material _glow;
        AudioSource _audio;
        AudioClip _chime;
        Vector3 _doorway;
        float _letteringAlpha, _glowLevel;

        void Awake()
        {
            _talk = new InputAction("gate-talk", InputActionType.Button);
            _talk.AddBinding("<XRController>{LeftHand}/primaryButton");   // X
            _undo = new InputAction("gate-undo", InputActionType.Button);
            _undo.AddBinding("<XRController>{RightHand}/secondaryButton"); // B
            if (editorKeys)
            {
                _talk.AddBinding("<Keyboard>/x");
                _undo.AddBinding("<Keyboard>/b");
            }
            _talk.Enable(); _undo.Enable();
            Flow.PhaseChanged += OnPhase;
            if (dialogue != null) dialogue.TextDictated += OnDictated;
        }

        void OnDestroy()
        {
            _talk?.Dispose(); _undo?.Dispose();
            if (dialogue != null) dialogue.TextDictated -= OnDictated;
        }

        void Start()
        {
            if (eye == null && Camera.main != null) eye = Camera.main.transform;
            Build();
        }

        void Build()
        {
            var origin = spawn != null ? spawn.position : transform.position;
            var facing = Quaternion.Euler(0f, (spawn != null ? spawn.eulerAngles.y : transform.eulerAngles.y), 0f);

            // The arch: lettering on the wall above it, a warm glow filling the doorway below it.
            var toArch = Quaternion.Euler(0f, archYawOffset, 0f) * (facing * Vector3.forward);
            _doorway = origin + toArch * archDistance;
            if (doorwayWorld != Vector3.zero)
            {
                _doorway = doorwayWorld;
                toArch = doorwayWorld - origin; toArch.y = 0f; toArch.Normalize();
            }
            var awayFromViewer = Quaternion.LookRotation(toArch, Vector3.up);   // flat things read with +Z away

            _lettering = Text("Gate Lettering", _doorway + Vector3.up * letteringHeight, awayFromViewer,
                              letteringWidth, letteringCapHeight * 2.4f, letteringCapHeight, titleFont, letteringInk);
            _lettering.enableWordWrapping = true;
            _lettering.text = string.Empty;
            // Weight: Gilda ships one weight, so thicken the face a little for a title read at 50 m.
            _lettering.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.22f);

            // A ground for the title, so it never sits on vine and finial - but soft, a wash of pale
            // stone light feathered at every edge, not a pasted placard (blind review, 3 Oct: the hard
            // gilt-edged plaque read as a label stuck on the building and hid the dome).
            _plaque = Panel("Gate Title Ground", _lettering.transform.position + toArch * 0.03f, awayFromViewer,
                            new Color(0.96f, 0.93f, 0.86f, 0f), out _plaqueT);
            _plaque.SetTexture("_BaseMap", Feathered(128, 64, 0.32f));

            // Her concept art has the doors standing open: dark wooden leaves either side of a lit
            // gallery. The capture's doorway is already open, so "the doors open" needs doors: two
            // leaves close the doorway while the visitor chooses and swing inward when it opens.
            BuildDoors(toArch, awayFromViewer);

            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Gate Doorway Glow";
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(transform, false);
            glow.transform.SetPositionAndRotation(_doorway + Vector3.up * 2.4f + toArch * 0.6f, awayFromViewer);   // behind the leaves: the shut doors hide it
            glow.transform.localScale = new Vector3(3.4f, 5.2f, 1f);
            _glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _glow.SetTexture("_BaseMap", ArchGlow());   // feathered and arch-shaped: no hard quad edge
            _glow.SetFloat("_Surface", 1f); _glow.SetFloat("_Blend", 2f);   // additive
            _glow.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _glow.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _glow.SetFloat("_ZWrite", 0f);
            _glow.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");   // the blend floats alone do not do it
            _glow.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            glow.GetComponent<Renderer>().sharedMaterial = _glow;

            // Her web app's first two screens, in her web styling (muse-infinity, localhost:4173): the
            // landing, then "What question are you carrying?" with her dark glass question panel.
            var forward = facing * Vector3.forward;
            BuildWebScreens(origin, forward);
            var promptDir = forward;
            // Her undo: the B pill and its countdown, under the questions, only while a choice can be undone.
            var undoAnchor = new GameObject("Gate Undo").transform;
            undoAnchor.SetParent(transform, false);
            undoAnchor.SetPositionAndRotation(origin + promptDir * ScreenDistance + Vector3.up * 0.72f,
                                              Quaternion.LookRotation(promptDir, Vector3.up));
            var uc = MuseUi.Canvas(undoAnchor, "Undo", PromptDistance, 200f);
            var urow = MuseUi.Row(uc, 0f, TextAnchor.MiddleCenter);
            var ub = MuseUi.Pill(urow, "B", "Undo · 3 s", false, () => { if (Flow.Undo()) Buzz(0.25f, 0.06f); });
            _undoLabel = FindLabel(ub.transform);
            _undoPill = undoAnchor.gameObject;
            _undoPill.SetActive(false);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.volume = 0.5f;
            _chime = Chime();
            RefreshPrompt();
        }

        void Plate(int index, Vector3 pos, Quaternion awayFromViewer)
        {
            // Her option card (.opt): white paper, a hairline, the number in mono, the question in sans.
            var anchor = new GameObject("Gate Question " + index).transform;
            anchor.SetParent(transform, false);
            anchor.SetPositionAndRotation(pos, awayFromViewer);
            var c = MuseUi.Canvas(anchor, "Question", PlateDistance, 230f);
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 14f, padY: 12f, name: "Option");
            var line = MuseUi.Row(card, 9f, TextAnchor.UpperLeft, "OptionRow");
            var num = MuseUi.Text(line, "0" + (index + 1), MuseUi.Face.Mono, 12f, MuseTheme.Ink3, name: "Number");
            num.enableWordWrapping = false; num.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0f;
            var txt = MuseUi.Text(line, GateFlow.Samples[index], MuseUi.Face.Sans, MuseTheme.BodyPx + 1f, MuseTheme.Ink, name: "Question");
            txt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var group = c.gameObject.AddComponent<CanvasGroup>();

            // Pointable: a collider the size of the card, measured once the layout has run.
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4]; card.GetWorldCorners(corners);
            var box = anchor.gameObject.AddComponent<BoxCollider>();   // not a trigger: XRI drops triggers
            var lo = anchor.InverseTransformPoint(corners[0]); var hi = anchor.InverseTransformPoint(corners[2]);
            box.center = (lo + hi) / 2f;
            box.size = new Vector3(Mathf.Abs(hi.x - lo.x), Mathf.Abs(hi.y - lo.y), 0.05f);

            Image edge = null;
            foreach (var img in card.GetComponentsInChildren<Image>(true)) if (img.name == "Edge") edge = img;
            _plates.Add((card.GetComponent<Image>(), group, txt));
            _plateAnchors.Add(anchor); _plateEdges.Add(edge);

            var interactable = anchor.gameObject.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(box);
            interactable.selectEntered.AddListener(_ => Choose(index));
        }

        static TextMeshProUGUI FindLabel(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true)) if (t.name == "Label") return t;
            return null;
        }

        readonly System.Collections.Generic.List<Transform> _plateAnchors = new System.Collections.Generic.List<Transform>();
        readonly System.Collections.Generic.List<Image> _plateEdges = new System.Collections.Generic.List<Image>();
        readonly System.Collections.Generic.List<(Image back, CanvasGroup group, TextMeshProUGUI text)> _plates =
            new System.Collections.Generic.List<(Image, CanvasGroup, TextMeshProUGUI)>();
        Material _plaque;
        Transform _plaqueT, _leafL, _leafR;
        float _doorOpen;

        /// <summary>Two dark wooden leaves hinged at the doorway's sides, closed across it.</summary>
        void BuildDoors(Vector3 toArch, Quaternion awayFromViewer)
        {
            const float width = 2.4f, height = 4.6f, thick = 0.12f;   // the opening measures ~2.4 m in the capture
            var right = Vector3.Cross(Vector3.up, toArch).normalized;
            foreach (var side in new[] { -1f, 1f })
            {
                var hinge = new GameObject(side < 0 ? "Gate Door Hinge L" : "Gate Door Hinge R").transform;
                hinge.SetParent(transform, false);
                hinge.SetPositionAndRotation(_doorway + right * (side * width / 2f) + Vector3.up * 0.02f, awayFromViewer);
                // Each leaf reaches from its hinge to the centre. Measured, not reasoned: with the
                // opposite sign both leaves stood outside the doorway, beside it (3 Oct 2026).
                // The generated walnut leaf (Saul, 4 Oct: no shapes made in code); its handle edge meets the other's.
                var centre = hinge.TransformPoint(new Vector3(side * width / 4f, 0f, 0f));
                // The hinge's +Z points away from the visitor; the model's front is its +Z, so it is turned to face them.
                var leaf = MuseXR.Interaction.PropModels.Spawn("door-leaf", hinge, centre, hinge.rotation * Quaternion.Euler(0f, 180f, 0f),
                                                               new Vector3(width / 2f, height, thick), uniform: false);
                if (leaf != null)
                {
                    leaf.name = "Leaf";
                    if (side > 0) { var s = leaf.transform.localScale; leaf.transform.localScale = new Vector3(-s.x, s.y, s.z); }   // a mirrored pair
                }
                if (side < 0) _leafL = hinge; else _leafR = hinge;
            }
        }

        /// <summary>A rectangle whose alpha falls to nothing at every edge.</summary>
        static Texture2D Feathered(int w, int h, float edge)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "feathered" };
            var px = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float u = Mathf.Min(x + 0.5f, w - x - 0.5f) / w, v = Mathf.Min(y + 0.5f, h - y - 0.5f) / h;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / (edge * 0.5f))) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(v / edge));
                px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }

        Material Panel(string name, Vector3 pos, Quaternion awayFromViewer, Color colour, out Transform t)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.SetPositionAndRotation(pos, awayFromViewer);
            q.transform.localScale = Vector3.zero;
            var m = MilkGlass(0f);
            m.SetColor("_BaseColor", colour);
            q.GetComponent<Renderer>().sharedMaterial = m;
            t = q.transform;
            return m;
        }

        /// <summary>A doorway-shaped glow: a rectangle with a round head, feathered at every edge.
        /// Additive, so the falloff lives in the colour.</summary>
        static Texture2D ArchGlow()
        {
            const int w = 64, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "gate-arch-glow" };
            var px = new Color[w * h];
            const float halfWidth = 0.82f;                       // of the opening, in -1..1 across
            const float headRadiusV = halfWidth * w / h * 0.5f;  // the round head's radius, in 0..1 up
            const float headStart = 1f - headRadiusV;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float v = (y + 0.5f) / h;
                float d = v < headStart ? Mathf.Abs(u) / halfWidth
                                        : new Vector2(u / halfWidth, (v - headStart) / headRadiusV).magnitude;
                float inside = Mathf.Clamp01((1f - d) / 0.35f) * Mathf.Clamp01(v / 0.08f);
                float a = inside * inside * Mathf.Lerp(1f, 0.7f, v);
                px[y * w + x] = new Color(a, a, a, a);
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }

        void Backing(string name, Vector3 pos, Quaternion awayFromViewer, Vector2 size)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.SetPositionAndRotation(pos, awayFromViewer);
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            q.GetComponent<Renderer>().sharedMaterial = MilkGlass(0.86f);
        }

        static Material MilkGlass(float alpha)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(0.97f, 0.95f, 0.91f, alpha));
            return m;
        }

        TextMeshPro Text(string name, Vector3 pos, Quaternion rot, float width, float height, float cap,
                         TMP_FontAsset font, Color ink)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.rectTransform.sizeDelta = new Vector2(width, height);
            // Measure, do not assume: the cap height per point depends on the font. Lay out an "H"
            // at a known size and scale to the cap height asked for.
            t.fontSize = 10f;
            t.text = "H";
            t.ForceMeshUpdate();
            // The glyph itself, not the line: textBounds includes line spacing and reads ~1.6x tall.
            var h = t.textInfo.characterInfo[0];
            float capAt10 = h.topLeft.y - h.bottomLeft.y;
            t.fontSize = capAt10 > 1e-5f ? 10f * cap / capAt10 : cap * FontSizePerMetreOfCap;
            t.text = string.Empty;
            t.alignment = TextAlignmentOptions.Center;
            t.color = ink;
            t.enableWordWrapping = true;
            return t;
        }

        public void Choose(int index)
        {
            if (_promptHidden) return;   // the question is settled once the journey has moved on
            if (index < 0 || index >= GateFlow.Samples.Count) return;
            EnterMuseum();
            SetDraft(GateFlow.Samples[index]);
            Flow.ChooseSample(index);
        }

        void OnDictated(string text)
        {
            _listening = false;
            if (string.IsNullOrWhiteSpace(text)) { RefreshPrompt("Nothing heard. Hold X and speak again."); return; }
            SetDraft(text.Trim());
            RefreshPrompt("Heard you. Choose who walks with you, or hold X to say it again.");
        }

        // ---- her web screens ------------------------------------------------------------------------

        // Her web palette (muse-infinity styles): cream ink, lavender accent, dark glass.
        static readonly Color WebInk = new Color32(238, 233, 223, 255);
        static readonly Color WebInk2 = new Color32(238, 233, 223, 163);   // 0.64
        static readonly Color WebInkFaint = new Color32(238, 233, 223, 92);
        static readonly Color WebLine = new Color32(238, 233, 223, 46);    // 0.18
        static readonly Color WebLineStrong = new Color32(238, 233, 223, 107);   // 0.42
        static readonly Color WebAccent = new Color32(158, 135, 170, 255);
        static readonly Color WebGlass = new Color32(8, 6, 10, 196);       // her 0.48, darkened for the headset (Saul, 4 Oct)
        static readonly Color WebDim = new Color32(8, 6, 10, 160);         // her screen dim behind the copy, darker to read
        static readonly Color WebButton = new Color32(56, 48, 61, 131);
        static readonly Color LandingInk = new Color32(44, 36, 31, 255);
        static readonly Color LandingInk2 = new Color32(72, 62, 54, 255);
        const string Placeholder = "What makes a life not wasted?";   // her updated script's sample question
        const float ScreenDistance = 2.6f;

        GameObject _landing;
        TextMeshProUGUI _draftText;
        string _draft = string.Empty;
        bool _entered;
        readonly System.Collections.Generic.List<(Image edge, TextMeshProUGUI label, string question)> _chips =
            new System.Collections.Generic.List<(Image, TextMeshProUGUI, string)>();
        readonly System.Collections.Generic.List<(RectTransform rect, System.Action action, Image edge, Color idle, Color hover)> _clickables =
            new System.Collections.Generic.List<(RectTransform, System.Action, Image, Color, Color)>();

        TextMeshProUGUI WebText(Transform parent, string text, bool serif, float px, Color colour, float trackingEm = 0f,
                                bool upper = false, float lineHeight = 1.4f, string name = "Text")
        {
            var t = MuseUi.Text(parent, text, MuseUi.Face.Sans, px, colour, trackingEm, upper, lineHeight, name);
            if (serif && titleFont != null) t.font = titleFont;
            return t;
        }

        void BuildWebScreens(Vector3 origin, Vector3 forward)
        {
            var at = origin + forward * ScreenDistance + Vector3.up * 1.55f;
            var rot = Quaternion.LookRotation(forward, Vector3.up);   // +Z away from the viewer reads

            // Landing: "A LIVING ARCHIVE BEYOND TIME", the title, her line, and the round ENTER.
            var la = new GameObject("Gate Landing").transform;
            la.SetParent(transform, false);
            la.SetPositionAndRotation(at, rot);
            _landing = la.gameObject;
            var lc = MuseUi.Canvas(la, "Landing", ScreenDistance, 760f);
            lc.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            // A soft pale ground under her dark copy: in the headset the pavilion behind it is as busy as
            // her hero image, and the title went unread over it.
            var col = MuseUi.Card(lc, new Color32(247, 242, 233, 215), 18f, null, 0f, padX: 40f, padY: 34f, gap: 10f, name: "Landing Copy");
            col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            var e = WebText(col, "A living archive beyond time", false, 10f, LandingInk2, 0.28f, true, name: "Eyebrow");
            e.alignment = TextAlignmentOptions.Center;
            var t = WebText(col, "The Impossible\nMuseum", true, 72f, LandingInk, 0f, false, 0.95f, "Title");
            t.alignment = TextAlignmentOptions.Center;
            var sub = WebText(col, "Enter a cultural memory where artists disagree\u2014and your answer becomes part of the architecture.",
                              false, 14f, LandingInk2, 0f, false, 1.5f, "Lede");
            sub.alignment = TextAlignmentOptions.Center;
            MuseUi.Space(col, 14f);
            var row = MuseUi.Row(col, 22f, TextAnchor.MiddleCenter, "Enter Row");
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childForceExpandWidth = false; rowLayout.childControlWidth = true;   // the ENTER disc keeps its round 88 px
            // Dark on the pale ground: her cream disc vanished once the panel behind it was made opaque.
            var enter = MuseUi.Card(row, new Color32(44, 36, 31, 235), 44f, null, 0f, padX: 0f, padY: 0f, name: "Enter");
            var ele = enter.gameObject.AddComponent<LayoutElement>(); ele.preferredWidth = 88f; ele.preferredHeight = 88f;
            ele.minWidth = 88f; ele.minHeight = 88f; ele.flexibleWidth = 0f;
            enter.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            enter.GetComponent<VerticalLayoutGroup>().childControlHeight = false;
            var et = WebText(enter, "Enter", false, 9f, WebInk, 0.2f, true, name: "Label");
            et.alignment = TextAlignmentOptions.Center;
            var how = WebText(row, "Point and pull the trigger to cross", false, 9f, LandingInk2, 0.2f, true, name: "How");
            how.enableWordWrapping = false;
            var enterEdge = enter.GetComponent<Image>();
            _clickables.Add((enter, EnterMuseum, enterEdge, enterEdge.color, (Color)WebAccent));

            // The question screen: her copy on the left, her dark glass question panel on the right.
            var qa = new GameObject("Gate Prompt").transform;
            qa.SetParent(transform, false);
            qa.SetPositionAndRotation(at, rot);
            _promptRoot = qa.gameObject;
            var qc = MuseUi.Canvas(qa, "Prompt", ScreenDistance, 1060f);
            var dim = MuseUi.Card(qc, WebDim, 0f, null, 0f, padX: 34f, padY: 34f, gap: 0f, name: "Screen");
            var cols = MuseUi.Row(dim, 46f, TextAnchor.MiddleLeft, "Columns");
            cols.GetComponent<HorizontalLayoutGroup>().childControlHeight = true;
            cols.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;

            var left = MuseUi.Column(cols, 14f, "Copy");
            left.gameObject.AddComponent<LayoutElement>().preferredWidth = 400f;
            WebText(left, "01 / Begin with your life", false, 10f, WebAccent, 0.28f, true, name: "Eyebrow");
            _promptTitle = WebText(left, "Which question do you bring in?", true, 64f, WebInk, 0f, false, 1.05f, "Title");
            WebText(left, "There is no correct question. The museum will use it as the curatorial thread connecting every artwork, companion and space.",
                    false, 14f, WebInk2, 0f, false, 1.5f, "Lede");

            var panel = MuseUi.Card(cols, WebGlass, 0f, WebLine, 1f, padX: 30f, padY: 30f, gap: 16f, name: "Question Panel");
            var ple = panel.gameObject.AddComponent<LayoutElement>(); ple.preferredWidth = ple.minWidth = 540f; ple.flexibleWidth = 0f;
            WebText(panel, "Your question", false, 8f, WebAccent, 0.22f, true, name: "Label");
            _draftText = WebText(panel, Placeholder, true, 40f, WebInkFaint, 0f, false, 1.15f, "Question");
            // One size whatever the question: room for three lines, so choosing a longer one never resizes the menu (Saul, 4 Oct).
            var dle = _draftText.gameObject.AddComponent<LayoutElement>();
            dle.minHeight = dle.preferredHeight = 40f * 1.15f * 3f + 6f; dle.flexibleHeight = 0f;
            dle.preferredWidth = 480f; dle.flexibleWidth = 0f;
            var chips = MuseUi.Row(panel, 8f, TextAnchor.MiddleLeft, "Chips");
            for (var k = 0; k < GateFlow.Samples.Count; k++)   // her three samples (MUSE-VR-design, 2 Oct 2026)
            {
                var q = GateFlow.Samples[k];
                var chip = MuseUi.Card(chips, new Color(0f, 0f, 0f, 0f), 0f, WebLine, 1f, padX: 10f, padY: 8f, name: "Chip");
                var cl = WebText(chip, q, false, 8.5f, WebInk2, 0f, false, 1.2f, "Label");
                cl.enableWordWrapping = false;
                var edge = EdgeOf(chip);
                _chips.Add((edge, cl, q));
                var question = q;
                _clickables.Add((chip, () => { if (!_promptHidden) SetDraft(question); }, edge, WebLine, WebAccent));
            }
            var speak = MuseUi.Row(panel, 10f, TextAnchor.MiddleLeft, "Speak");
            WebText(speak, "Hold X to speak your own", false, 9f, WebInk2, 0.16f, true, name: "Hint").enableWordWrapping = false;
            _meter = MuseUi.Row(panel, 0f, TextAnchor.MiddleLeft, "Level").gameObject;
            var track = _meter.AddComponent<Image>();
            track.sprite = UiSprites.Rounded(4f); track.type = Image.Type.Sliced; track.color = WebLine;
            var tle = _meter.AddComponent<LayoutElement>(); tle.preferredHeight = 6f; tle.minHeight = 6f;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(_meter.transform, false);
            _meterFill = (RectTransform)fill.transform;
            var fi = fill.GetComponent<Image>();
            fi.sprite = UiSprites.Rounded(4f); fi.type = Image.Type.Sliced; fi.color = WebAccent;
            fill.AddComponent<LayoutElement>().ignoreLayout = true;
            _meterFill.anchorMin = Vector2.zero; _meterFill.anchorMax = new Vector2(0f, 1f); _meterFill.pivot = new Vector2(0f, 0.5f);
            _meterFill.offsetMin = Vector2.zero; _meterFill.offsetMax = Vector2.zero;
            _meter.SetActive(false);

            var buttonRow = MuseUi.Row(panel, 0f, TextAnchor.MiddleLeft, "Button Row");
            var button = MuseUi.Card(buttonRow, WebButton, 22f, WebLineStrong, 1f, padX: 22f, padY: 14f, name: "Choose");
            button.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0f;
            WebText(button, "Choose who walks with me  \u2192", false, 10f, WebInk, 0.2f, true, name: "Label").enableWordWrapping = false;
            _clickables.Add((button, ConfirmDraft, EdgeOf(button), WebLineStrong, WebInk));
            _promptHint = WebText(panel, "", false, 10f, WebInk2, 0f, false, 1.4f, "Status");

            _promptRoot.SetActive(false);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(lc);
            LayoutRebuilder.ForceRebuildLayoutImmediate(qc);
            foreach (var c in _clickables) MakeClickable(c.rect, c.action, c.edge, c.idle, c.hover);
        }

        static Image EdgeOf(RectTransform card)
        {
            foreach (var img in card.GetComponentsInChildren<Image>(true)) if (img.name == "Edge") return img;
            return card.GetComponent<Image>();
        }

        /// <summary>A collider the size of the drawn rect and an XRI interactable: the ray's Select (the
        /// trigger) runs <paramref name="action"/>; hovering lights the edge in her accent.</summary>
        static void MakeClickable(RectTransform rect, System.Action action, Image edge, Color idle, Color hover)
        {
            var box = rect.gameObject.AddComponent<BoxCollider>();   // not a trigger: XRI drops triggers
            box.center = rect.rect.center;
            box.size = new Vector3(rect.rect.width, rect.rect.height, 12f);
            var interactable = rect.gameObject.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(box);
            interactable.selectEntered.AddListener(_ => action());
            if (edge != null)
            {
                interactable.hoverEntered.AddListener(_ => edge.color = hover);
                interactable.hoverExited.AddListener(_ => edge.color = idle);
            }
            // At a desk the mouse hand points with MuseXR's Pointer, not the XR ray: give it the same
            // control. Only without a headset, so a trigger never fires both paths.
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
            {
                var p = MuseXR.Interaction.Pointable.Make(rect.gameObject, rect.name);
                p.Selected += (_, __) => action();
                if (edge != null) { p.Hovering += _ => edge.color = hover; p.Unhovered += _ => edge.color = idle; }
            }
        }

        void EnterMuseum()
        {
            if (_entered) return;
            _entered = true;
            if (_landing != null) _landing.SetActive(false);
            Buzz(0.2f, 0.05f);
            RefreshPrompt();
        }

        void SetDraft(string question)
        {
            _draft = question ?? string.Empty;
            if (_draftText != null)
            {
                _draftText.text = _draft.Length > 0 ? _draft : Placeholder;
                _draftText.color = _draft.Length > 0 ? WebInk : WebInkFaint;
            }
            foreach (var c in _chips)
            {
                var on = c.question == _draft;
                if (c.edge != null) c.edge.color = on ? WebAccent : WebLine;
                if (c.label != null) c.label.color = on ? WebInk : WebInk2;
            }
        }

        /// <summary>Her "Choose who walks with me": the question in the field (or her placeholder) is set.</summary>
        void ConfirmDraft()
        {
            if (_promptHidden) return;
            var q = _draft.Length > 0 ? _draft : Placeholder;
            SetDraft(q);
            var index = -1;
            for (var k = 0; k < GateFlow.Samples.Count; k++) if (GateFlow.Samples[k] == q) index = k;
            if (index >= 0) Flow.ChooseSample(index); else Flow.SetSpoken(q);
            // Her undo bar exists for a choice made by pointing at a card; this button is already a
            // deliberate confirm. Waiting it out left the question screen up for 3 s with the arch's
            // lettering showing through it (headset test, 4 Oct). The doors open now.
            Flow.Tick(GateFlow.UndoSeconds + 0.01f);
            Buzz(0.3f, 0.06f);
        }

        void OnPhase(GateFlow.Phase phase)
        {
            if (phase == GateFlow.Phase.Chosen)
            {
                _lettering.text = GateFlow.Lettering(Flow.Question);
                // Size the plaque to the words: the set text's width plus a margin.
                var size = _lettering.GetPreferredValues(_lettering.text, letteringWidth, 0f);
                _plaqueT.localScale = new Vector3(Mathf.Min(size.x, letteringWidth) * 1.45f + 1.2f, size.y * 1.9f + 0.8f, 1f);
                _letteringAlpha = 0f;
                for (var i = 0; i < _plateAnchors.Count; i++)
                    if (GateFlow.Samples[i] == Flow.Question && _undoPill != null)
                    {
                        var at = _plateAnchors[i];
                        _undoPill.transform.SetPositionAndRotation(at.position - Vector3.up * 0.2f, at.rotation);
                    }
                if (_audio != null && _chime != null) _audio.PlayOneShot(_chime);
                Buzz(0.45f, 0.12f);
            }
            if (phase == GateFlow.Phase.Asking) _lettering.text = string.Empty;
            if (phase == GateFlow.Phase.Entered) Entered?.Invoke(Flow.Question);
            RefreshPrompt();
        }

        void Update()
        {
            if (_lettering == null) return;

            if (_talk.WasPressedThisFrame())
            {
                if (dialogue == null) RefreshPrompt("No microphone in this scene. Point at a question instead.");
                else { dialogue.ListenForText(); _listening = true; RefreshPrompt("Listening… release X to send."); }
            }
            if (_talk.WasReleasedThisFrame() && dialogue != null) { dialogue.FinishListening(); _listening = false; }
            if (_meter != null)
            {
                if (_meter.activeSelf != _listening) _meter.SetActive(_listening);
                if (_listening && _meterFill != null)
                {
                    float level = dialogue != null && dialogue.voice != null ? dialogue.voice.Level : 0f;
                    float w = ((RectTransform)_meter.transform).rect.width;
                    _meterFill.sizeDelta = new Vector2(w * Mathf.Clamp01(level * 4f), 0f);
                }
            }
            if (_undo.WasPressedThisFrame() && Flow.Undo()) Buzz(0.25f, 0.06f);

            if (editorKeys && Keyboard.current != null)
            {
                var k = Keyboard.current;
                if (k.f1Key.wasPressedThisFrame) Choose(0);
                if (k.f2Key.wasPressedThisFrame) Choose(1);
                if (k.f3Key.wasPressedThisFrame) Choose(2);
                if (k.f4Key.wasPressedThisFrame) Choose(3);
                if (k.gKey.wasPressedThisFrame) Flow.Tick(GateFlow.UndoSeconds);
            }

            Flow.Tick(Time.deltaTime);
            bool canUndo = Flow.Current == GateFlow.Phase.Chosen;
            if (_undoPill != null && _undoPill.activeSelf != canUndo) _undoPill.SetActive(canUndo);
            if (canUndo && _undoLabel != null) _undoLabel.text = $"Undo · {Mathf.CeilToInt(Flow.UndoLeft)} s";

            // Feedback eases in: the lettering over a second, the doorway glow toward "lit".
            float targetAlpha = Flow.Current == GateFlow.Phase.Asking ? 0f : 1f;
            _letteringAlpha = Mathf.MoveTowards(_letteringAlpha, targetAlpha, Time.deltaTime);
            _lettering.alpha = _letteringAlpha;
            _plaque.SetColor("_BaseColor", new Color(0.96f, 0.93f, 0.86f, 0.92f * _letteringAlpha));

            // The doors: shut while asking and choosing, swinging inward over 2.5 s once open.
            float open = Flow.Current == GateFlow.Phase.DoorsOpen || Flow.Current == GateFlow.Phase.Entered ? 1f : 0f;
            _doorOpen = Mathf.MoveTowards(_doorOpen, open, Time.deltaTime / 2.5f);
            float swing = Mathf.SmoothStep(0f, 1f, _doorOpen) * 100f;
            if (_leafL != null) _leafL.localRotation = Quaternion.Euler(0f, swing, 0f);
            if (_leafR != null) _leafR.localRotation = Quaternion.Euler(0f, -swing, 0f);

            // The chosen question warms; the others step back; once the doors open the walk clears.
            for (var i = 0; i < _plates.Count; i++)
            {
                bool chosen = Flow.Current != GateFlow.Phase.Asking && GateFlow.Samples[i] == Flow.Question;
                float a = Flow.Current == GateFlow.Phase.Asking ? 1f
                        : Flow.Current == GateFlow.Phase.Chosen ? (chosen ? 1f : 0f)
                        : 0f;
                var (back, group, text) = _plates[i];
                // Chosen: her gold-soft card with gold ink, never colour alone (it also goes bold); the
                // others step back, and the walk clears once the doors open.
                back.color = chosen ? MuseTheme.GoldSoft : MuseTheme.Paper;
                if (i < _plateEdges.Count && _plateEdges[i] != null) _plateEdges[i].color = chosen ? MuseTheme.Gold : MuseTheme.Line;
                text.color = chosen ? MuseTheme.GoldInk : MuseTheme.Ink;
                text.fontStyle = chosen ? FontStyles.Bold : FontStyles.Normal;
                group.alpha = Mathf.MoveTowards(group.alpha, a, Time.deltaTime * (a > group.alpha ? 2f : 4f));
            }

            // The doorway: unlit while asking, warming when a question is chosen, full once open.
            float targetGlow = Flow.Current == GateFlow.Phase.Asking ? 0.3f : Flow.Current == GateFlow.Phase.Chosen ? 0.6f : 1.4f;
            _glowLevel = Mathf.MoveTowards(_glowLevel, targetGlow, Time.deltaTime * 0.6f);
            _glow.SetColor("_BaseColor", glowColour * (_glowLevel * 0.8f));

            if (Flow.Current == GateFlow.Phase.DoorsOpen && eye != null)
            {
                var flat = eye.position - _doorway; flat.y = 0f;
                if (flat.magnitude <= doorwayRadius || (editorKeys && Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame))
                    Flow.Enter();
            }
        }

        void RefreshPrompt(string note = null)
        {
            if (_promptTitle == null) return;
            _promptTitle.text = Flow.Current switch
            {
                GateFlow.Phase.DoorsOpen => "The doors are open",
                GateFlow.Phase.Chosen => "Your question is above the doors",
                _ => "Which question do you bring in?",
            };
            string hint = Flow.Current switch
            {
                GateFlow.Phase.Asking => "Point at a question, or hold X and say your own. Then choose who walks with you.",
                GateFlow.Phase.Chosen => "B undoes it. Point at another, or hold X, to change it.",
                GateFlow.Phase.DoorsOpen => "Walk through when you are ready.",
                _ => string.Empty,
            };
            _promptHint.text = note ?? hint;
            if (_promptRoot != null) _promptRoot.SetActive(_entered && !_promptHidden && Flow.Current != GateFlow.Phase.Entered);
        }

        static void Buzz(float amplitude, float seconds)
        {
            foreach (var node in new[] { XRNode.RightHand, XRNode.LeftHand })
            {
                var device = InputDevices.GetDeviceAtXRNode(node);
                if (device.isValid) device.SendHapticImpulse(0u, amplitude, seconds);
            }
        }

        /// <summary>A soft struck chime: two partials with a bell's decay. Generated, so no asset to ship.</summary>
        static AudioClip Chime()
        {
            const int rate = 44100; const float seconds = 1.8f;
            var n = (int)(rate * seconds);
            var data = new float[n];
            for (var i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-3.2f * t) * Mathf.Clamp01(t * 200f);
                data[i] = env * (0.6f * Mathf.Sin(2f * Mathf.PI * 784f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 1568f * t * 1.002f));
            }
            var clip = AudioClip.Create("gate-chime", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
