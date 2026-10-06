using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using MusePico.Dialogue;

namespace MusePico.Journey
{
    /// <summary>
    /// Draws a <see cref="StagePanel"/> in the world, and makes its choices pointable.
    ///
    /// <b>One text block, not a stack of rects.</b> CLAUDE.md records the cost of the alternative:
    /// a <c>RectTransform</c>'s localPosition is its CENTRE while Top alignment draws from the top
    /// edge, so stacking a title over a body means computing <c>centre = top - height/2</c>, and
    /// getting it wrong renders one through the other — which it did twice. Rich text inside a
    /// single <see cref="TMP_Text"/> cannot overlap itself.
    ///
    /// <b>Built in code, not authored in the scene.</b> Ten stages with different numbers of
    /// choices would otherwise be ten hand-made hierarchies to keep in step. The scene holds one
    /// empty object; everything below is made at runtime and torn down on the next render.
    ///
    /// Motion is <see cref="PanelAnchor"/>'s: body-locked, horizontal only, hysteresis. This class
    /// only holds the latch and applies the result.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JourneyPanel : MonoBehaviour
    {
        [Tooltip("Left empty, the main camera is used.")]
        public Transform head;

        [Tooltip("World height the visitor is standing on. The panel rides this, never the head.")]
        public float floorY;

        [Tooltip("Metres wide. The text wraps inside this.")]
        public float width = 2.1f;

        /// <summary>
        /// Rows of choices before a second column starts.
        ///
        /// <b>Measured: stage 02 has seven masters plus an action.</b> In one column that is eight
        /// rows, and the last of them lands below the floor where nobody can point at it. A grid
        /// keeps every row between eye height and knee height, which is the band a seated or
        /// standing visitor can actually reach.
        /// </summary>
        public const int MaxRows = 4;

        /// <summary>Metres between rows. Tuned so four rows plus an action clear the floor.</summary>
        public const float RowSpacing = 0.24f;

        /// <summary>Height of a picture card: the image plus the caption under it.</summary>
        public const float PortraitHeight = 0.56f;

        /// <summary>Metres of the card reserved for the caption below the image.</summary>
        public const float CaptionHeight = 0.15f;

        /// <summary>Local Y of the top of the copy block. The panel origin is eye height.</summary>
        public const float CopyTop = 1.0f;

        /// <summary>
        /// The copy block's height is rounded up to a multiple of this before anything is placed
        /// under it, so a one-word change cannot nudge every control on the panel.
        /// </summary>
        public const float CopyStep = 0.16f;

        /// <summary>Height of the question field. FIXED - that is the whole point of it.</summary>
        public const float FieldHeight = 0.40f;

        /// <summary>
        /// The copy block's rect height. This is the room the text may WRAP inside, not the space
        /// it takes — the choices are placed under what was actually rendered, measured after the
        /// fact, because the ten stages differ by several lines.
        /// </summary>
        public const float CopyHeight = 1.9f;

        /// <summary>
        /// Resolves a <see cref="StageChoice.ImageId"/> to a picture. Set by the runner, which holds
        /// the serialized portraits — a player build has no AssetDatabase to look them up with.
        /// </summary>
        public Func<string, Texture2D> ImageFor;

        /// <summary>Raised when the visitor picks a choice; the argument is <see cref="StageChoice.Id"/>.</summary>
        public event Action<string> ChoiceTaken;

        /// <summary>Raised when the visitor takes the stage's forward action.</summary>
        public event Action ActionTaken;

        /// <summary>Raised when the visitor steps back a stage.</summary>
        public event Action BackTaken;

        /// <summary>
        /// Raised when the visitor picks a room from the navigator; the argument is the room index.
        /// Her <c>[data-scene-index]</c> and <c>[data-scene-direction]</c>, which both resolve to
        /// an absolute index in <c>goToExhibitionScene</c>.
        /// </summary>
        public event Action<int> NavTaken;

        /// <summary>True while the microphone is open. Drawn, because otherwise nothing says so.</summary>
        public bool listening;

        /// <summary>0..1 of what the microphone is hearing, for the meter.</summary>
        public float micLevel;

        TMP_Text _text;
        Transform _plates;
        Transform _scrim;
        Transform _aside;
        Transform _field;
        Transform _mic;
        Transform _micFill;
        TMP_Text _micText;
        Transform _compass;
        Transform _arrow;
        TMP_Text _compassText;
        TourStop _shownStop;
        float _yaw;
        bool _following;

        /// <summary>
        /// Degrees the panel stands off the visitor's gaze, positive to the right. 0 is straight
        /// ahead, as every stage has it. In stage 04 with doors it is set to one side: the panel
        /// follows the gaze, so straight ahead it stood in front of every door the visitor turned to.
        /// </summary>
        public float yawOffset;

        /// <summary>Where the panel is anchored on the ground plane. NOT the head's position.</summary>
        Vector2 _ground;
        bool _walking;

        /// <summary>The floor height actually in use, deadbanded against the rig's micro-bounce.</summary>
        float _heldFloor;
        bool _seated;
        StagePanel _panel;

        /// <summary>The choice ids currently on offer, in the order they are drawn.</summary>
        public IReadOnlyList<string> ChoiceIds { get; private set; } = new List<string>();

        void Awake()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            BuildScrim();                  // behind the text, so it is built first
            BuildText();
            _aside = new GameObject("Aside").transform;
            _aside.SetParent(transform, false);
            _field = new GameObject("Field").transform;
            _field.SetParent(transform, false);
            _plates = new GameObject("Choices").transform;
            _plates.SetParent(transform, false);
            BuildMic();
            BuildFps();
            if (head != null) _yaw = head.eulerAngles.y;
        }

        /// <summary>
        /// Click anything pointable with the mouse, whenever there is no headset.
        ///
        /// Her build says "CLICK TO CROSS" and that is exactly what this restores: without a
        /// headset the XR ray interactors have no controller to ride on, so plates, masters and
        /// artworks are unpointable. With a headset (device or Link) the ray is the only way in.
        ///
        /// The nearest <see cref="DesktopPointable"/> wins, but a solid collider in front of it
        /// (a wall, a statue) blocks the click, so nothing is clicked through the world. Triggers
        /// are passed through unless pointable — a master's ask target is a trigger.
        /// </summary>
        void DesktopMousePick()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            var hits = Physics.RaycastAll(ray, 60f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                var pointable = hit.collider.GetComponentInParent<DesktopPointable>();
                if (pointable != null) { pointable.Pick(); return; }
                if (!hit.collider.isTrigger) return;
            }
        }

        /// <summary>
        /// The ray and the mouse, from one call: anything pointable gets both, running the same
        /// callback, so the two input paths cannot drift apart.
        /// </summary>
        XRSimpleInteractable Pointable(GameObject go, Action onPick)
        {
            // A plate that has only just appeared ignores a press for a moment. One trigger pull on a
            // master opened the ask form and then landed on the plate that appeared under the ray —
            // "OR ASK: …the Great Buddha?" — so a click on a master "talked about the Buddha"
            // (Saul, Quest, 27 Sep). Every accepted press is logged with what it hit.
            Action guarded = () =>
            {
                if (Time.unscaledTime - _builtAt < FreshPlateGuard)
                {
                    Debug.Log("[Pick] ignored " + go.name + " (appeared " +
                              ((Time.unscaledTime - _builtAt) * 1000f).ToString("0") + " ms ago)");
                    return;
                }
                Debug.Log("[Pick] panel: " + go.name);
                onPick();
            };
            var interactable = go.AddComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(_ => guarded());
            go.AddComponent<DesktopPointable>().Picked = guarded;
            return interactable;
        }

        /// <summary>How long a freshly built plate refuses presses.</summary>
        const float FreshPlateGuard = 0.5f;

        /// <summary>When the panel last changed screen.</summary>
        float _builtAt = -10f;

        string _guardHeading, _guardMarker;

        void LateUpdate()
        {
            DesktopMousePick();
            DrawMic();
            DrawFps();

            if (head == null) return;
            if (!_seated) Seat();
            DrawCompassPose();

            // Snap, do not ease, while the head pose is still settling: when the scene starts and
            // just after a stage opens. XR tracking arrives a few frames after the first one, so a
            // panel seated on frame one then slid across the room to wherever the head really was.
            if (Time.unscaledTime < _snapUntil) { Snap(); return; }

            _yaw = PanelAnchor.Follow(_yaw, head.eulerAngles.y, _following, Time.deltaTime, out _following);
            _ground = PanelAnchor.FollowGround(
                _ground, new Vector2(head.position.x, head.position.z), _walking, Time.deltaTime, out _walking);
            _heldFloor = PanelAnchor.FollowFloor(_heldFloor, floorY);

            transform.SetPositionAndRotation(
                PanelAnchor.Position(new Vector3(_ground.x, 0f, _ground.y), _yaw + yawOffset, _heldFloor) + Vector3.up * _lift,
                PanelAnchor.Rotation(_yaw + yawOffset));
        }

        /// <summary>
        /// The microphone indicator: a word and a level bar, under the panel.
        ///
        /// <b>"Recording silence" and "no microphone" are identical from inside the app</b> - the
        /// device opens, reports a name and returns zeros either way. Only a live level separates
        /// them, which is why VoiceCapture exposes one. Without this drawn, holding the grip does
        /// nothing observable and the only way to find out whether it heard you is to let go and
        /// wait, which is exactly the report that came back from the headset.
        ///
        /// Built once and hidden, then driven from LateUpdate. It deliberately does NOT live in
        /// BuildPlates: that rebuilds the pointable plates, and rebuilding them every frame to
        /// animate a bar would destroy the interactable under the visitor's ray mid-press.
        /// </summary>
        /// <summary>
        /// A frame-time readout, drawn above the panel.
        ///
        /// <b>Judder from dropped frames and a rendering bug look identical from inside a
        /// headset</b> - in both cases the world appears to shake when you move. The only thing
        /// that separates them is the number, and guessing between them has already cost most of
        /// a day. CLAUDE.md's "no shake on Quest" was measured on SmallWorlds.unity, which has
        /// splats and nothing else; this scene also carries three animated companions, a wall of
        /// artwork and this panel, so it is not the same measurement.
        /// </summary>
        TMP_Text _fps;
        float _fpsAccum;
        int _fpsFrames;
        float _fpsWorst;

        void BuildFps()
        {
            _fps = new GameObject("Frame Rate").AddComponent<TextMeshPro>();
            _fps.transform.SetParent(transform, false);
            _fps.transform.localPosition = new Vector3(0f, CopyTop + 0.30f, 0f);
            _fps.fontSize = 0.42f;
            _fps.alignment = TextAlignmentOptions.Center;
            _fps.enableWordWrapping = false;
            _fps.rectTransform.sizeDelta = new Vector2(width, 0.16f);
        }

        void DrawFps()
        {
            if (_fps == null) return;
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (Time.unscaledDeltaTime > _fpsWorst) _fpsWorst = Time.unscaledDeltaTime;

            if (_fpsAccum < 0.5f) return;
            var ms = _fpsAccum / _fpsFrames * 1000f;
            var fps = 1000f / ms;
            var worstMs = _fpsWorst * 1000f;
            _fpsAccum = 0f; _fpsFrames = 0; _fpsWorst = 0f;

            // Gold while it is holding the refresh rate, red once it is not.
            var ok = fps >= 68f;
            _fps.text = "<cspace=0.14em><color=" + (ok ? Gold : "#E06C5A") + ">"
                      + fps.ToString("0") + " FPS  ·  " + ms.ToString("0.0") + " ms  ·  worst "
                      + worstMs.ToString("0") + " ms</color></cspace>";
        }

        void BuildMic()
        {
            _mic = new GameObject("Microphone").transform;
            _mic.SetParent(transform, false);
            _mic.localPosition = new Vector3(0f, -0.86f, 0f);

            _micText = new GameObject("Label").AddComponent<TextMeshPro>();
            _micText.transform.SetParent(_mic, false);
            _micText.transform.localPosition = new Vector3(0f, 0.075f, 0f);
            _micText.fontSize = 0.50f;
            _micText.alignment = TextAlignmentOptions.Center;
            _micText.enableWordWrapping = false;
            _micText.rectTransform.sizeDelta = new Vector2(width, 0.14f);

            var track = GameObject.CreatePrimitive(PrimitiveType.Quad);
            track.name = "Track";
            Destroy(track.GetComponent<Collider>());
            track.transform.SetParent(_mic, false);
            track.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            track.transform.localScale = new Vector3(0.90f, 0.022f, 1f);
            track.GetComponent<MeshRenderer>().sharedMaterial = FlatMaterial(
                new Color(0.933f, 0.914f, 0.875f, 0.20f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            Destroy(fill.GetComponent<Collider>());
            fill.transform.SetParent(_mic, false);
            fill.GetComponent<MeshRenderer>().sharedMaterial = FlatMaterial(
                new Color(0.788f, 0.667f, 0.447f, 0.95f));
            _micFill = fill.transform;

            _mic.gameObject.SetActive(false);
        }

        /// <summary>An unlit transparent material, the one shape this panel keeps making.</summary>
        static Material FlatMaterial(Color colour)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 5;
            return m;
        }

        /// <summary>Show what the microphone is doing, if anything.</summary>
        void DrawMic()
        {
            if (_mic == null) return;
            if (_mic.gameObject.activeSelf != listening) _mic.gameObject.SetActive(listening);
            if (!listening) return;

            _mic.localPosition = new Vector3(0f, -0.86f, 0f);

            // A floor under the bar, so "open but hearing nothing" still reads as open rather than
            // as a dead control. Anything above the floor is genuinely your voice.
            var shown = Mathf.Clamp01(micLevel * 6f);
            var w = 0.90f * Mathf.Max(shown, 0.02f);
            _micFill.localScale = new Vector3(w, 0.022f, 1f);
            _micFill.localPosition = new Vector3(-(0.90f - w) * 0.5f, 0f, 0.012f);

            _micText.text = "<cspace=0.2em><color=" + Gold + ">LISTENING" +
                            (shown > 0.06f ? " \u25cf" : " \u25cb") + "</color></cspace>";
        }

        /// <summary>
        /// Plant the anchors on the visitor where they stand. Without this the panel starts at the
        /// world origin and slides in from wherever that happens to be.
        /// </summary>
        void Seat()
        {
            _ground = new Vector2(head.position.x, head.position.z);
            _heldFloor = floorY;
            _walking = false;
            _seated = true;
        }

        /// <summary>Draw a stage. Safe to call every frame; it only rebuilds when the copy changes.</summary>
        public void Show(StagePanel panel)
        {
            if (panel == null) return;
            var markup = Markup(panel);
            if (_panel != null && _text.text == markup && SameChoices(panel)) return;

            _panel = panel;
            _text.text = markup;
            PlaceSpeaker(panel.SpeakerImageId);

            // Measure what was actually drawn before placing anything under it. A fixed copy height
            // leaves the threshold's one button stranded half a metre below its own hint, and
            // crushes stage 02, whose copy is three lines longer.
            _text.ForceMeshUpdate();

            // Quantised to a line-ish step. The copy is measured because ten stages differ by
            // several lines - but a raw measurement also moves by a millimetre whenever a word
            // changes, and everything below is placed relative to it, so the whole panel breathed.
            // Rounding up to a step means only a REAL change of line count moves anything.
            var measured = Mathf.Max(_text.textBounds.size.y, 0.2f);
            var copyHeight = Mathf.Ceil(measured / CopyStep) * CopyStep;
            BuildPlates(panel, copyHeight);
            FitScrim(panel, copyHeight);
        }

        Transform _speaker;
        const float SpeakerFace = 0.34f, SpeakerGap = 0.06f;

        /// <summary>
        /// The speaking master's portrait at the top left of the copy, the text set beside it — her
        /// popup's head row. Hidden, and the copy back to full width, when no one is named.
        /// </summary>
        void PlaceSpeaker(string imageId)
        {
            var texture = string.IsNullOrEmpty(imageId) || ImageFor == null ? null : ImageFor(imageId);
            if (texture == null)
            {
                if (_speaker != null) _speaker.gameObject.SetActive(false);
                _text.margin = Vector4.zero;
                return;
            }
            if (_speaker == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Speaker Portrait";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                quad.transform.localScale = new Vector3(SpeakerFace, SpeakerFace, 1f);
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetColor("_BaseColor", Color.white);
                quad.GetComponent<MeshRenderer>().sharedMaterial = m;
                _speaker = quad.transform;
            }
            _speaker.gameObject.SetActive(true);
            var material = _speaker.GetComponent<MeshRenderer>().sharedMaterial;
            material.SetTexture("_BaseMap", texture);
            CoverCrop(material, texture, 1f);
            _speaker.localPosition = new Vector3(-width * 0.5f + SpeakerFace * 0.5f, CopyTop - SpeakerFace * 0.5f, 0.01f);
            _text.margin = new Vector4(SpeakerFace + SpeakerGap, 0f, 0f, 0f);
        }

        /// <summary>
        /// Her guided-walk HUD: an arrow pointing at the next stop, the stop counter, and a live
        /// distance. Pass a stop with no total to hide it.
        ///
        /// The arrow is built as a triangle MESH rather than a glyph. U+2191 is not in
        /// LiberationSans SDF — the same trap that made the selection tick render as a box — and an
        /// arrow that silently becomes a rectangle is worse than no arrow, because the visitor
        /// still trusts it.
        /// </summary>
        public void ShowCompass(TourStop stop)
        {
            if (_compass == null) BuildCompass();

            if (!stop.HasStop)
            {
                _compassPlaced = false;
                _compass.gameObject.SetActive(false);
                _shownStop = stop;
                return;
            }

            _compass.gameObject.SetActive(true);
            if (!TourGuide.Differs(_shownStop, stop) && _compassText.text.Length > 0) return;
            _shownStop = stop;

            // Bearing is degrees to the RIGHT, and a positive Z rotation turns anticlockwise on a
            // panel whose +Z faces away — so the arrow takes the negative.
            _arrow.localRotation = Quaternion.Euler(0f, 0f, -stop.Bearing);

            var title = string.IsNullOrEmpty(stop.Title) ? string.Empty : "\n" + stop.Title;
            _compassText.text =
                "<size=74%><cspace=0.18em><color=" + Gold + ">" + TourGuide.Step(stop) + "</color></cspace></size>"
                + title
                + "\n<size=66%><cspace=0.14em><color=" + Ivory + "AA>" + TourGuide.Hint(stop) + "</color></cspace></size>";
        }

        void BuildCompass()
        {
            // Not a child of the panel: the panel stands off to one side in the gallery and eases
            // after the gaze, and the compass has to be where the visitor is looking. Its own root,
            // centred on the gaze and kept there every frame (DrawCompassPose).
            _compass = new GameObject("Compass").transform;

            var arrowGo = new GameObject("Arrow");
            arrowGo.transform.SetParent(_compass, false);
            arrowGo.transform.localPosition = new Vector3(-0.72f, -0.02f, 0f);
            arrowGo.transform.localScale = Vector3.one * 0.13f;
            var filter = arrowGo.AddComponent<MeshFilter>();
            filter.sharedMesh = ArrowMesh();
            var renderer = arrowGo.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", new Color(0.788f, 0.667f, 0.447f, 1f));   // her gold
            renderer.sharedMaterial = material;
            _arrow = arrowGo.transform;

            var textGo = new GameObject("Reading");
            textGo.transform.SetParent(_compass, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0f);
            _compassText = textGo.AddComponent<TextMeshPro>();
            _compassText.fontSize = 0.58f;
            _compassText.alignment = TextAlignmentOptions.Left;
            _compassText.enableWordWrapping = false;
            _compassText.rectTransform.sizeDelta = new Vector2(1.5f, 0.34f);
        }

        /// <summary>How far ahead of the eye, and how far below it, the compass hangs. 0.7 m down at
        /// 2.4 m is 16 degrees: in view without looking down, under anything at eye height.</summary>
        const float CompassAhead = 2.4f, CompassBelow = 0.7f;

        /// <summary>
        /// Keep the compass centred on the gaze, level, and always in view. Yaw follows the head
        /// closely (a fraction of a second) rather than rigidly, so it does not swim with every
        /// small head movement.
        /// </summary>
        void DrawCompassPose()
        {
            if (_compass == null || !_compass.gameObject.activeSelf) return;
            var target = head.eulerAngles.y;
            _compassYaw = _compassPlaced ? Mathf.LerpAngle(_compassYaw, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime)) : target;
            _compassPlaced = true;
            var rot = PanelAnchor.Rotation(_compassYaw);
            _compass.SetPositionAndRotation(head.position + rot * Vector3.forward * CompassAhead + Vector3.down * CompassBelow, rot);
        }

        float _compassYaw;
        bool _compassPlaced;

        void OnDestroy()
        {
            if (_compass != null) Destroy(_compass.gameObject);
        }

        /// <summary>
        /// A flat triangle, wound both ways.
        ///
        /// Double-sided on purpose: this mesh hangs on a panel whose facing is decided by
        /// PanelAnchor, and a single-sided arrow that disappears depending on which way the panel
        /// settled is exactly the class of bug the Quad normal already caused once.
        /// </summary>
        static Mesh ArrowMesh()
        {
            var mesh = new Mesh { name = "Compass Arrow" };
            mesh.SetVertices(new System.Collections.Generic.List<Vector3>
            {
                new Vector3(0f, 0.55f, 0f),
                new Vector3(-0.42f, -0.4f, 0f),
                new Vector3(0.42f, -0.4f, 0f),
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 1 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Until when the panel snaps to the head instead of easing after it.</summary>
        float _snapUntil = 1.5f;

        /// <summary>Snap to face the visitor now, without easing. Used when a stage opens.</summary>
        public void Reorient()
        {
            _snapUntil = Mathf.Max(_snapUntil, Time.unscaledTime + 0.5f);
            Snap();
        }

        void Snap()
        {
            if (head == null) return;
            _yaw = head.eulerAngles.y;
            _following = false;
            Seat();
            transform.SetPositionAndRotation(
                PanelAnchor.Position(head.position, _yaw + yawOffset, floorY) + Vector3.up * _lift, PanelAnchor.Rotation(_yaw + yawOffset));
        }

        /// <summary>
        /// Her panel, as one block of rich text.
        ///
        /// <c>fontSize</c> in world space is neither metres nor points — measured, 0.6 is about
        /// 16 mm of cap height — so every size here is a PERCENTAGE of the base, which is the only
        /// way the hierarchy survives someone changing the base.
        /// </summary>
        public static string Markup(StagePanel panel)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(panel.Marker))
                sb.Append("<size=58%><cspace=0.14em><color=").Append(Ivory).Append("44>")
                  .Append(panel.Marker).Append("</color></cspace></size>\n<size=24%> </size>\n");

            // Gold and widely tracked, as her `.eyebrow` is — this is the one coloured line, and it
            // is what tells you which stage you are standing in.
            if (!string.IsNullOrEmpty(panel.Eyebrow))
                sb.Append("<size=70%><cspace=0.26em><color=").Append(Gold).Append(">")
                  .Append(panel.Eyebrow).Append("</color></cspace></size>\n");

            // No faux bold: her display face is a light serif, and <b> on a sans is the loudest
            // "unpolished" signal there is. Negative tracking and tight leading instead.
            if (!string.IsNullOrEmpty(panel.Heading))
                // NO <line-height> on the heading. TMP applies it relative to the DEFAULT line
                // height rather than to the 300% size on that line, so anything under 100% — and
                // 108% too, measured — leaves a box shorter than the glyphs, and a heading that
                // wraps then climbs into the eyebrow above it. Stage 02 wraps; that is how it was
                // found. The default spacing is correct.
                sb.Append("<size=200%><cspace=-0.02em>")
                  .Append(panel.Heading).Append("</cspace></size>\n");

            if (!string.IsNullOrEmpty(panel.Lede))
                sb.Append("\n<line-height=150%><color=").Append(Ivory).Append("A8>")
                  .Append(panel.Lede).Append("</color></line-height>\n");

            if (!string.IsNullOrEmpty(panel.Hint))
                sb.Append("\n<size=64%><cspace=0.16em><color=").Append(Ivory).Append("99>")
                  .Append(panel.Hint).Append("</color></cspace></size>");

            if (!string.IsNullOrEmpty(panel.Notice))
                // Spacer line, for the same reason the marker has one: the hint and the notice are
                // both set below 100%, so TMP packs their shrunken line boxes until they touch.
                // A footnote: small and quiet, the last thing on the panel, never competing with what
                // the master said (Saul, 1 Oct 2026: it read "huge, front and centre").
                sb.Append("\n<size=24%> </size>\n<size=44%><cspace=0.06em><color=")
                  .Append(Ivory).Append("55>")
                  .Append(panel.Notice).Append("</color></cspace></size>");

            return sb.ToString();
        }

        /// <summary>Her `--ivory`: the warm off-white everything that is not gold is drawn in.</summary>
        public const string Ivory = "#EEE9DF";

        /// <summary>Her `--gold`, the single accent — eyebrows, selection, the rule under a title.</summary>
        public const string Gold = "#C9AA72";

        void BuildScrim()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Scrim";
            Destroy(go.GetComponent<Collider>());     // the plates own the pointing, not the backdrop
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.05f);   // behind the text, and clearly

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            material.SetColor("_BaseColor", new Color(0.03f, 0.025f, 0.02f, 0.84f));
            // URP needs telling, in four places, that a material is transparent. Setting only the
            // colour's alpha leaves it rendering fully opaque.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // The scrim WRITES DEPTH, unlike ordinary transparent geometry.
            //
            // Gaussian splats composite in their own URP pass, after this one, and they were
            // painting straight over a plate that wrote no depth — which is why raising the alpha
            // from 0.62 to 0.84 changed nothing visible and the foliage stayed legible through the
            // copy. Writing depth makes the splats lose the test behind it. Safe here because the
            // scrim is a single flat quad drawn before everything it must sit behind.
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_AlphaClip", 0f);
            // The floats alone are not enough: URP branches on the KEYWORD, and without it the
            // plate blended at roughly a third of the alpha it was given. Measured by setting 0.86
            // and getting a light haze instead of near-black.
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            // BEFORE the text, explicitly. Both were queue 3000 with ZWrite off, so Unity sorted
            // them by bounds-centre distance — and on any stage with cards FitScrim moved the
            // scrim's centre nearer the camera, so the backing plate painted OVER the copy.
            // Measured: headings rendered at 105/255 grey instead of ~240, which is the whole of
            // "the colours are hard to see". The threshold looked fine only because its short
            // scrim happened to win the coin toss the other way.
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;

            _scrim = go.transform;
        }

        /// <summary>Where the first row of choices sits, given the copy actually drawn above it.</summary>
        static float PlatesTop(float copyHeight) => CopyTop - copyHeight - 0.10f;

        /// <summary>Where the forward action sits, below however many rows of choices there are.</summary>
        static float ActionY(float platesTop, int rows, int choiceCount) =>
            platesTop - rows * RowSpacing - (choiceCount > 0 ? 0.14f : 0f);

        static int Rows(int choiceCount) =>
            choiceCount == 0 ? 0
                : Mathf.CeilToInt(choiceCount /
                    (float)Mathf.Max(1, Mathf.CeilToInt(choiceCount / (float)MaxRows)));

        /// <summary>
        /// Stretch the plate over everything this stage drew.
        ///
        /// It reaches the ACTION, not just the choices — the first version stopped at the copy and
        /// left "FORM MY ANSWER" floating unbacked on a sunlit glasshouse, which is exactly the
        /// case the scrim exists for. Shared helpers with <see cref="BuildPlates"/> so the two
        /// cannot drift.
        /// </summary>
        void FitScrim(StagePanel panel, float copyHeight)
        {
            if (_scrim == null) return;

            var rows = Rows(panel.Choices.Count);
            var top = PlatesTop(copyHeight) - _asideHeight - _fieldHeight;
            var lowest = HasPortraits(panel)
                ? top - _cardHeight - (string.IsNullOrEmpty(panel.Action) ? 0f : 0.14f)
                : string.IsNullOrEmpty(panel.Action)
                    ? top - Mathf.Max(rows - 1, 0) * RowSpacing
                    : ActionY(top, rows, panel.Choices.Count);

            if (!float.IsNaN(_controlsLowest)) lowest = _controlsLowest;
            var bottom = lowest - 0.18f;

            // Remembered so the microphone can sit clear of whatever this stage actually drew,
            // instead of at a fixed height that some stages reach and others do not.
            _bottom = bottom;
            // A panel taller than the room between its anchor and the floor is raised until its
            // lowest edge clears the floor: a button under the floor is a softlock (stage 06 was one).
            _lift = Mathf.Max(0f, MinFloorClearance - (PanelAnchor.Height + bottom));
            var height = (CopyTop + 0.16f) - bottom;

            // Even padding. It was 27 cm at the sides against 7 cm top and bottom, which is what
            // made the plate look like a slab the copy had been dropped onto.
            _scrim.localScale = new Vector3(width + 0.24f, height, 1f);
            _scrim.localPosition = new Vector3(0f, CopyTop + 0.16f - height * 0.5f, 0.02f);
        }

        void BuildText()
        {
            var go = new GameObject("Copy");
            go.transform.SetParent(transform, false);
            _text = go.AddComponent<TextMeshPro>();
            // Her scale ratio is 7.2 : 1.4 : 1 : 0.8 across heading / lede / eyebrow / hint. Ours
            // was 2.4 : 1.3 : 1 : 0.97 — four near-identical grey lines with nothing leading the
            // eye. Dropping the base and pushing the heading restores the ratio.
            _text.fontSize = 0.72f;
            _text.alignment = TextAlignmentOptions.Top;
            _text.enableWordWrapping = true;
            _text.richText = true;
            _text.rectTransform.sizeDelta = new Vector2(width, CopyHeight);
            // Top alignment draws from the top edge while localPosition is the CENTRE, so the block
            // is seated by half its own height. Getting this wrong is what overlapped the text.
            _text.rectTransform.localPosition = new Vector3(0f, CopyTop - CopyHeight * 0.5f, 0f);
        }

        /// <summary>
        /// Whether the plates on offer are the same ones already drawn.
        ///
        /// It must compare the SELECTED flag and the field, not just the ids. It did not, and the
        /// bug was invisible only because the hint used to carry the chosen question - so picking
        /// an option changed the markup, and the markup comparison forced the rebuild by accident.
        /// Moving the question into its own fixed field removed that accident and the selection
        /// stopped drawing at all. A guard that depends on an unrelated string changing is not a
        /// guard.
        /// </summary>
        bool SameChoices(StagePanel panel)
        {
            if (panel.Choices.Count != ChoiceIds.Count) return false;
            for (var i = 0; i < panel.Choices.Count; i++)
            {
                if (panel.Choices[i].Id != ChoiceIds[i]) return false;
                if (panel.Choices[i].Selected != _shownSelected[i]) return false;
            }
            if (panel.Field != _shownField) return false;
            if (panel.NavIndex != _shownNavIndex) return false;
            return true;
        }

        /// <summary>What was selected when the plates were last built, parallel to ChoiceIds.</summary>
        readonly List<bool> _shownSelected = new List<bool>();

        /// <summary>The field contents last drawn, so filling it in redraws it.</summary>
        string _shownField;

        /// <summary>The room last drawn, so the navigator's dot moves with it.</summary>
        int _shownNavIndex = -1;

        /// <summary>Whether the finish menu (the action behind <see cref="StagePanel.MenuLabel"/>) is open.</summary>
        bool _menuOpen;

        /// <summary>The heading the menu was opened under; any other heading closes it.</summary>
        string _menuHeading;

        /// <summary>The lowest control the gallery layout drew, or NaN for every other layout.</summary>
        float _controlsLowest = float.NaN;

        /// <summary>Redraw on the next Show, for state the panel owns (the menu) rather than the stage.</summary>
        void Rebuild() => _panel = null;

        /// <summary>
        /// The gallery: changing world is the main control, and ending the walk is a menu away.
        ///
        /// Saul, 27 Sep 2026, after comparing both builds: "FORM MY ANSWER" is meant for the END of
        /// the experience, after exploring and asking more questions, so it must not be the most
        /// prominent thing on the panel. The navigator it used to sit above was two 22 cm arrows
        /// and 18 mm dots. Now the arrows are full plates that NAME the world they go to, and the
        /// action sits behind a small "FINISH THE WALK…" toggle with one line saying what it does.
        ///
        /// Returns the centre height of the lowest row drawn, for the scrim.
        /// </summary>
        float BuildGalleryControls(StagePanel panel, float y)
        {
            var row = y;

            if (panel.NavTotal > 0)
            {
                var i = panel.NavIndex;
                var n = panel.NavTotal;
                var half = width * 0.485f;

                Plate(i > 0 ? "← " + panel.NavPrevLabel : "←", false, half,
                    new Vector3(-width * 0.2575f, row, 0f), () => NavTaken?.Invoke(i - 1), i > 0, 0.34f);
                Plate(i < n - 1 ? panel.NavNextLabel + " →" : "→", false, half,
                    new Vector3(width * 0.2575f, row, 0f), () => NavTaken?.Invoke(i + 1), i < n - 1, 0.34f);

                row -= 0.17f;
                // No counter here: the panel's marker already says "01 / 08", and drawing it
                // twice was flagged by the independent review (27 Sep).
                BuildRoomDots(i, n, row, counter: false);
                row -= 0.17f;
            }

            if (!_menuOpen)
            {
                Plate(panel.MenuLabel, false, width * 0.34f, new Vector3(width * 0.31f, row, 0f),
                    () => { _menuOpen = true; Rebuild(); }, true, 0.28f, pillHeight: 0.085f, maxFont: 0.40f);
                return row;
            }

            var note = new GameObject("Menu Note").AddComponent<TextMeshPro>();
            note.transform.SetParent(_plates, false);
            note.transform.localPosition = new Vector3(0f, row, 0f);
            note.fontSize = 0.44f;
            note.alignment = TextAlignmentOptions.Center;
            note.enableWordWrapping = true;
            note.rectTransform.sizeDelta = new Vector2(width * 0.92f, 0.14f);
            note.text = "<color=" + Ivory + "CC>" + panel.MenuNote + "</color>";

            row -= 0.17f;
            Plate("NOT YET", false, width * 0.30f, new Vector3(-width * 0.33f, row, 0f),
                () => { _menuOpen = false; Rebuild(); }, true, 0.34f);
            Plate(panel.Action, false, width * 0.58f, new Vector3(width * 0.19f, row, 0f),
                () => { if (panel.ActionEnabled) { _menuOpen = false; ActionTaken?.Invoke(); } },
                panel.ActionEnabled, 0.38f);
            return row;
        }

        void BuildPlates(StagePanel panel, float copyHeight)
        {
            // Only a change of SCREEN arms the guard, not every rebuild: the panel also rebuilds
            // when a choice is ticked or a menu opens, and those must stay responsive.
            // Marker + eyebrow identify the screen; the heading does not (on the ask form it IS the
            // question being typed, so it changes on every key).
            if (panel.Eyebrow != _guardHeading || panel.Marker != _guardMarker)
            {
                _builtAt = Time.unscaledTime;
                _guardHeading = panel.Eyebrow;
                _guardMarker = panel.Marker;
            }
            for (var i = _plates.childCount - 1; i >= 0; i--) Destroy(_plates.GetChild(i).gameObject);

            var ids = new List<string>();
            var count = panel.Choices.Count;

            _asideHeight = BuildAside(panel, PlatesTop(copyHeight));
            _fieldHeight = BuildField(panel, PlatesTop(copyHeight) - _asideHeight);
            var top = PlatesTop(copyHeight) - _asideHeight - _fieldHeight;

            var portraits = HasPortraits(panel);
            if (portraits) BuildPortraitRow(panel, top, ids);
            else BuildTextRows(panel, top, ids);

            // A new stage or room closes the finish menu; it only ever opens on purpose.
            if (panel.Heading != _menuHeading) { _menuOpen = false; _menuHeading = panel.Heading; }
            _controlsLowest = float.NaN;

            if (!string.IsNullOrEmpty(panel.Action) && panel.ActionInMenu)
            {
                var y = portraits
                    ? top - _cardHeight - 0.14f
                    : ActionY(top, Rows(count), count);
                _controlsLowest = BuildGalleryControls(panel, y);
            }
            else if (!string.IsNullOrEmpty(panel.Action))
            {
                var y = portraits
                    ? top - _cardHeight - 0.14f
                    : ActionY(top, Rows(count), count);
                // Her `.salon-next` is a quiet corner control, not a `.primary-action` pill: in
                // the gallery the point is to WALK and look, and a huge button in the middle of the
                // room tells the visitor the opposite.
                var forwardWidth = panel.ActionIsPrimary ? width * 0.7f : width * 0.42f;
                var hasBack = !string.IsNullOrEmpty(panel.Back);

                // With a back control the pair sits side by side, forward on the right — the
                // direction it goes. Alone, the forward action keeps the centre.
                // Spans measured against the panel's own half-width so the pair can neither
                // overlap nor run off the edge: back [-0.48w, -0.17w], forward [-0.11w, +0.49w].
                // A lower floor than a choice plate. These carry her longest wording -
                // "CHOOSE WHO WALKS WITH ME" - and a truncated instruction is worse than a
                // slightly smaller one; the choices above stay at the higher floor.
                Plate(panel.Action, false, hasBack ? width * 0.60f : forwardWidth,
                    new Vector3(hasBack ? width * 0.19f : 0f, y, 0f),
                    () => { if (panel.ActionEnabled) ActionTaken?.Invoke(); }, panel.ActionEnabled, 0.38f);

                if (hasBack)
                    Plate(panel.Back, false, width * 0.315f, new Vector3(-width * 0.324f, y, 0f),
                        () => BackTaken?.Invoke(), true, 0.38f);

                if (panel.NavTotal > 0) BuildNavigator(panel, y - 0.30f);
            }

            ChoiceIds = ids;

            // Remember the state the plates were built FROM, so SameChoices can tell when it has
            // genuinely changed rather than inferring it from an unrelated string.
            _shownSelected.Clear();
            foreach (var c in panel.Choices) _shownSelected.Add(c.Selected);
            _shownField = panel.Field;
            _shownNavIndex = panel.NavIndex;
        }

        /// <summary>
        /// Her <c>&lt;textarea&gt;</c>: the question, in a box of FIXED height.
        ///
        /// Fixed is the requirement, not a detail. Everything below this is positioned relative to
        /// it, so if it grew with its contents then picking a question would move the buttons - and
        /// in a headset the ray you were aiming is already on one of them.
        ///
        /// Returns the height it used, so the caller can place the choices under it.
        /// </summary>
        float BuildField(StagePanel panel, float top)
        {
            for (var i = _field.childCount - 1; i >= 0; i--) Destroy(_field.GetChild(i).gameObject);
            if (panel.Field == null) return 0f;

            var centre = top - FieldHeight * 0.5f;
            var filled = panel.Field.Length > 0;

            var box = GameObject.CreatePrimitive(PrimitiveType.Quad);
            box.name = "Field Box";
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(_field, false);
            box.transform.localPosition = new Vector3(0f, centre, 0.008f);
            box.transform.localScale = new Vector3(width * 0.92f, FieldHeight - 0.06f, 1f);
            box.GetComponent<MeshRenderer>().sharedMaterial = FlatMaterial(
                new Color(0.06f, 0.05f, 0.04f, 0.55f));

            var rule = GameObject.CreatePrimitive(PrimitiveType.Quad);
            rule.name = "Field Rule";
            Destroy(rule.GetComponent<Collider>());
            rule.transform.SetParent(_field, false);
            rule.transform.localPosition = new Vector3(0f, centre - (FieldHeight - 0.06f) * 0.5f, 0.009f);
            rule.transform.localScale = new Vector3(width * 0.92f, 0.006f, 1f);
            rule.GetComponent<MeshRenderer>().sharedMaterial = FlatMaterial(filled
                ? new Color(0.788f, 0.667f, 0.447f, 0.95f)
                : new Color(0.933f, 0.914f, 0.875f, 0.30f));

            var text = new GameObject("Field Text").AddComponent<TextMeshPro>();
            text.transform.SetParent(_field, false);
            text.transform.localPosition = new Vector3(0f, centre, 0.012f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.rectTransform.sizeDelta = new Vector2(width * 0.86f, FieldHeight - 0.08f);
            text.enableAutoSizing = true;
            text.fontSizeMax = 0.76f;
            text.fontSizeMin = 0.44f;
            text.text = filled
                ? "<color=" + Ivory + ">" + panel.Field + "</color>"
                : "<color=" + Ivory + "55><i>" + panel.FieldPlaceholder + "</i></color>";

            return FieldHeight + 0.06f;
        }

        /// <summary>
        /// The small, unpointable row of faces that belongs with the copy. Returns the height it
        /// used so the choices below it move down by exactly that much.
        /// </summary>
        float BuildAside(StagePanel panel, float top)
        {
            for (var i = _aside.childCount - 1; i >= 0; i--) Destroy(_aside.GetChild(i).gameObject);
            if (panel.Cards != null && panel.Cards.Count > 0) return BuildCards(panel, top);
            if (panel.Aside == null || panel.Aside.Count == 0) return 0f;

            const float face = 0.17f;
            const float gap = 0.05f;
            var pitch = face + gap;
            var height = face + 0.10f;
            var centre = top - height * 0.5f;

            for (var i = 0; i < panel.Aside.Count; i++)
            {
                var entry = panel.Aside[i];
                var x = (i - (panel.Aside.Count - 1) * 0.5f) * pitch;

                var go = new GameObject("Invited " + entry.Label);
                go.transform.SetParent(_aside, false);
                go.transform.localPosition = new Vector3(x, centre, 0f);

                var texture = ImageFor != null ? ImageFor(entry.ImageId) : null;
                if (texture != null)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "Face";
                    Destroy(quad.GetComponent<Collider>());   // nothing here is pointable
                    quad.transform.SetParent(go.transform, false);
                    quad.transform.localPosition = new Vector3(0f, 0.03f, 0.01f);
                    quad.transform.localScale = new Vector3(face, face, 1f);

                    var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    material.SetTexture("_BaseMap", texture);
                    CoverCrop(material, texture, 1f);          // square here, so a face stays a face
                    material.SetColor("_BaseColor", Color.white);
                    quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                }

                var label = new GameObject("Name").AddComponent<TextMeshPro>();
                label.transform.SetParent(go.transform, false);
                label.transform.localPosition = new Vector3(0f, -face * 0.5f - 0.005f, 0.01f);
                label.fontSize = 0.30f;
                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.rectTransform.sizeDelta = new Vector2(pitch, 0.07f);
                label.text = "<cspace=0.1em><color=" + Gold + ">" + entry.Label + "</color></cspace>";
            }

            return height + 0.06f;
        }

        /// <summary>
        /// Cards side by side under the copy: per card a face, a gold name, the words, and a small
        /// footnote. Her roundtable sets its threads across the page; stacked in the copy here they
        /// drove the panel's only button under the floor (Saul, 1 Oct 2026). Returns the row's height.
        /// </summary>
        float BuildCards(StagePanel panel, float top)
        {
            const float gap = 0.10f, face = 0.22f, pad = 0.04f;
            var n = panel.Cards.Count;
            var cardWidth = (width - gap * (n - 1)) / n;
            var tallest = 0f;
            var texts = new List<TextMeshPro>();
            for (var i = 0; i < n; i++)
            {
                var card = panel.Cards[i];
                var x = -width * 0.5f + cardWidth * 0.5f + i * (cardWidth + gap);
                var go = new GameObject("Card " + card.Title);
                go.transform.SetParent(_aside, false);
                go.transform.localPosition = new Vector3(x, top, 0f);

                var texture = ImageFor != null ? ImageFor(card.ImageId) : null;
                if (texture != null)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "Face";
                    Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(go.transform, false);
                    quad.transform.localPosition = new Vector3(-cardWidth * 0.5f + face * 0.5f, -face * 0.5f, 0.01f);
                    quad.transform.localScale = new Vector3(face, face, 1f);
                    var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    material.SetTexture("_BaseMap", texture);
                    CoverCrop(material, texture, 1f);
                    material.SetColor("_BaseColor", Color.white);
                    quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                }

                var name = new GameObject("Name").AddComponent<TextMeshPro>();
                name.transform.SetParent(go.transform, false);
                var nameLeft = texture != null ? face + pad : 0f;
                name.rectTransform.sizeDelta = new Vector2(cardWidth - nameLeft, face);
                name.transform.localPosition = new Vector3(nameLeft * 0.5f, -face * 0.5f, 0f);
                name.fontSize = 0.40f;
                name.alignment = TextAlignmentOptions.Left;
                name.enableWordWrapping = true;
                name.text = "<cspace=0.12em><color=" + Gold + ">" + card.Title.ToUpperInvariant() + "</color></cspace>";

                var body = new GameObject("Words").AddComponent<TextMeshPro>();
                body.transform.SetParent(go.transform, false);
                body.rectTransform.pivot = new Vector2(0.5f, 1f);
                body.rectTransform.sizeDelta = new Vector2(cardWidth, 1.2f);
                body.transform.localPosition = new Vector3(0f, -face - pad, 0f);
                body.fontSize = 0.52f;   // 0.42 read as fine print at 2.8 m
                body.alignment = TextAlignmentOptions.TopLeft;
                body.enableWordWrapping = true;
                body.text = "<color=" + Ivory + "D0>" + card.Body + "</color>" +
                            (string.IsNullOrEmpty(card.Footnote) ? "" :
                             "\n<size=60%><color=" + Ivory + "55>" + card.Footnote + "</color></size>");
                body.ForceMeshUpdate();
                tallest = Mathf.Max(tallest, face + pad + body.textBounds.size.y);
                texts.Add(body);
            }
            return tallest + 0.10f;
        }

        static bool HasPortraits(StagePanel panel)
        {
            foreach (var c in panel.Choices) if (!string.IsNullOrEmpty(c.ImageId)) return true;
            return false;
        }

        /// <summary>
        /// Her companion grid: the masters as pictures, side by side, names underneath.
        ///
        /// One row rather than a grid, because seven portraits at this size span about 2.2 m against
        /// a 2.4 m panel — and a single row keeps every card between eye height and knee height,
        /// which a stack of rows does not.
        /// </summary>
        void BuildPortraitRow(StagePanel panel, float top, List<string> ids)
        {
            var count = panel.Choices.Count;
            // Never wider than five across: the card's height follows its width, and with only two
            // masters offered a half-panel card stood 0.96 m tall and pushed the buttons under the
            // floor, where no click could reach them ("LET AI CURATE doesn't work", 1 Oct 2026).
            var pitch = width / Mathf.Max(count, 5);
            var aspect = CardAspect(panel);
            _cardHeight = (pitch - 0.03f) * 0.92f / Mathf.Max(aspect, 0.05f) + CaptionHeight + 0.06f;
            var centre = top - _cardHeight * 0.5f;

            for (var i = 0; i < count; i++)
            {
                var choice = panel.Choices[i];
                ids.Add(choice.Id);
                var id = choice.Id;
                var x = (i - (count - 1) * 0.5f) * pitch;

                PortraitPlate(choice, pitch - 0.03f, aspect, new Vector3(x, centre, 0f),
                    () => ChoiceTaken?.Invoke(id));
            }
        }

        /// <summary>Height of the card row last built, so the action and the scrim sit under it.</summary>
        float _cardHeight = PortraitHeight;

        /// <summary>Height the aside row took, so everything below it moves down by that much.</summary>
        float _asideHeight;

        /// <summary>How far the panel is raised so its lowest edge clears the floor.</summary>
        float _lift;
        const float MinFloorClearance = 0.35f;

        /// <summary>Height the question field took. Constant when there is one, zero when not.</summary>
        float _fieldHeight;

        /// <summary>Local Y of the lowest thing the panel drew, so the microphone clears it.</summary>
        float _bottom = -0.5f;

        void BuildTextRows(StagePanel panel, float top, List<string> ids)
        {
            var count = panel.Choices.Count;
            var columns = Mathf.Max(1, Mathf.CeilToInt(count / (float)MaxRows));
            var rows = columns == 0 ? 0 : Mathf.CeilToInt(count / (float)columns);
            var plateWidth = width / columns - 0.06f;

            for (var i = 0; i < count; i++)
            {
                var choice = panel.Choices[i];
                ids.Add(choice.Id);
                var id = choice.Id;

                var column = i / Mathf.Max(rows, 1);
                var row = i % Mathf.Max(rows, 1);
                var x = columns == 1 ? 0f : (column - (columns - 1) * 0.5f) * (width / columns);

                // Her artwork answers run to 66 characters and ellipsised at the one-line floor
                // (seen 27 Sep: "…I will see different…"). A long label gets two lines instead.
                var wrap = choice.Label.Length > WrapAbove;
                Plate(choice.Label, choice.Selected, plateWidth,
                    new Vector3(x, top - row * RowSpacing, 0f), () => ChoiceTaken?.Invoke(id),
                    true, wrap ? 0.42f : 0.54f, wrap ? 0.20f : 0.135f, wrap ? 0.56f : 0.72f, wrap);
            }
        }

        /// <summary>
        /// The aspect every card on this stage shares, taken as the MEDIAN of the pictures it has.
        ///
        /// <b>Cards must be identical; pictures must not be distorted.</b> Fitting each image to
        /// its own proportions was the first attempt and it is why the row looked broken — seven
        /// portraits scanned at seven different aspect ratios produced seven different card sizes,
        /// and a ragged row reads as a bug rather than as a gallery. The median means a row of tall
        /// portraits gets a tall card and a row of wide thumbnails gets a wide one, with no stage
        /// having to declare which it is.
        /// </summary>
        float CardAspect(StagePanel panel)
        {
            var aspects = new List<float>();
            foreach (var c in panel.Choices)
            {
                var t = ImageFor != null ? ImageFor(c.ImageId) : null;
                if (t != null && t.height > 0) aspects.Add(MuseXR.Worlds.PictureAspect.Of(t));
            }
            if (aspects.Count == 0) return 0.75f;

            aspects.Sort();
            return aspects[aspects.Count / 2];
        }

        /// <summary>
        /// Crop a picture to fill the card without distorting it — CSS <c>background-size: cover</c>,
        /// which is exactly what her <c>.companion-card</c> does with its <c>--portrait</c>.
        ///
        /// The quad is always the full card; the CROP happens in UV space, so every card in the row
        /// is the same size and every face is centred in it.
        /// </summary>
        static void CoverCrop(Material material, Texture2D texture, float cardAspect)
        {
            if (texture == null || texture.height <= 0) return;

            var imageAspect = MuseXR.Worlds.PictureAspect.Of(texture);
            if (imageAspect > cardAspect)
            {
                // Too wide: keep full height, take a centred slice of the width.
                var scale = cardAspect / imageAspect;
                material.SetTextureScale("_BaseMap", new Vector2(scale, 1f));
                material.SetTextureOffset("_BaseMap", new Vector2((1f - scale) * 0.5f, 0f));
            }
            else
            {
                // Too tall: keep full width, take a centred slice of the height. Biased ABOVE
                // centre, because on a portrait the face is in the upper half and a centred crop
                // beheads it.
                var scale = imageAspect / cardAspect;
                material.SetTextureScale("_BaseMap", new Vector2(1f, scale));
                material.SetTextureOffset("_BaseMap", new Vector2(0f, (1f - scale) * 0.72f));
            }
        }

        /// <summary>
        /// Her <c>.scene-navigator</c>: a back arrow, "03 / 09", a dot per room, a forward arrow.
        ///
        /// Every part of it is pointable, and the arrows go dead at the ends exactly as her
        /// <c>disabled</c> attribute does. This is the whole of how a visitor changes room.
        /// </summary>
        void BuildNavigator(StagePanel panel, float y)
        {
            var i = panel.NavIndex;
            var n = panel.NavTotal;

            Plate("←", false, 0.22f, new Vector3(-width * 0.40f, y, 0f),
                () => NavTaken?.Invoke(i - 1), i > 0);
            Plate("→", false, 0.22f, new Vector3(width * 0.40f, y, 0f),
                () => NavTaken?.Invoke(i + 1), i < n - 1);
            BuildRoomDots(i, n, y);
        }

        /// <summary>"03 / 09" above one pointable dot per room.</summary>
        void BuildRoomDots(int i, int n, float y, bool counter = true)
        {
            if (counter) BuildRoomCounter(i, n, y);
            BuildDots(i, n, y);
        }

        void BuildRoomCounter(int i, int n, float y)
        {
            var counter = new GameObject("Room Counter").AddComponent<TextMeshPro>();
            counter.transform.SetParent(_plates, false);
            counter.transform.localPosition = new Vector3(0f, y + 0.105f, 0f);
            counter.fontSize = 0.46f;
            counter.alignment = TextAlignmentOptions.Center;
            counter.enableWordWrapping = false;
            counter.rectTransform.sizeDelta = new Vector2(width * 0.5f, 0.14f);
            counter.text = "<cspace=0.22em><color=" + Gold + ">" +
                           (i + 1).ToString("00") + " / " + n.ToString("00") + "</color></cspace>";
        }

        void BuildDots(int i, int n, float y)
        {
            // One dot per room. Tiny, so they are given a collider far larger than they look -
            // a 6 mm target is unhittable with a ray from two and a half metres.
            var pitch = Mathf.Min(0.085f, width * 0.55f / Mathf.Max(n, 1));
            for (var k = 0; k < n; k++)
            {
                var here = k;
                var x = (k - (n - 1) * 0.5f) * pitch;

                var dot = new GameObject("Room " + (k + 1));
                dot.transform.SetParent(_plates, false);
                dot.transform.localPosition = new Vector3(x, y - 0.035f, 0f);

                var box = dot.AddComponent<BoxCollider>();
                box.size = new Vector3(pitch, 0.10f, 0.04f);

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Pip";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(dot.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                var size = k == i ? 0.030f : 0.018f;
                quad.transform.localScale = new Vector3(size, size, 1f);

                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetColor("_BaseColor", k == i
                    ? new Color(0.788f, 0.667f, 0.447f, 1f)            // --gold, the room you are in
                    : new Color(0.933f, 0.914f, 0.875f, 0.34f));
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 5;
                quad.GetComponent<MeshRenderer>().sharedMaterial = mat;

                Pointable(dot, () => NavTaken?.Invoke(here));
            }
        }

        /// <summary>A choice as a picture you can point at, with its caption under it.</summary>
        void PortraitPlate(StageChoice choice, float cardWidth, float cardAspect, Vector3 at,
                           Action onPick)
        {
            var go = new GameObject("Portrait " + choice.Label);
            go.transform.SetParent(_plates, false);
            // Her `.companion-card.selected` lifts 12px of a 250px card. Ours lifts the same
            // proportion, and toward the viewer as well, so the cue survives a dark photograph.
            go.transform.localPosition = choice.Selected
                ? at + new Vector3(0f, 0.022f, -0.012f)
                : at;

            // One size for every card on the stage. The picture is cropped into it, never fitted.
            var imageWidth = cardWidth * 0.92f;
            var imageHeight = imageWidth / Mathf.Max(cardAspect, 0.05f);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(cardWidth, imageHeight + CaptionHeight, 0.04f);

            // A frame BEHIND the portrait, lit only when the master is invited.
            //
            // Dimming the portrait itself was the first attempt and it does not work: these are
            // seven real photographs and paintings with wildly different exposures, so a bright
            // unselected Monet still outshines a dim selected Socrates and the visitor cannot tell
            // who they picked. A frame is the same for everyone, which is the whole point — it is
            // also what her card does with its `.selected` border and its check.
            var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frame.name = choice.Selected ? "Frame (selected)" : "Frame";
            Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(go.transform, false);
            var imageY = CaptionHeight * 0.5f + 0.02f;

            frame.transform.localPosition = new Vector3(0f, imageY, 0.015f);
            // A HAIRLINE, not a mat. Hers is a 1px border on a 250px card — about 2 mm at this
            // scale; ours was 15 mm of mid-grey passe-partout, which made the row read as a
            // contact sheet rather than as seven pictures.
            // A selected card gets a GOLD edge, and a thicker one. Her near-white border reads as
            // "brightly lit" rather than "chosen" on a wall of photographs that are already white,
            // and a 2 mm hairline is under one pixel at arm's length in a headset. Gold is the
            // palette's one accent and appears nowhere else on a card, so it cannot be mistaken
            // for part of the picture.
            var edge = choice.Selected ? 0.022f : 0.006f;
            frame.transform.localScale = new Vector3(imageWidth + edge, imageHeight + edge, 1f);
            var frameMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            frameMat.SetColor("_BaseColor", choice.Selected
                ? new Color(0.788f, 0.667f, 0.447f, 1f)                 // --gold #C9AA72
                : new Color(0.933f, 0.914f, 0.875f, 0.22f));            // ivory at 22%, her hairline
            frame.GetComponent<MeshRenderer>().sharedMaterial = frameMat;

            var texture = ImageFor != null ? ImageFor(choice.ImageId) : null;
            if (texture != null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Face";
                Destroy(quad.GetComponent<Collider>());     // the card owns the pointing
                quad.transform.SetParent(go.transform, false);
                quad.transform.localPosition = new Vector3(0f, imageY, 0.01f);
                quad.transform.localScale = new Vector3(imageWidth, imageHeight, 1f);

                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetTexture("_BaseMap", texture);
                CoverCrop(material, texture, cardAspect);
                // Every portrait renders at full strength. Selection is the frame, never the
                // picture, so a dark photograph is not read as "not chosen".
                material.SetColor("_BaseColor", Color.white);
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            // No tick glyph. U+2713 is not in LiberationSans SDF and renders as a missing-glyph
            // box, which is worse than nothing — and the gold frame plus the gold name already say
            // "invited" unambiguously, without depending on a font having a character.

            var label = new GameObject("Name").AddComponent<TextMeshPro>();
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition =
                new Vector3(0f, imageY - imageHeight * 0.5f - CaptionHeight * 0.55f, 0.01f);
            label.fontSize = 0.44f;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = true;
            label.rectTransform.sizeDelta = new Vector2(cardWidth, CaptionHeight);
            label.text = choice.Selected
                ? "<color=" + Gold + "><b>" + choice.Label + "</b></color>"
                : "<color=" + Ivory + "DD>" + choice.Label + "</color>";

            var interactable = Pointable(go, onPick);
            Highlight(interactable, frame.GetComponent<MeshRenderer>(), frameMat.GetColor("_BaseColor"));
        }

        /// <summary>Choice labels longer than this wrap onto two lines rather than shrink or ellipsise.</summary>
        const int WrapAbove = 48;

        void Plate(string label, bool selected, float plateWidth, Vector3 at, Action onPick,
                   bool enabled = true, float minFont = 0.54f, float pillHeight = 0.135f, float maxFont = 0.72f,
                   bool wrap = false)
        {
            var go = new GameObject("Plate " + label);
            go.transform.SetParent(_plates, false);
            go.transform.localPosition = at;

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(plateWidth, 0.22f, 0.04f);

            // Her `.primary-action` is a bordered pill. Ours was bare floating text, which is why
            // "LET AI CURATE" read as a caption rather than as the way forward.
            var pill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pill.name = "Pill";
            Destroy(pill.GetComponent<Collider>());
            pill.transform.SetParent(go.transform, false);
            pill.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            // The pill is WIDER than the text box it backs, not narrower. It was 0.86 of a text
            // rect of 1.0, so any label that filled its rect - "What should I keep, and what
            // should I let go?" - hung out over both ends of its own backdrop.
            pill.transform.localScale = new Vector3(plateWidth, pillHeight, 1f);
            var pillMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            pillMat.SetColor("_BaseColor",
                !enabled ? new Color(0.12f, 0.12f, 0.12f, 0.35f)
                : selected ? new Color(0.788f, 0.667f, 0.447f, 0.96f) // chosen: --gold, solid
                : new Color(0.165f, 0.129f, 0.094f, 0.78f));
            pillMat.SetFloat("_Surface", 1f);
            pillMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            pillMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            pillMat.SetFloat("_ZWrite", 0f);
            pillMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            pillMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 5;
            pill.GetComponent<MeshRenderer>().sharedMaterial = pillMat;

            var text = new GameObject("Label").AddComponent<TextMeshPro>();
            text.transform.SetParent(go.transform, false);
            text.fontSize = 0.60f;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.rectTransform.sizeDelta = new Vector2(plateWidth * 0.90f, wrap ? pillHeight : 0.22f);

            // Shrink to fit before ellipsising. "CHOOSE WHO WALKS WITH ME" became
            // "CHOOSE WHO WALKS WITH..." the moment the type grew, and a truncated label on a
            // button you are meant to aim at is worse than a slightly smaller one. The floor is
            // still above the legibility threshold measured for this distance.
            text.enableAutoSizing = true;
            text.fontSizeMax = maxFont;
            text.fontSizeMin = minFont;
            text.text = "<cspace=0.18em>"
                      // NO <b> on the selected label. Bold is wider, so autosizing shrank the
                      // chosen option - the one thing that should not get smaller when you pick it.
                      + (selected ? "<color=#241C12>" + label + "</color>"
                                  : enabled ? "<color=" + Ivory + "EE>" + label + "</color>"
                                            : "<color=" + Ivory + "59>" + label + "</color>")
                      + "</cspace>";

            // Pointable with the ray the rig already carries — no Canvas, no XRUIInputModule.
            var interactable = Pointable(go, onPick);
            Highlight(interactable, pill.GetComponent<MeshRenderer>(), pillMat.GetColor("_BaseColor"));
        }

        /// <summary>
        /// Light a control up while it is pointed at.
        ///
        /// Without it there is no way to tell what the ray is on before committing, which in a
        /// headset means aiming blind — the cursor is a thin line and the plates are flat.
        /// </summary>
        static void Highlight(XRSimpleInteractable interactable, MeshRenderer target, Color rest)
        {
            if (target == null) return;
            var hot = new Color(
                Mathf.Min(rest.r + 0.22f, 1f),
                Mathf.Min(rest.g + 0.20f, 1f),
                Mathf.Min(rest.b + 0.14f, 1f),
                Mathf.Min(rest.a + 0.12f, 1f));

            // Null-checked inside the callbacks: a plate is destroyed when the panel rebuilds, often
            // while the ray is still on it, and the hover-exit then arrives for a dead renderer.
            // Measured on Quest 27 Sep: 12 NullReferenceExceptions in the first minute.
            interactable.hoverEntered.AddListener(_ => { if (target != null) target.sharedMaterial.SetColor("_BaseColor", hot); });
            interactable.hoverExited.AddListener(_ => { if (target != null) target.sharedMaterial.SetColor("_BaseColor", rest); });
        }

        /// <summary>Pick by index, for the keyboard and stick paths that exist for the Editor.</summary>
        public void Pick(int index)
        {
            if (index < 0 || index >= ChoiceIds.Count) return;
            ChoiceTaken?.Invoke(ChoiceIds[index]);
        }

        /// <summary>Take the stage's forward action, whatever it is.</summary>
        public void TakeAction()
        {
            if (_panel == null || !_panel.ActionEnabled) return;

            // An action kept in a menu is reached through the menu, from the keyboard too: the
            // first Enter opens it, the second takes it. Otherwise one key would end the walk.
            if (_panel.ActionInMenu && !_menuOpen) { _menuOpen = true; Rebuild(); return; }
            _menuOpen = false;
            ActionTaken?.Invoke();
        }

        /// <summary>Step back a stage, where the stage offers it.</summary>
        public void TakeBack()
        {
            if (_panel != null && !string.IsNullOrEmpty(_panel.Back)) BackTaken?.Invoke();
        }
    }
}
