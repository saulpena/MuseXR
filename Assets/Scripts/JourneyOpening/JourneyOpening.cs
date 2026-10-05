using System.Collections;
using System.Collections.Generic;
using MusePico.Dialogue;
using MusePico.Journey;
using MuseXR.Interaction;
using MuseXR.Slots;
using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Journey
{
    /// <summary>
    /// The start of her journey as one walk: the Gate (stage 1) hands its question to the Company
    /// (stage 2). When the Gate's doors open, her six masters stand in a row across the path in front
    /// of them; the visitor points to invite one to three (Monet, Van Gogh and Socrates preselected, her
    /// demo preset), presses A, and the chosen step to the visitor's side and each answers the
    /// question in one line, in turn, live from the model, with her subtitle panel over the speaker.
    ///
    /// The Gate and the Company keep their own logic (GateStage, CompanyStage); this only joins them.
    /// </summary>
    public sealed class JourneyOpening : MonoBehaviour
    {
        public GateStage gate;
        public MuseumDialogue dialogue;
        [Tooltip("The seven masters in her row order: Monet, Van Gogh, Socrates, Frida, Picasso, Hilma, Morisot.")]
        public GameObject[] masterPrefabs = new GameObject[7];

        [Tooltip("How far down the walk from the Gate spawn the row stands. A Marble capture is sharp only within ~15 m of its centre; the doors are ~50 m out, in its fog.")]
        [System.NonSerialized] public float rowFromSpawn = 4.5f;   // inside the pointer's 8 m reach for every master; not serialized (a scene copy beat the code default)
        // A shallow arc around the spawn, 11 degrees apart (~0.85 m): six in a straight 6 m line stood in
        // the walk's walls at both ends (headset test, 3 Oct). Each faces the spawn.
        // Seven at 11 degrees span 66: wider and the end masters stood behind the walk's Pissarros. Their
        // name cards, wider with her labels, alternate in height instead so they never overlap (4 Oct).
        [System.NonSerialized] public float rowDegrees = 11f;

        public CompanyStage Company { get; private set; }
        public IReadOnlyList<string> Companions { get; private set; } = new string[0];

        readonly Dictionary<string, string> _lines = new Dictionary<string, string>();
        bool _asked, _answersReady;
        // Her explicit prompt at the row: what to do, in her kit (icon, words and the controller letter).
        GameObject _prompt;
        TextMeshProUGUI _promptTitle, _promptHint;
        // Who is who, and who is invited: a fixed name over each master and a ring at their feet.
        readonly Dictionary<string, (MeshRenderer ring, TextMeshProUGUI state)> _marks =
            new Dictionary<string, (MeshRenderer, TextMeshProUGUI)>();
        Material _ringIdle, _ringChosen, _ringHover;
        readonly HashSet<string> _hovered = new HashSet<string>();
        readonly HashSet<string> _introduced = new HashSet<string>();

        // Her four sample questions are answered ahead of time and shipped (Resources/OpeningAnswers.json),
        // so the masters speak at once; any other question is asked the moment the doors open, for all
        // six, while the visitor walks up and chooses - not after A.
        [System.Serializable] public class BakedLine { public string id, line; }
        [System.Serializable] public class BakedQuestion { public string question; public List<BakedLine> lines = new List<BakedLine>(); }
        [System.Serializable] public class BakedAnswers { public List<BakedQuestion> items = new List<BakedQuestion>(); }
        bool _asking;

        // Voices: each line is spoken in that master's MiniMax voice, from where the master stands.
        // Fetched ahead (when the company is chosen) so a turn starts speaking at once.
        readonly Dictionary<string, System.Threading.Tasks.Task<AudioClip>> _voices =
            new Dictionary<string, System.Threading.Tasks.Task<AudioClip>>();
        AudioSource _speaking;

        void Start()
        {
            if (gate == null) gate = FindAnyObjectByType<GateStage>();
            // Pointing needs a pointer and grip on each controller - the trigger selected nothing without it.
            if (FindAnyObjectByType<HandsBootstrap>() == null) gameObject.AddComponent<HandsBootstrap>();
            // Desktop testing in the Editor (no headset): WASD to walk through the rig's CharacterController,
            // Shift faster, hold the right mouse button to look; left click points (HandsBootstrap's mouse
            // hand), Enter is A, Backspace is B, X held speaks. DesktopMove switches itself off in a headset.
            // Her satchel on the left wrist, and the Gate's hero work to replicate into it.
            Satchel.Get();
            if (FindAnyObjectByType<GateHero>() == null) gameObject.AddComponent<GateHero>().gate = gate;
            if (FindAnyObjectByType<MuseXR.Worlds.DesktopMove>() == null)
            {
                var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (origin != null) origin.gameObject.AddComponent<MuseXR.Worlds.DesktopMove>();
            }
            if (gate != null) gate.Flow.PhaseChanged += OnGatePhase;
            HerLight();
            // Saul, 4 Oct: nothing to walk to and nothing to touch until the companions are chosen. Walking and
            // the Gate's works come with them; the works fade in rather than pop.
            Walking(false);
            StartCoroutine(HideGateWorks());
        }

        bool _revealed;

        /// <summary>Walking on or off: the rig's continuous move (a turn still turns) and the desk's WASD.</summary>
        static void Walking(bool on)
        {
            MuseXR.Worlds.DesktopMove.Suspended = !on;
            foreach (var m in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                m.enabled = on;
        }

        static IEnumerable<GameObject> GateWorks()
        {
            foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (r.name == "Gate Paintings") yield return r;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "Hero · Mona Lisa" || t.name == "Hero · Venus de Milo") yield return t.gameObject;
        }

        /// <summary>The hung works and the heroes are built over the first frames: keep them hidden until revealed.</summary>
        IEnumerator HideGateWorks()
        {
            for (var f = 0; f < 120 && !_revealed; f++)
            {
                foreach (var go in GateWorks()) if (go.activeSelf) go.SetActive(false);
                yield return null;
            }
        }

        void RevealGateWorks()
        {
            if (_revealed) return;
            _revealed = true;
            Walking(true);
            foreach (var go in new List<GameObject>(GateWorks())) FadeIn.Reveal(go, 1.6f);
        }

        /// <summary>
        /// Her hemisphere light, converted (CLAUDE.md: three.js Hemisphere 2.25 -> Trilight 1.3, sky #f7fbff,
        /// ground #76644e). With Skybox ambient a figure facing away from the sun was a silhouette - Monet
        /// read black in the Palace once the world turned under the light.
        /// </summary>
        static void HerLight()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color32(0xf7, 0xfb, 0xff, 0xff);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color32(0xf7, 0xfb, 0xff, 0xff), new Color32(0x76, 0x64, 0x4e, 0xff), 0.5f);
            RenderSettings.ambientGroundColor = new Color32(0x76, 0x64, 0x4e, 0xff);
            RenderSettings.ambientIntensity = 1.3f;
        }

        void OnDestroy()
        {
            if (gate != null) gate.Flow.PhaseChanged -= OnGatePhase;
        }

        void OnGatePhase(GateFlow.Phase phase)
        {
            if (phase == GateFlow.Phase.DoorsOpen && Company == null) BuildCompany();
        }

        void BuildCompany()
        {
            var eye = gate.eye != null ? gate.eye : Camera.main.transform;
            var doorway = gate.Doorway;
            var from = gate.spawn != null ? gate.spawn.position : eye.position;
            var toDoor = doorway - from; toDoor.y = 0f; toDoor.Normalize();
            var toVisitor = -toDoor;
            var centre = from + toDoor * rowFromSpawn;
            centre.y = from.y;
            var facing = Quaternion.LookRotation(toVisitor, Vector3.up);   // a figure's front (+Z) toward the visitor
            var right = Vector3.Cross(Vector3.up, toVisitor).normalized;

            var root = new GameObject("Company").transform;
            root.SetParent(transform, false);
            root.SetPositionAndRotation(centre, facing);
            var standees = new Dictionary<string, Transform>();
            for (var i = 0; i < Masters.Row.Count; i++)
            {
                var id = Masters.Row[i];
                var slot = new GameObject("Standee " + id).transform;
                slot.SetParent(root, false);
                var dir = Quaternion.Euler(0f, (i - (Masters.Row.Count - 1) * 0.5f) * rowDegrees, 0f) * toDoor;
                var at = from + dir * rowFromSpawn; at.y = from.y;
                slot.SetPositionAndRotation(at, Quaternion.LookRotation(-dir, Vector3.up));
                var prefab = i < masterPrefabs.Length ? masterPrefabs[i] : null;
                if (prefab != null)
                {
                    var figure = Instantiate(prefab, slot);
                    figure.transform.localPosition = Vector3.zero; figure.transform.localRotation = Quaternion.identity;
                }
                // Pointable needs a collider the size of a standing figure.
                var box = slot.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.9f, 0f); box.size = new Vector3(0.6f, 1.8f, 0.4f);
                standees[id] = slot;
                BuildMark(id, slot, -dir);
            }

            Company = CompanyStage.Make(root.gameObject, standees);
            Company.Question = gate.Flow.Question;
            Company.Group.Head = eye;
            Company.Group.LineFor = id => _lines.TryGetValue(id, out var l) ? l : Masters.Name(id) + " considers your question.";
            Company.ReadyToAnswer = () => _answersReady;
            Company.PhaseChanged += OnCompanyPhase;
            Company.Completed += ids => Companions = ids;
            // The compass's first target: the row of masters to choose from.
            var rowTarget = CompassTarget.Add(root.gameObject, 5, "Choose your companions", "Point and pull the trigger");
            Company.PhaseChanged += ph => { if (ph != CompanyStage.Phase.Choosing) rowTarget.MarkDone(); };   // chosen: on to the next
            root.gameObject.AddComponent<SubtitleRig>().Group = Company.Group;
            // No preset: with Monet, Van Gogh and Socrates preselected a single A chose for the visitor
            // (headset test). Her demo preset is for the 3-minute demo route, not this walk.
            Company.Group.FollowVisitor = false;   // placed once beside the visitor, then they stand still
            // With a voice the clip times each turn; without one, reading time does (EstimateSeconds).
            Company.Group.TimeLinesByLength = dialogue == null || !dialogue.HasVoice;
            Company.Group.LineStarted += (id, line) => StartCoroutine(SpeakTurn(id, line));
            AddFill(eye);
            if (!UseBaked(Company.Question)) _ = AskAll(Company.Question);
            gate.HidePrompt();   // the Gate is answered; its "hold X to speak" must not linger
            BuildPrompt(centre, facing, toVisitor);
            foreach (var kv in standees)
            {
                var pointable = kv.Value.GetComponent<Pointable>();
                if (pointable == null) continue;
                var id = kv.Key;
                pointable.Hovering += _ =>
                {
                    _hovered.Add(id); RefreshMarks();
                    // Her "point at one to hear a one-line introduction", while choosing (Saul, 4 Oct): only a master
                    // not yet chosen, after a deliberate point (the ray resting 0.4 s), and pointing at another stops
                    // this one - voice and card - at once.
                    if (Company.Current == CompanyStage.Phase.Choosing && id != _introId)
                    {
                        StopIntro();
                        if (!Company.Invitation.IsChosen(id)) { _introId = id; _intro = StartCoroutine(IntroAfterDwell(id)); }
                    }
                };
                pointable.Unhovered += _ => { _hovered.Remove(id); RefreshMarks(); };
            }
            Company.Toggled += (id, r) =>
            {
                if (id == _introId) StopIntro();
                RefreshMarks();
                RefreshPrompt(r == Invitation.Result.Refused ? "Three is the most. Point at one you have invited to release them first." : null);
            };
            RefreshPrompt();
            Debug.Log("[Opening] the Company stands on the walk; the question is: " + Company.Question);
        }

        void BuildMark(string id, Transform slot, Vector3 toVisitor)
        {
            if (_ringIdle == null)
            {
                _ringIdle = new Material(Shader.Find("Universal Render Pipeline/Unlit")); _ringIdle.SetColor("_BaseColor", MuseTheme.Ink3);
                _ringChosen = new Material(Shader.Find("Universal Render Pipeline/Unlit")); _ringChosen.SetColor("_BaseColor", MuseTheme.Gold);
                _ringHover = new Material(Shader.Find("Universal Render Pipeline/Unlit")); _ringHover.SetColor("_BaseColor", MuseTheme.Rose);
                _ringIdle.SetFloat("_Cull", 0f); _ringChosen.SetFloat("_Cull", 0f); _ringHover.SetFloat("_Cull", 0f);   // reads from above whatever the winding
            }
            // The ring on the floor: thin grey while waiting, wide gold once invited (shape and colour).
            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(slot, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            ringGo.AddComponent<MeshFilter>().sharedMesh = Annulus(0.36f, 0.42f);
            var ring = ringGo.AddComponent<MeshRenderer>(); ring.sharedMaterial = _ringIdle;

            // The name and the state, fixed in the world, set once toward where the visitor stands.
            var tag = new GameObject("Name").transform;
            tag.SetParent(slot, false);
            var index = 0; for (var k = 0; k < Masters.Row.Count; k++) if (Masters.Row[k] == id) index = k;
            tag.position = slot.position + Vector3.up * (index % 2 == 0 ? 2.05f : 2.4f);
            tag.rotation = Quaternion.LookRotation(-toVisitor, Vector3.up);   // +Z away from the viewer reads
            var c = MuseUi.Canvas(tag, "Name", rowFromSpawn, 128f);   // wide enough for her longest label
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 10f, padY: 6f, gap: 2f, name: "Name Card");
            card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var name = MuseUi.Text(card, Masters.Name(id), MuseUi.Face.SansSemi, 12.5f, MuseTheme.Ink, name: "Master");
            name.alignment = TextAlignmentOptions.Center; name.enableWordWrapping = false;
            // Her picker label under the name: "Monet · how light changes".
            var tagline = MuseUi.Text(card, Masters.Tagline(id), MuseUi.Face.Sans, 9.5f, MuseTheme.Ink2, name: "Tagline");
            tagline.alignment = TextAlignmentOptions.Center; tagline.enableWordWrapping = false; tagline.fontStyle = FontStyles.Italic;
            var state = MuseUi.Text(card, "Point to invite", MuseUi.Face.Sans, 10f, MuseTheme.Ink3, name: "State");
            state.alignment = TextAlignmentOptions.Center; state.enableWordWrapping = false;
            _marks[id] = (ring, state);
        }

        const float PromptAhead = 3f;

        void RefreshMarks()
        {
            if (Company == null) return;
            foreach (var kv in _marks)
            {
                bool chosen = Company.Invitation.IsChosen(kv.Key);
                bool hover = _hovered.Contains(kv.Key);
                // Hover: her rose (the "aligned" colour) and a thicker ring; chosen: gold and wide.
                kv.Value.ring.sharedMaterial = hover ? _ringHover : chosen ? _ringChosen : _ringIdle;
                kv.Value.ring.GetComponent<MeshFilter>().sharedMesh = chosen ? Annulus(0.3f, 0.44f) : hover ? Annulus(0.33f, 0.44f) : Annulus(0.36f, 0.42f);
                kv.Value.state.text = hover ? (chosen ? "Pull the trigger to release" : "Pull the trigger to invite") : chosen ? "Invited" : "Point to invite";
                kv.Value.state.color = hover ? MuseTheme.Rose : chosen ? MuseTheme.GoldInk : MuseTheme.Ink3;
                kv.Value.state.fontStyle = chosen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        static Mesh Annulus(float inner, float outer)
        {
            const int n = 48;
            var v = new Vector3[(n + 1) * 2]; var t = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v[i * 2] = new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner);
                v[i * 2 + 1] = new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer);
                if (i < n) { int k = i * 2, j = i * 6; t[j] = k; t[j + 1] = k + 1; t[j + 2] = k + 2; t[j + 3] = k + 1; t[j + 4] = k + 3; t[j + 5] = k + 2; }
            }
            var m = new Mesh { vertices = v, triangles = t }; m.RecalculateBounds(); return m;
        }

        void BuildPrompt(Vector3 centre, Quaternion facing, Vector3 toVisitor)
        {
            var anchor = new GameObject("Company Prompt").transform;
            anchor.SetParent(transform, false);
            // Near the visitor, where the Gate's prompt stood: 3 m ahead and to the right of the walk, at eye
            // height, read at arm's reach of the walk. By the row it had to be ~5 m wide to be read from
            // the spawn and covered the name cards. Facing the visitor: +Z away from them.
            var spawn = gate.spawn != null ? gate.spawn.position : centre + toVisitor * rowFromSpawn;
            var toRow = -toVisitor;
            var visitorRight = Vector3.Cross(Vector3.up, toRow).normalized;
            // Centred over the row, above the name cards and above eye level: instructions stand above the
            // view (Saul, 3 Oct). At 40 degrees right it hung half off the edge of the view (4 Oct).
            var at = spawn + toRow * rowFromSpawn + Vector3.up * 3.3f;   // clear of the name cards, which now stand at two heights
            var toEye = spawn - at; toEye.y = 0f;
            anchor.SetPositionAndRotation(at, Quaternion.LookRotation(-toEye.normalized, Vector3.up));
            // Her web dark glass (as the Gate's question panel): cream ink, lavender eyebrow, Gilda title.
            var cream = new Color32(238, 233, 223, 255); var cream2 = new Color32(238, 233, 223, 170);
            var accent = new Color32(158, 135, 170, 255);
            var c = MuseUi.Canvas(anchor, "Prompt", rowFromSpawn, 520f);
            var glass = MuseUi.Card(c, new Color32(8, 6, 10, 196), 0f, new Color32(238, 233, 223, 46), 1f, padX: 26f, padY: 20f, gap: 8f, name: "Glass");
            MuseUi.Text(glass, "02 / Invite companions", MuseUi.Face.Sans, 10f, accent, 0.28f, true, name: "Eyebrow");
            _promptTitle = MuseUi.Text(glass, "Choose up to three", MuseUi.Face.Serif, 30f, cream, lineHeight: 1.1f, name: "Title");
            var fonts = MuseFonts.Get();
            if (fonts != null && fonts.display != null) _promptTitle.font = fonts.display;
            _promptHint = MuseUi.Text(glass, "", MuseUi.Face.Sans, 13f, cream2, name: "Body");
            var pill = MuseUi.Card(glass, new Color32(56, 48, 61, 160), 18f, new Color32(238, 233, 223, 107), 1f, padX: 16f, padY: 9f, name: "Continue");
            pill.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0f;
            glass.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childForceExpandWidth = false;
            MuseUi.Text(pill, "A  \u00b7  Continue", MuseUi.Face.Sans, 10f, cream, 0.2f, true, name: "Label").enableWordWrapping = false;
            _prompt = anchor.gameObject;
        }

        void RefreshPrompt(string note = null)
        {
            if (_prompt == null || Company == null) return;
            switch (Company.Current)
            {
                case CompanyStage.Phase.Choosing:
                    int n = Company.Invitation.SpeakingOrder().Count;
                    _promptTitle.text = "Choose up to three  ·  " + n + " chosen";
                    _promptHint.text = note ?? "Point at a master and pull the trigger to invite or release them. Press A when you are ready.";
                    break;
                case CompanyStage.Phase.Stepping:
                case CompanyStage.Phase.Answering:
                    _promptTitle.text = "Your companions answer";
                    _promptHint.text = _answersReady ? "Each answers your question in turn. A moves to the next." : "They are thinking about your question…";
                    break;
                case CompanyStage.Phase.Done:
                    break;   // the curation card takes over the walk; this prompt is retired (Update hides it)
            }
        }

        void Update()
        {
            // The prompt follows the stage; it hides while a companion is speaking, so the subtitle is the
            // only thing to read.
            if (_prompt == null || Company == null) return;
            bool hidden = Company.Current == CompanyStage.Phase.Done
                          || (Company.Current == CompanyStage.Phase.Answering && _answersReady);
            if (_prompt.activeSelf == hidden) _prompt.SetActive(!hidden);
        }

        async void OnCompanyPhase(CompanyStage.Phase phase)
        {
            RefreshPrompt();
            // Once chosen they walk with the visitor as a crowd: beside, never in front (Saul, 3 Oct).
            if (phase == CompanyStage.Phase.Answering) Company.Group.Crowd = true;
            if (phase != CompanyStage.Phase.Choosing) RevealGateWorks();   // the company is confirmed: walking and the works
            if (phase == CompanyStage.Phase.Stepping)
                foreach (var kv in _marks) { kv.Value.ring.gameObject.SetActive(false); kv.Value.state.transform.parent.parent.parent.gameObject.SetActive(false); }
            if (phase != CompanyStage.Phase.Stepping || _asked) return;
            _asked = true;
            // Asked when the doors opened (or baked): usually ready already. If a chosen master's line is
            // missing (the call failed), the stock line stands in rather than leaving the visitor waiting.
            if (!_asking) _answersReady = true;
            foreach (var id in Company.Invitation.SpeakingOrder())
                if (_lines.TryGetValue(id, out var l)) VoiceFor(id, l);   // fetch ahead while they step over
            await System.Threading.Tasks.Task.Yield();
            RefreshPrompt();
        }

        /// <summary>The museum's camera-mounted fill (MuseumJourney: point 2.0, her Point 2.4 converted), so a
        /// master standing against the sun is a face, not a silhouette. Meshes only; splats are unlit.</summary>
        static void AddFill(Transform eye)
        {
            if (eye.GetComponentInChildren<Light>() != null) return;
            var l = new GameObject("Companion Fill").AddComponent<Light>();
            l.transform.SetParent(eye, false);
            l.type = LightType.Point; l.intensity = 2.0f; l.range = 9f; l.color = Color.white; l.shadows = LightShadows.None;
        }

        /// <summary>The figure standing for a company member, or null.</summary>
        public Transform FigureOf(string id) =>
            Company != null && Company.Group.Figures.TryGetValue(id, out var f) ? f : null;

        /// <summary>A line in <paramref name="id"/>'s voice, fetched once and cached.</summary>
        public System.Threading.Tasks.Task<AudioClip> VoiceFor(string id, string text)
        {
            if (dialogue == null || !dialogue.HasVoice || string.IsNullOrWhiteSpace(text))
                return System.Threading.Tasks.Task.FromResult<AudioClip>(null);
            var key = id + "|" + text;
            if (!_voices.TryGetValue(key, out var t)) _voices[key] = t = dialogue.VoiceAsync(RosterId(id), text);
            return t;
        }

        /// <summary>
        /// Speak <paramref name="text"/> in <paramref name="id"/>'s voice from their figure (a 3D source,
        /// so the voice comes from the master). Stops whoever was speaking. Yields until the line is done;
        /// returns at once when there is no voice.
        /// </summary>
        public System.Collections.IEnumerator Say(string id, string text, string kicker = "Why this room fits your question")
        {
            // Outside the answer turns (a lantern's line) the words go up at once: waiting for the voice
            // left the panel empty for the 2-3 s a clip takes to come back.
            // The answer round is over once the company is Done (its finished turns object stays set).
            var outsideTurns = Company != null && (Company.Group.Turns == null || Company.Current == CompanyStage.Phase.Done);
            if (outsideTurns)
            {
                Company.Group.ActiveSpeaker = id;   // ring and heads too
                var panel = TorsoPanel.Get();
                if (panel != null) panel.ShowLine(id, Masters.Name(id), text, null, kicker);
            }
            var task = VoiceFor(id, text);
            for (float t = 0f; !task.IsCompleted && t < 10f; t += Time.deltaTime) yield return null;
            var clip = task.IsCompleted && !task.IsFaulted ? task.Result : null;
            var figure = FigureOf(id);
            if (clip != null && figure != null)
            {
                if (_speaking != null) _speaking.Stop();
                var source = figure.GetComponent<AudioSource>();
                if (source == null)
                {
                    source = figure.gameObject.AddComponent<AudioSource>();
                    source.spatialBlend = 0.85f; source.minDistance = 2f; source.maxDistance = 25f;
                    source.rolloffMode = AudioRolloffMode.Linear; source.playOnAwake = false;
                }
                source.clip = clip; source.Play();
                _speaking = source;
                for (float t = 0f; t < clip.length + 0.3f && source != null && source.isPlaying; t += Time.deltaTime) yield return null;
            }
            else if (outsideTurns)
            {
                // No voice: leave the words up for their reading time.
                for (float t = 0f; t < CompanionGroup.EstimateSeconds(text); t += Time.deltaTime) yield return null;
            }
            if (outsideTurns && Company != null && Company.Group.ActiveSpeaker == id)
            {
                Company.Group.ActiveSpeaker = null;
                var panel = TorsoPanel.Get();
                if (panel != null) panel.ClearLine();
            }
        }

        string _introId;
        Coroutine _intro;

        System.Collections.IEnumerator IntroAfterDwell(string id)
        {
            yield return new WaitForSeconds(0.4f);
            if (!_hovered.Contains(id) || Company == null || Company.Current != CompanyStage.Phase.Choosing) { _introId = null; yield break; }
            var intro = Masters.Intro(id);
            if (!string.IsNullOrEmpty(intro)) yield return Say(id, intro, "Meet your companion  ·  " + Masters.Tagline(id));
            if (_introId == id) _introId = null;
        }

        /// <summary>The intro playing now, voice and card, stops: the visitor pointed at someone else (or chose).</summary>
        void StopIntro()
        {
            if (_intro != null) StopCoroutine(_intro);
            _intro = null;
            if (_introId == null) return;
            if (_speaking != null) _speaking.Stop();
            if (Company != null && Company.Group.ActiveSpeaker == _introId) Company.Group.ActiveSpeaker = null;
            var panel = TorsoPanel.Existing;
            if (panel != null) panel.ClearLine();
            _introId = null;
        }

        System.Collections.IEnumerator SpeakTurn(string id, string line)
        {
            if (Company == null || Company.Group.TimeLinesByLength) yield break;
            var started = Time.time;
            yield return Say(id, line);
            // No clip came back: hold the line for its reading time instead.
            var voiced = VoiceFor(id, line);
            if (!voiced.IsCompleted || voiced.IsFaulted || voiced.Result == null)
                while (Time.time - started < CompanionGroup.EstimateSeconds(line)) yield return null;
            var turns = Company != null ? Company.Group.Turns : null;
            if (turns != null && turns.Speaker == id && turns.Current == TurnTaking.Phase.Speaking) Company.Group.LineFinished();
        }

        bool UseBaked(string question)
        {
            var file = Resources.Load<TextAsset>("OpeningAnswers");
            if (file == null || string.IsNullOrWhiteSpace(question)) return false;
            var baked = JsonUtility.FromJson<BakedAnswers>(file.text);
            foreach (var q in baked.items)
                if (string.Equals(q.question.Trim(), question.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var l in q.lines) _lines[l.id] = l.line;
                    // Her script writes lines for her trio only: the other masters are asked live.
                    foreach (var id in Masters.Row) if (!_lines.ContainsKey(id)) { Debug.Log("[Opening] baked answers for her trio; asking the rest live: " + question); return false; }
                    _answersReady = true;
                    Debug.Log("[Opening] baked answers for: " + question);
                    return true;
                }
            return false;
        }

        /// <summary>Ask all six masters at once, one line each; returns their lines by Company id.</summary>
        public async System.Threading.Tasks.Task<Dictionary<string, string>> AskAll(string question)
        {
            var found = new Dictionary<string, string>();
            if (dialogue == null) { _answersReady = true; return found; }
            _asking = true;
            dialogue.invitedMasterIds = new List<string>();
            foreach (var id in Masters.Row) dialogue.invitedMasterIds.Add(RosterId(id));
            dialogue.exactlyInvited = true;
            dialogue.speakReplies = false;   // the turns are paced by the Company; the subtitle carries each line
            if (string.IsNullOrWhiteSpace(question)) question = GateFlow.Samples[0];
            Debug.Log("[Opening] asking all " + Masters.Row.Count + ": " + question);
            // The perspective prompt grounds every reading in a named work; at the Gate there is no painting,
            // so the context is the place itself (otherwise its default, Water Lilies, coloured every answer).
            var (t, a, d) = (dialogue.artworkTitle, dialogue.artworkArtist, dialogue.artworkDate);
            dialogue.artworkTitle = GateContextTitle; dialogue.artworkArtist = "the visitor, at the start of the journey"; dialogue.artworkDate = "now";
            DialogueResult result = null;
            try { result = await dialogue.AskAsync(question); }
            catch (System.Exception ex) { Debug.LogWarning("[Opening] the masters could not be asked: " + ex.Message); }
            finally { dialogue.artworkTitle = t; dialogue.artworkArtist = a; dialogue.artworkDate = d; }
            if (result != null)
                foreach (var p in result.Perspectives)
                    foreach (var id in Masters.Row)
                        if (RosterId(id) == p.speakerId && !_lines.ContainsKey(id)) { _lines[id] = OneLine(p.text, question); found[id] = _lines[id]; }   // her scripted lines stand
            Debug.Log("[Opening] answers ready (" + found.Count + " of " + Masters.Row.Count + ", live " + (result != null && result.Live) + ")");
            _asking = false;
            _answersReady = true;
            RefreshPrompt();
            return found;
        }

        public const string GateContextTitle =
            "the Gate of the museum: a glasshouse garden walk between still pools, the visitor's question lettered " +
            "like an exhibition title above the pavilion arch, its doors just opened";

        /// <summary>Her "one line": the first sentence of the master's reading.</summary>
        public static string OneLine(string text) => OneLine(text, null);

        /// <summary>
        /// Her "one line": the reading's opening sentence - skipping one that only repeats the visitor's
        /// question (Socrates opens that way) and taking a second when the first is too short to say much.
        /// </summary>
        public static string OneLine(string text, string question)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var sentences = new List<string>();
            int start = 0; text = text.Trim();
            for (var i = 0; i < text.Length; i++)
                if ((text[i] == '.' || text[i] == '?' || text[i] == '!') && (i + 1 == text.Length || text[i + 1] == ' '))
                { sentences.Add(text.Substring(start, i + 1 - start).Trim()); start = i + 1; }
            if (start < text.Length) sentences.Add(text.Substring(start).Trim());
            string Bare(string x) => new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where((x ?? "").ToLowerInvariant(), char.IsLetterOrDigit)));
            if (question != null && sentences.Count > 1 && Bare(sentences[0]) == Bare(question)) sentences.RemoveAt(0);
            var line = sentences.Count > 0 ? sentences[0] : text;
            if (line.Length < 40 && sentences.Count > 1) line += " " + sentences[1];
            return line;
        }

        /// <summary>The rigged figure for a master id (her row order: Monet, Van Gogh, Socrates, Frida, Hilma, Morisot).</summary>
        public GameObject PrefabFor(string id)
        {
            for (var i = 0; i < Masters.Row.Count && i < masterPrefabs.Length; i++) if (Masters.Row[i] == id) return masterPrefabs[i];
            return null;
        }

        /// <summary>The Company's ids are the Slots spellings; masters.json uses short ones.</summary>
        public static string RosterId(string id) => id switch
        {
            Masters.Frida => "frida", Masters.Hilma => "hilma", Masters.Morisot => "morisot", _ => id,
        };
    }
}
