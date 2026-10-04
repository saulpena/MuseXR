using System.Collections;
using System.Collections.Generic;
using GaussianSplatting.Runtime;
using MusePico.Dialogue;
using MusePico.Generation;
using MusePico.Journey;
using MusePico.Tripo;
using MuseXR.Interaction;
using MuseXR.Slots;
using MuseXR.UI;
using MuseXR.Worlds;
using TMPro;
using UnityEngine;

namespace MuseXR.Journey
{
    /// <summary>
    /// Her stage 3, Curation unfolds, and the way into stage 4 (the Palace):
    ///
    ///   - when the companions have answered, four chapter lanterns light one after another down the
    ///     walk, in her chapter order: Palace, Grotto, Van Gogh, Monet - the path preview;
    ///   - pointing at a lantern shows one line on why that chapter answers the visitor's question
    ///     (asked once, when the question is set, baked for her four samples; a failed call shows a
    ///     written line, labelled as the fallback);
    ///   - then the first lantern becomes the moon gate: the Palace world shows through its round
    ///     opening (SplatPortalDoor), the visitor walks through, and the Palace chapter takes over
    ///     with the companions they chose.
    /// </summary>
    public sealed class JourneyCuration : MonoBehaviour, IConfirmable
    {
        public JourneyOpening opening;
        public GateStage gate;
        [Tooltip("Assets/Prefabs/Moon Gate.prefab - the shared chapter transition (Docs/MOON-GATE.md)")]
        public MoonGate moonGatePrefab;
        [Tooltip("The generated garden lantern (Assets/Art/Props/lantern.glb); primitives stand in without it.")]
        public GameObject lanternModel;
        [Tooltip("Assets/Art/Doors/moon-gate.glb - her round moon gate")]
        public GameObject moonGateModel;
        [Tooltip("Everything of the Palace: its splat world, its chapter layout, its interactions. Inactive until the gate.")]
        public Transform palaceFrame;
        public GaussianSplatRenderer palaceWorld;
        public GaussianSplatRenderer conservatoryWorld;
        [Tooltip("The Palace's chapter content, switched on once the visitor is through.")]
        public GameObject[] palaceContent;
        [Tooltip("Things of the Gate stage that go once the visitor is through (the Gate, its floor).")]
        public GameObject[] gateLeftovers;

        public static readonly string[] Chapters = { "Palace", "Grotto", "Van Gogh", "Monet" };
        static readonly string[] Keys = { "palace", "grotto", "vangogh", "monet" };
        static readonly string[] Subtitles = { "Court of Keeping", "Hall of Time", "Studio of the Burning Sky", "Garden of Water and Light" };

        /// <summary>Her local fallback: written lines that fit any question, always labelled as fallback.</summary>
        static readonly string[] Fallback =
        {
            "The Palace keeps what an empire chose to hold on to - a room for asking what deserves keeping.",
            "The Grotto carves its Buddhas to outlast their makers - a room for asking what lasts.",
            "Van Gogh's studio burns with what he could not leave unsaid - a room for asking what presses on you.",
            "Monet's garden changes by the hour - a room for asking what remains when the light moves on.",
        };

        // Inside the pointer's 8 m reach from the spawn, all four (at 6.5 + 2.6 m steps three were beyond it,
        // so pointing at them did nothing - headset test).
        [System.NonSerialized] public float firstLantern = 3.6f, lanternStep = 1.3f;   // metres down the walk from the spawn
        static readonly float[] LanternOffsets = { 0f, -1.7f, 1.7f, -1.7f };   // metres across the walk
        const float ModelOpeningCentre = 2.02f;   // measured on the generated model (Docs/Doors/moon-gate.measure.txt)
        const float ModelOpeningRadius = 1.275f;

        readonly string[] _lines = new string[4];
        bool _fallback = true, _begun, _crossed;
        readonly List<Transform> _lanterns = new List<Transform>();
        readonly List<Light> _lights = new List<Light>();
        GameObject _card;
        TextMeshProUGUI _cardKicker, _cardLine, _cardNote;
        MoonGate _gate;
        // Her 2.3 has no timer: the visitor points at lanterns to hear them, and the first becomes the gate
        // when they choose to walk on (A) - an 8 s timer opened it before anyone had pointed at anything.
        readonly HashSet<int> _heard = new HashSet<int>();
        bool _awaitingWalkOn;
        Vector3 _toDoor;
        Transform _eye;

        [System.Serializable] public class BakedLantern { public string question; public List<string> lines = new List<string>(); }
        [System.Serializable] public class BakedLanterns { public List<BakedLantern> items = new List<BakedLantern>(); }

        void Start()
        {
            if (opening == null) opening = FindAnyObjectByType<JourneyOpening>();
            if (gate == null) gate = FindAnyObjectByType<GateStage>();
            for (var i = 0; i < 4; i++) _lines[i] = Fallback[i];
            if (palaceFrame != null) palaceFrame.gameObject.SetActive(false);
            if (gate != null) gate.Flow.PhaseChanged += p => { if (p == GateFlow.Phase.DoorsOpen) PrepareLines(gate.Flow.Question); };
        }

        void Update()
        {
            if (!_begun && opening != null && opening.Company != null && opening.Company.Current == CompanyStage.Phase.Done)
            {
                _begun = true;
                StartCoroutine(Unfold());
            }

        }

        // ---- lines ----------------------------------------------------------------------------

        async void PrepareLines(string question)
        {
            if (UseBaked(question)) return;
            var lines = await AskLanterns(question);
            if (lines != null) { for (var i = 0; i < 4; i++) _lines[i] = lines[i]; _fallback = false; }
        }

        bool UseBaked(string question)
        {
            var file = Resources.Load<TextAsset>("LanternLines");
            if (file == null || string.IsNullOrWhiteSpace(question)) return false;
            foreach (var q in JsonUtility.FromJson<BakedLanterns>(file.text).items)
                if (q.lines.Count == 4 && string.Equals(q.question.Trim(), question.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    for (var i = 0; i < 4; i++) _lines[i] = q.lines[i];
                    _fallback = false;
                    return true;
                }
            return false;
        }

        [System.Serializable] class LanternReply { public string palace, grotto, vangogh, monet; }

        /// <summary>One call: a line per chapter on why it answers the question. Null when it fails.</summary>
        public static async System.Threading.Tasks.Task<string[]> AskLanterns(string question)
        {
            try
            {
                var key = await FallbackKeySource.ForOpenAi().GetKeyAsync();
                if (string.IsNullOrEmpty(key)) return null;
                var call = new ResponsesCall(new TripoWebRequestTransport(key, ResponsesCall.DefaultEndpoint));
                var props = new JsonBuilder();
                foreach (var k in Keys) props.Add(k, new JsonBuilder().Add("type", "string"));
                var schema = new JsonBuilder().Add("type", "object").Add("properties", props)
                    .AddStringArray("required", Keys).Add("additionalProperties", false);
                const string instructions =
                    "You are the curator of an imaginary museum of four rooms, walked in this order: " +
                    "the Palace (a Forbidden City court of keeping: a throne hall, a bronze crane and turtle), " +
                    "the Grotto (cliff Buddhas, carved reliefs, a lamp to look by), " +
                    "Van Gogh's studio (a burning sky, the visitor draws one stroke), " +
                    "Monet's garden (water lilies, light changing with the hour). " +
                    "For each room write ONE sentence, under 22 words, telling the visitor why that room speaks to " +
                    "their question. Speak to the visitor as 'you'. Concrete, warm, no quotation marks, no hedging.";
                var text = await call.SendAsync(instructions, "Visitor question: " + question,
                    ResponsesCall.TextFormat("lantern_lines", schema), raw =>
                    {
                        try { var r = JsonUtility.FromJson<LanternReply>(raw); return r != null && !string.IsNullOrWhiteSpace(r.palace) ? null : "empty"; }
                        catch (System.Exception ex) { return ex.Message; }
                    });
                var reply = JsonUtility.FromJson<LanternReply>(text);
                return new[] { reply.palace, reply.grotto, reply.vangogh, reply.monet };
            }
            catch (System.Exception ex) { Debug.LogWarning("[Curation] lantern lines failed: " + ex.Message); return null; }
        }

        // ---- the path preview -----------------------------------------------------------------------

        IEnumerator Unfold()
        {
            _eye = gate.eye != null ? gate.eye : Camera.main.transform;
            var from = gate.spawn.position;
            var toDoor = gate.Doorway - from; toDoor.y = 0f; toDoor.Normalize();
            var face = Quaternion.LookRotation(-toDoor, Vector3.up);   // a lantern's front toward the visitor
            WalkAlongside();
            for (var i = 0; i < 4; i++) { var sp = SpeakerFor(i); if (sp != null) opening.VoiceFor(sp, _lines[i]); }   // fetch ahead
            var side = Vector3.Cross(Vector3.up, toDoor).normalized;
            var warm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            warm.SetColor("_BaseColor", new Color(0.35f, 0.3f, 0.24f));
            var wood = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            wood.SetColor("_BaseColor", new Color(0.32f, 0.22f, 0.14f));

            for (var i = 0; i < 4; i++)
            {
                // The first stands on the walk (it becomes the gate); the rest step out to alternate sides, so
                // from the spawn all four are seen at once instead of hiding behind one another.
                var at = from + toDoor * (firstLantern + lanternStep * i) + side * LanternOffsets[i];
                var lantern = new GameObject("Lantern " + Chapters[i]).transform;
                lantern.SetParent(transform, false);
                lantern.SetPositionAndRotation(at, face);
                Transform body;
                if (lanternModel != null)
                {
                    var model = Instantiate(lanternModel, lantern);
                    model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
                    FitHeight(model.transform, LanternHeight);
                    body = model.transform;
                }
                else
                {
                    Part(lantern, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.06f, 0.6f, 0.06f), wood);       // post
                    body = Part(lantern, PrimitiveType.Sphere, new Vector3(0f, 1.42f, 0f), new Vector3(0.42f, 0.52f, 0.42f), new Material(warm));
                    Part(lantern, PrimitiveType.Cylinder, new Vector3(0f, 1.71f, 0f), new Vector3(0.2f, 0.03f, 0.2f), wood);       // cap
                }
                var light = new GameObject("Glow").AddComponent<Light>();
                light.transform.SetParent(lantern, false); light.transform.localPosition = new Vector3(0f, 1.42f, 0f);
                light.type = LightType.Point; light.range = 3.5f; light.intensity = 0f; light.color = new Color(1f, 0.78f, 0.45f);
                var box = lantern.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 1.1f, 0f); box.size = new Vector3(1.2f, 2.2f, 0.8f);   // the generated lantern hangs off its post
                var index = i;
                var p = Pointable.Make(lantern.gameObject, Keys[i]);
                p.Selected += (_, __) => Hear(index);
                p.Hovering += _ => light.intensity = 3.2f;     // brightens under the laser
                p.Unhovered += _ => light.intensity = 1.6f;
                Tag(lantern, Chapters[i], Subtitles[i]);
                _lanterns.Add(lantern); _lights.Add(light);
                // It lights: the paper warms and its glow comes up.
                for (float t = 0f; t < 0.7f; t += Time.deltaTime)
                {
                    var k = t / 0.7f;
                    var bodyRenderer = lanternModel == null ? body.GetComponent<Renderer>() : null;
                    if (bodyRenderer != null) bodyRenderer.material.SetColor("_BaseColor", Color.Lerp(new Color(0.35f, 0.3f, 0.24f), new Color(1f, 0.74f, 0.4f), k));
                    light.intensity = 1.6f * k;
                    yield return null;
                }
            }
            gate.HideLettering();
            ShowCard(-1);
            _toDoor = toDoor;
            _awaitingWalkOn = true;
            ConfirmInput.Take(this);
        }

        const float LanternHeight = 1.9f;

        static void FitHeight(Transform model, float metres)
        {
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 1e-4f) model.localScale *= metres / b.size.y;
            // Stand it on the ground whatever its origin: scale about the pivot moved the base.
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            model.position += Vector3.up * (model.parent.position.y - b.min.y);
        }

        /// <summary>Which companion voices lantern <paramref name="i"/>: they take it in turn.</summary>
        string SpeakerFor(int i)
        {
            var c = opening != null ? opening.Companions : null;
            return c != null && c.Count > 0 ? c[i % c.Count] : null;
        }

        /// <summary>Pointing at a lantern: its line on the card, spoken by a companion beside the visitor.</summary>
        void Hear(int index)
        {
            ShowCard(index);
            var who = SpeakerFor(index);
            if (who != null) StartCoroutine(opening.Say(who, _lines[index]));
        }

        /// <summary>
        /// Her "companions take flank marks": once the answers are done the companions stand at the
        /// visitor's sides, out of the view down the walk, and come along (re-marked at each teleport) so
        /// the one who speaks a lantern's line is beside the visitor. Their invitation colliders go, so the
        /// laser reaches the lanterns past them.
        /// </summary>
        void WalkAlongside()
        {
            var group = opening != null && opening.Company != null ? opening.Company.Group : null;
            if (group == null) return;
            foreach (var f in group.Figures.Values)
            {
                if (f == null) continue;
                foreach (var c in f.GetComponentsInChildren<Collider>()) c.enabled = false;
                foreach (var p in f.GetComponentsInChildren<Pointable>()) p.enabled = false;
            }
            group.MarkCandidates = Flank;
            group.FollowVisitor = true;
            group.PlaceAll();
        }

        static readonly CompanionMarks.Mark[] Flanks =
            // Wide of the lanterns (within ~25 degrees of the walk) yet under CompanionGroup.BehindDegrees once
            // the visitor glances aside, so they are not re-marked every time the head turns.
            { new CompanionMarks.Mark(-52f, 1.6f), new CompanionMarks.Mark(52f, 1.6f), new CompanionMarks.Mark(-72f, 1.9f) };

        static IEnumerable<CompanionMarks.Mark> Flank(int order)
        {
            var m = Flanks[Mathf.Clamp(order, 0, Flanks.Length - 1)];
            yield return m;
            yield return new CompanionMarks.Mark(m.Bearing, 1.1f);
            yield return new CompanionMarks.Mark(-m.Bearing, m.Distance);
        }

        static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        void Tag(Transform lantern, string chapter, string subtitle)
        {
            var anchor = new GameObject("Name").transform;
            anchor.SetParent(lantern, false);
            anchor.position = lantern.position + Vector3.up * 2.05f;
            var toEye = gate.spawn.position - anchor.position; toEye.y = 0f;
            anchor.rotation = Quaternion.LookRotation(-toEye.normalized, Vector3.up);   // +Z away from the viewer reads
            var c = MuseUi.Canvas(anchor, "Lantern", 6f, 120f);
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 10f, padY: 6f, gap: 1f, name: "Lantern Card");
            card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var t = MuseUi.Text(card, chapter, MuseUi.Face.Serif, 16f, MuseTheme.Ink, name: "Chapter"); t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false;
            var s = MuseUi.Text(card, subtitle, MuseUi.Face.Sans, 9.5f, MuseTheme.Ink3, name: "Sub"); s.alignment = TextAlignmentOptions.Center; s.enableWordWrapping = false;
        }

        /// <summary>The card with a lantern's line, standing still beside the walk where the prompts stand.</summary>
        void ShowCard(int index)
        {
            if (_card == null)
            {
                var from = gate.spawn.position;
                var toDoor = gate.Doorway - from; toDoor.y = 0f; toDoor.Normalize();
                var right = Vector3.Cross(Vector3.up, toDoor).normalized;
                var anchor = new GameObject("Curation Card").transform;
                anchor.SetParent(transform, false);
                // Above the companions' heads (they stand at the visitor's flanks and hid a card at 1.45 m).
                var at = from + toDoor * 3.2f + right * 2.2f + Vector3.up * 2.05f;
                var toEye = from - at; toEye.y = 0f;
                anchor.SetPositionAndRotation(at, Quaternion.LookRotation(-toEye.normalized, Vector3.up));
                var c = MuseUi.Canvas(anchor, "Curation", 3f, 360f);
                var glass = MuseUi.Glass(c, 360f, gap: 8f);
                _cardKicker = MuseUi.Kicker(glass, "Your path", MuseTheme.Gold);
                _cardLine = MuseUi.Title(glass, "", 20f);
                _cardNote = MuseUi.Body(glass, "");
                _card = anchor.gameObject;
            }
            if (index < 0)
            {
                _cardKicker.text = "Your path";
                _cardLine.text = "Four rooms will answer your question";
                _cardNote.text = "Point at a lantern and pull the trigger to hear why it fits.";
                return;
            }
            _heard.Add(index);
            _cardKicker.text = Chapters[index] + "  ·  " + Subtitles[index];
            _cardLine.text = _lines[index];
            _cardNote.text = (_fallback ? "Local fallback - written ahead, not generated for your question.\n" : "")
                             + (_awaitingWalkOn ? "Point at another lantern, or press A to walk on: the first opens the way." : "");
        }

        public bool Confirm()
        {
            if (!_awaitingWalkOn) return false;
            if (_heard.Count == 0)
            {
                if (_card != null) _cardNote.text = "Point at a lantern first and pull the trigger to hear why it fits.";
                return false;
            }
            _awaitingWalkOn = false;
            ConfirmInput.Drop(this);
            MakeGate(_toDoor);
            return true;
        }

        public bool Redo() => false;

        // ---- the moon gate --------------------------------------------------------------------------

        void MakeGate(Vector3 toDoor)
        {
            var first = _lanterns[0];
            var at = first.position;
            first.gameObject.SetActive(false);   // the first lantern becomes the gate
            // The others have said their piece; left standing they showed through the gate's opening.
            for (var i = 1; i < _lanterns.Count; i++) _lanterns[i].gameObject.SetActive(false);
            if (_card != null)
            {
                _cardKicker.text = "Palace  ·  Court of Keeping";
                _cardLine.text = "The first lantern has become a moon gate";
                _cardNote.text = "Walk through it with your companions.";
            }

            // The shared Moon Gate (the Palace -> Grotto transition, Docs/MOON-GATE.md): it loads the Palace
            // behind the gate so its spawn lies just through it, reveals it in the keyhole as the visitor
            // looks, and clears the conservatory once they are through.
            WorldDefinition def = null;
            foreach (var w in WorldCatalog.Small) if (w.key.StartsWith("palace-court-of-keeping")) def = w;
            var mg = Instantiate(moonGatePrefab, at, Quaternion.LookRotation(toDoor, Vector3.up), transform);
            mg.nextWorldAsset = palaceWorld.m_Asset;
            mg.nextWorldKey = def != null ? def.key : "palace-court-of-keeping-500k";
            // Her generated gate's own round opening, no passage: the keyhole is exactly the hole in the frame.
            mg.radius = ModelOpeningRadius; mg.centreHeight = ModelOpeningCentre; mg.passageWidth = 0f;
            var props = new List<GameObject>();
            foreach (var g in gateLeftovers) if (g != null) props.Add(g);
            foreach (var l in _lanterns) if (l != null) props.Add(l.gameObject);
            if (_card != null) props.Add(_card);
            if (opening != null && opening.Company != null) props.Add(opening.Company.gameObject);
            if (!mg.Open(conservatoryWorld, props, _eye)) return;

            // Her Palace chapter (layout, props, interactions) stands on the gate's Palace: its frame takes
            // the pivot's pose, the gate's world becomes the one drawn, and our copy of the splat sleeps.
            var pivot = mg.NextWorld.transform.parent;
            palaceFrame.SetPositionAndRotation(pivot.position, pivot.rotation);
            palaceWorld.gameObject.SetActive(false);
            foreach (var c in palaceContent) if (c != null) c.SetActive(false);
            palaceFrame.gameObject.SetActive(true);
            pivot.SetParent(palaceFrame, true);

            // The conservatory has no stone ring of its own, so her generated moon gate is the frame.
            if (moonGateModel != null)
            {
                var frame = Instantiate(moonGateModel, mg.transform);
                frame.name = "Moon Gate Frame";
                frame.transform.localPosition = new Vector3(0f, 0f, -0.05f);   // on the visitor's side of the opening
                frame.transform.localRotation = Quaternion.identity;
            }
            mg.Arrived += () => StartCoroutine(Arrive());
            _gate = mg;
            Debug.Log("[Curation] the moon gate stands; the Palace is behind it");
        }

        // ---- through ----------------------------------------------------------------------------------

        IEnumerator Arrive()
        {
            if (_crossed) yield break;
            _crossed = true;
            // Move the Palace and the visitor back to the origin together, in one frame: everything the
            // chapter knows about its world assumes it stands at the origin (its layout was built there).
            var rig = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var r = Quaternion.Inverse(palaceFrame.rotation);
            var f = palaceFrame.position;
            var cc = rig != null ? rig.GetComponent<CharacterController>() : null;
            // Gravity waits until the Palace floor exists (the chapter builds it a frame or two after its
            // content wakes); otherwise the visitor drops through the gap (CLAUDE.md: y = -4,791).
            var gravity = rig != null ? rig.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>() : null;
            if (gravity != null) gravity.enabled = false;
            if (cc != null) cc.enabled = false;
            if (rig != null) rig.transform.SetPositionAndRotation(r * (rig.transform.position - f), r * rig.transform.rotation);
            palaceFrame.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (cc != null) cc.enabled = true;
            if (_gate != null) Destroy(_gate.gameObject);

            // The visitor's companions stand on her three marks in the Palace, in their speaking order.
            if (opening != null && opening.Companions != null && opening.Companions.Count > 0)
            {
                Masters.Company = opening.Companions;
                SwapFigures(opening.Companions);
            }
            yield return null;
            foreach (var c in palaceContent) if (c != null) c.SetActive(true);
            for (var i = 0; i < 30 && GameObject.Find("Teleport Floor") == null; i++) yield return null;
            yield return new WaitForFixedUpdate();
            if (gravity != null) gravity.enabled = true;
            Debug.Log("[Curation] through the moon gate: the Palace chapter begins with " + string.Join(", ", Masters.Company));
        }

        void SwapFigures(IReadOnlyList<string> company)
        {
            for (var i = 0; i < Masters.DefaultTrio.Count && i < company.Count; i++)
            {
                var mark = FindDeep(palaceFrame, "Mark " + Masters.DefaultTrio[i]);
                if (mark == null || company[i] == Masters.DefaultTrio[i]) continue;
                var prefab = opening.PrefabFor(company[i]);
                if (prefab == null) continue;
                foreach (Transform child in mark) Destroy(child.gameObject);
                // The layout instantiated the master prefab AS the mark, so its own renderers go too.
                foreach (var rend in mark.GetComponents<Renderer>()) rend.enabled = false;
                var figure = Instantiate(prefab, mark);
                figure.transform.localPosition = Vector3.zero; figure.transform.localRotation = Quaternion.identity;
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}
