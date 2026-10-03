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
        [System.NonSerialized] public float letteringHeight = 7.6f;
        [Tooltip("Cap height of the lettering, metres. Her rule: body text >= 1 degree; at 50 m that is 0.87 m.")]
        [System.NonSerialized] public float letteringCapHeight = 1.0f;
        [System.NonSerialized] public float letteringWidth = 14f;
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
            if (_undoPill != null) _undoPill.SetActive(false);
        }
        bool _promptHidden;

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

            // The four questions stand on the pool side of the walk, 2.2 m away between 18 and 43
            // degrees right - inside her +/-60 degrees, and clear of the straight view down the walk
            // to the pavilion, which is her hero image and where the question will be lettered.
            var forward = facing * Vector3.forward;
            // One flat grid on a single plane facing the visitor, so the cards line up like her option
            // stack instead of fanning (blind review, 3 Oct: each card turned to its own angle read as
            // tilted and ragged). Centred between her 18 and 43 degrees, columns 0.94 m apart.
            var gridDir = Quaternion.Euler(0f, questionsFrom + questionsStep * 0.5f, 0f) * forward;
            var gridRot = Quaternion.LookRotation(gridDir, Vector3.up);
            var gridRight = gridRot * Vector3.right;
            for (var i = 0; i < GateFlow.Samples.Count; i++)
            {
                var cell = origin + gridDir * PlateDistance + gridRight * ((i % 2 == 0 ? -1f : 1f) * 0.47f)
                         + Vector3.up * (i < 2 ? 1.3f : 0.98f);
                Plate(i, cell, gridRot);
            }

            // Her prompt, in her kit: a glass panel above the questions it is about - kicker, the
            // question it asks, how to answer (trigger, or the X pill to speak), and a level meter
            // while listening, because "recording silence" and "no microphone" look identical.
            var promptDir = Quaternion.Euler(0f, questionsFrom + questionsStep * 0.5f, 0f) * forward;
            var promptAnchor = new GameObject("Gate Prompt").transform;
            promptAnchor.SetParent(transform, false);
            promptAnchor.SetPositionAndRotation(origin + promptDir * PromptDistance + Vector3.up * 1.86f,
                                                Quaternion.LookRotation(promptDir, Vector3.up));
            _promptRoot = promptAnchor.gameObject;
            var pc = MuseUi.Canvas(promptAnchor, "Prompt", PromptDistance, 380f);
            var glass = MuseUi.Glass(pc, 380f, gap: 8f);
            MuseUi.Kicker(glass, "The gate · your question", MuseTheme.Gold);
            _promptTitle = MuseUi.Title(glass, "What question are you carrying?", 22f);
            _promptHint = MuseUi.Body(glass, "");
            var how = MuseUi.Row(glass, 10f);
            MuseUi.Pill(how, "X", "Hold to speak your own", false);
            _meter = MuseUi.Row(glass, 0f, TextAnchor.MiddleLeft, "Level").gameObject;
            var track = _meter.AddComponent<Image>();
            track.sprite = UiSprites.Rounded(4f); track.type = Image.Type.Sliced; track.color = MuseTheme.Line;
            var tle = _meter.AddComponent<LayoutElement>(); tle.preferredHeight = 8f; tle.minHeight = 8f;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(_meter.transform, false);
            _meterFill = (RectTransform)fill.transform;
            var fi = fill.GetComponent<Image>();
            fi.sprite = UiSprites.Rounded(4f); fi.type = Image.Type.Sliced; fi.color = MuseTheme.Rose;
            fill.AddComponent<LayoutElement>().ignoreLayout = true;
            _meterFill.anchorMin = Vector2.zero; _meterFill.anchorMax = new Vector2(0f, 1f); _meterFill.pivot = new Vector2(0f, 0.5f);
            _meterFill.offsetMin = Vector2.zero; _meterFill.offsetMax = Vector2.zero;
            _meter.SetActive(false);

            // Her undo: the B pill and its countdown, under the questions, only while a choice can be undone.
            var undoAnchor = new GameObject("Gate Undo").transform;
            undoAnchor.SetParent(transform, false);
            undoAnchor.SetPositionAndRotation(origin + promptDir * PromptDistance + Vector3.up * 0.62f,
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
            var wood = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            wood.SetTexture("_BaseMap", DoorPanel());
            wood.SetColor("_BaseColor", Color.white);
            foreach (var side in new[] { -1f, 1f })
            {
                var hinge = new GameObject(side < 0 ? "Gate Door Hinge L" : "Gate Door Hinge R").transform;
                hinge.SetParent(transform, false);
                hinge.SetPositionAndRotation(_doorway + right * (side * width / 2f) + Vector3.up * 0.02f, awayFromViewer);
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leaf.name = "Leaf";
                Destroy(leaf.GetComponent<Collider>());
                leaf.transform.SetParent(hinge, false);
                // Each leaf reaches from its hinge to the centre. Measured, not reasoned: with the
                // opposite sign both leaves stood outside the doorway, beside it (3 Oct 2026).
                leaf.transform.localPosition = new Vector3(side * width / 4f, height / 2f, 0f);
                leaf.transform.localScale = new Vector3(width / 2f, height, thick);
                leaf.GetComponent<Renderer>().sharedMaterial = wood;
                if (side < 0) _leafL = hinge; else _leafR = hinge;
            }
        }

        /// <summary>Dark wood with a raised panel and a lighter rail - reads as a door at 50 m.</summary>
        static Texture2D DoorPanel()
        {
            const int w = 32, h = 96;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "gate-door" };
            var dark = new Color(0.24f, 0.15f, 0.09f);
            var mid = new Color(0.36f, 0.23f, 0.13f);
            var px = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                bool rail = x < 3 || x >= w - 3 || y < 4 || y >= h - 4 || (y > 44 && y < 50);
                float grain = 0.92f + 0.08f * Mathf.Sin(x * 1.7f + y * 0.05f);
                px[y * w + x] = (rail ? mid : dark) * grain;
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
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
            Flow.ChooseSample(index);
        }

        void OnDictated(string text)
        {
            _listening = false;
            if (!Flow.SetSpoken(text)) RefreshPrompt("Nothing heard. Hold X and speak again.");
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
                _ => "What question are you carrying?",
            };
            string hint = Flow.Current switch
            {
                GateFlow.Phase.Asking => "Point at one and pull the trigger, or hold X and say your own.",
                GateFlow.Phase.Chosen => "Point at another to change it, or hold X to say it differently.",
                GateFlow.Phase.DoorsOpen => "Walk through when you are ready.",
                _ => string.Empty,
            };
            _promptHint.text = note ?? hint;
            if (_promptRoot != null) _promptRoot.SetActive(!_promptHidden && Flow.Current != GateFlow.Phase.Entered);
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
