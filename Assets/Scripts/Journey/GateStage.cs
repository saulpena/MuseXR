using System;
using MusePico.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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
        [System.NonSerialized] public float letteringHeight = 9.6f;
        [Tooltip("Cap height of the lettering, metres. Her rule: body text >= 1 degree; at 50 m that is 0.87 m.")]
        [System.NonSerialized] public float letteringCapHeight = 1.5f;
        [System.NonSerialized] public float letteringWidth = 16f;
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

        /// <summary>Raised once, when the visitor walks through the open doors.</summary>
        public event Action<string> Entered;

        // TMP world fontSize is neither metres nor points: measured, 0.6 renders ~16 mm of cap height.
        const float FontSizePerMetreOfCap = 0.6f / 0.016f;

        InputAction _talk, _undo;
        TextMeshPro _lettering, _prompt, _undoBar;
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

            // An exhibition title needs a ground: a pale stone plaque with a thin gilt edge, sized to
            // the words when they are set, so the lettering never sits on vine and finial.
            _plaqueGilt = Panel("Gate Plaque Gilt", _lettering.transform.position + toArch * 0.06f, awayFromViewer,
                                new Color(0.72f, 0.58f, 0.3f, 0f), out _plaqueGiltT);
            _plaque = Panel("Gate Plaque", _lettering.transform.position + toArch * 0.03f, awayFromViewer,
                            new Color(0.95f, 0.92f, 0.85f, 0f), out _plaqueT);

            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Gate Doorway Glow";
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(transform, false);
            glow.transform.SetPositionAndRotation(_doorway + Vector3.up * 2.4f - toArch * 0.5f, awayFromViewer);
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
            for (var i = 0; i < GateFlow.Samples.Count; i++)
            {
                // A 2 x 2 grid: 0.74 m plates at 2.2 m span ~19 degrees, 25 with a gap, and four in a
                // row would reach 93 degrees - past her +/-60. Two columns (18 and 43) stay inside it.
                float a = questionsFrom + (i % 2) * questionsStep;
                float y = i < 2 ? 1.36f : 0.96f;
                var dir = Quaternion.Euler(0f, a, 0f) * forward;
                var pos = origin + dir * 2.2f + Vector3.up * y;
                Plate(i, pos, Quaternion.LookRotation(dir, Vector3.up));
            }

            // Her explicit prompt: icon, text and the controller letter together, on milk glass,
            // above the questions it is about.
            var promptDir = Quaternion.Euler(0f, questionsFrom + questionsStep * 0.5f, 0f) * forward;
            var promptRot = Quaternion.LookRotation(promptDir, Vector3.up);
            var promptPos = origin + promptDir * 2.3f + Vector3.up * 1.78f;
            Backing("Gate Prompt Glass", promptPos + promptDir * 0.01f, promptRot, new Vector2(1.5f, 0.4f));
            _prompt = Text("Gate Prompt", promptPos, promptRot, 1.4f, 0.38f, 0.05f, titleFont, letteringInk);
            var undoPos = origin + promptDir * 2.3f + Vector3.up * 0.66f;
            _undoBar = Text("Gate Undo", undoPos, promptRot, 1.2f, 0.12f, 0.035f, null, letteringInk);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.volume = 0.5f;
            _chime = Chime();
            RefreshPrompt();
        }

        void Plate(int index, Vector3 pos, Quaternion awayFromViewer)
        {
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Gate Question " + index;
            plate.transform.SetParent(transform, false);
            plate.transform.SetPositionAndRotation(pos, awayFromViewer);
            plate.transform.localScale = new Vector3(0.74f, 0.34f, 1f);
            Destroy(plate.GetComponent<Collider>());
            var box = plate.AddComponent<BoxCollider>();   // not a trigger: XRI drops triggers
            box.size = new Vector3(1f, 1f, 0.05f);

            // Her milk glass: pale, mostly opaque, warm.
            plate.GetComponent<Renderer>().sharedMaterial = MilkGlass(0.82f);

            // Her rule: body text >= 1 degree. At 2.2 m that is a 3.8 cm cap; 4.2 cm here.
            var t = Text("Text", Vector3.zero, Quaternion.identity, 0.68f, 0.3f, 0.042f, titleFont, letteringInk);
            t.transform.SetParent(plate.transform, false);
            t.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            t.transform.localScale = new Vector3(1f / 0.74f, 1f / 0.34f, 1f);
            t.text = GateFlow.Samples[index];

            _plates.Add((plate.GetComponent<Renderer>().sharedMaterial, t));

            var interactable = plate.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(box);
            interactable.selectEntered.AddListener(_ => Choose(index));
        }

        readonly System.Collections.Generic.List<(Material glass, TextMeshPro text)> _plates =
            new System.Collections.Generic.List<(Material, TextMeshPro)>();
        Material _plaque, _plaqueGilt;
        Transform _plaqueT, _plaqueGiltT;

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
            if (!Flow.SetSpoken(text)) RefreshPrompt("Nothing heard. Hold X and speak again.");
        }

        void OnPhase(GateFlow.Phase phase)
        {
            if (phase == GateFlow.Phase.Chosen)
            {
                _lettering.text = GateFlow.Lettering(Flow.Question);
                // Size the plaque to the words: the set text's width plus a margin.
                var size = _lettering.GetPreferredValues(_lettering.text, letteringWidth, 0f);
                var plaque = new Vector3(Mathf.Min(size.x, letteringWidth) + 1.4f, size.y + 0.9f, 1f);
                _plaqueT.localScale = plaque;
                _plaqueGiltT.localScale = plaque + new Vector3(0.24f, 0.24f, 0f);
                _letteringAlpha = 0f;
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
                else { dialogue.ListenForText(); RefreshPrompt("Listening… release X to send."); }
            }
            if (_talk.WasReleasedThisFrame() && dialogue != null) dialogue.FinishListening();
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
            if (Flow.Current == GateFlow.Phase.Chosen) _undoBar.text = $"<b>B</b>  undo · {Mathf.CeilToInt(Flow.UndoLeft)} s";
            else _undoBar.text = string.Empty;

            // Feedback eases in: the lettering over a second, the doorway glow toward "lit".
            float targetAlpha = Flow.Current == GateFlow.Phase.Asking ? 0f : 1f;
            _letteringAlpha = Mathf.MoveTowards(_letteringAlpha, targetAlpha, Time.deltaTime);
            _lettering.alpha = _letteringAlpha;
            _plaque.SetColor("_BaseColor", new Color(0.95f, 0.92f, 0.85f, 0.94f * _letteringAlpha));
            _plaqueGilt.SetColor("_BaseColor", new Color(0.72f, 0.58f, 0.3f, 0.96f * _letteringAlpha));

            // The chosen question warms; the others step back; once the doors open the walk clears.
            for (var i = 0; i < _plates.Count; i++)
            {
                bool chosen = Flow.Current != GateFlow.Phase.Asking && GateFlow.Samples[i] == Flow.Question;
                float a = Flow.Current == GateFlow.Phase.Asking ? 1f
                        : Flow.Current == GateFlow.Phase.Chosen ? (chosen ? 1f : 0.3f)
                        : 0f;
                var (glass, text) = _plates[i];
                var c = chosen ? new Color(0.99f, 0.9f, 0.68f) : new Color(0.97f, 0.95f, 0.91f);
                var cur = glass.GetColor("_BaseColor");
                glass.SetColor("_BaseColor", new Color(c.r, c.g, c.b, Mathf.MoveTowards(cur.a, 0.82f * a, Time.deltaTime * 2f)));
                text.alpha = Mathf.MoveTowards(text.alpha, a, Time.deltaTime * 2f);
            }

            // The doorway: unlit while asking, warming when a question is chosen, full once open.
            float targetGlow = Flow.Current == GateFlow.Phase.Asking ? 0f : Flow.Current == GateFlow.Phase.Chosen ? 0.45f : 1f;
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
            if (_prompt == null) return;
            string body = Flow.Current switch
            {
                GateFlow.Phase.Asking => "What question are you carrying?\n<size=75%>Point at one and pull the trigger — or hold <b>[X]</b> and speak your own</size>",
                GateFlow.Phase.Chosen => "<size=75%>Point at another to change it — or hold <b>[X]</b> to say it differently</size>",
                GateFlow.Phase.DoorsOpen => "The doors are open",
                _ => string.Empty,
            };
            _prompt.text = note != null ? "<size=75%>" + note + "</size>" : body;
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
