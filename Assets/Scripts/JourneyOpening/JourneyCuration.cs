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
    public sealed class JourneyCuration : MonoBehaviour
    {
        public JourneyOpening opening;
        public GateStage gate;
        [Tooltip("Assets/Prefabs/Portal Door.prefab")]
        public GameObject portalDoorPrefab;
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

        [System.NonSerialized] public float firstLantern = 6.5f, lanternStep = 2.6f;   // metres down the walk from the spawn
        static readonly float[] LanternOffsets = { 0f, -1.7f, 1.7f, -1.7f };   // metres across the walk
        const float MoonGateOpeningCentre = 2.02f;   // measured on the generated model (Docs/Doors/moon-gate.measure.txt)
        const float MoonGateOpening = 2.55f;

        readonly string[] _lines = new string[4];
        bool _fallback = true, _begun, _crossed;
        readonly List<Transform> _lanterns = new List<Transform>();
        readonly List<Light> _lights = new List<Light>();
        GameObject _card;
        TextMeshProUGUI _cardKicker, _cardLine, _cardNote;
        SplatPortalDoor _door;
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
            if (_door != null && !_crossed && _door.IsDone) StartCoroutine(Arrive());
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
                Part(lantern, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.06f, 0.6f, 0.06f), wood);       // post
                var body = Part(lantern, PrimitiveType.Sphere, new Vector3(0f, 1.42f, 0f), new Vector3(0.42f, 0.52f, 0.42f), new Material(warm));   // a round paper lantern
                Part(lantern, PrimitiveType.Cylinder, new Vector3(0f, 1.71f, 0f), new Vector3(0.2f, 0.03f, 0.2f), wood);       // cap
                var light = new GameObject("Glow").AddComponent<Light>();
                light.transform.SetParent(lantern, false); light.transform.localPosition = new Vector3(0f, 1.42f, 0f);
                light.type = LightType.Point; light.range = 3.5f; light.intensity = 0f; light.color = new Color(1f, 0.78f, 0.45f);
                var box = lantern.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 1.1f, 0f); box.size = new Vector3(0.6f, 2.2f, 0.6f);
                var index = i;
                var p = Pointable.Make(lantern.gameObject, Keys[i]);
                p.Selected += (_, __) => ShowCard(index);
                p.Hovering += _ => body.GetComponent<Renderer>().material.SetColor("_BaseColor", new Color(1f, 0.86f, 0.62f));
                p.Unhovered += _ => body.GetComponent<Renderer>().material.SetColor("_BaseColor", new Color(1f, 0.74f, 0.4f));
                Tag(lantern, Chapters[i], Subtitles[i]);
                _lanterns.Add(lantern); _lights.Add(light);
                // It lights: the paper warms and its glow comes up.
                for (float t = 0f; t < 0.7f; t += Time.deltaTime)
                {
                    var k = t / 0.7f;
                    body.GetComponent<Renderer>().material.SetColor("_BaseColor", Color.Lerp(new Color(0.35f, 0.3f, 0.24f), new Color(1f, 0.74f, 0.4f), k));
                    light.intensity = 1.6f * k;
                    yield return null;
                }
            }
            ShowCard(-1);
            yield return new WaitForSeconds(8f);   // time to point at them; then the first becomes the gate
            MakeGate(toDoor);
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
                _cardNote.text = "Point at a lantern and pull the trigger to hear why. The first will open the way.";
                return;
            }
            _cardKicker.text = Chapters[index] + "  ·  " + Subtitles[index];
            _cardLine.text = _lines[index];
            _cardNote.text = _fallback ? "Local fallback - written ahead, not generated for your question." : "";
        }

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

            // The Palace stands behind the gate so that its playtested spawn lies just past the opening,
            // facing through it, at the visitor's floor height (the same rule JourneyDoors uses).
            WorldDefinition def = null;
            foreach (var w in WorldCatalog.Small) if (w.key.StartsWith("palace-court-of-keeping")) def = w;
            var pivotRot = Quaternion.LookRotation(toDoor, Vector3.up);
            var arrival = at + toDoor * 1.2f;
            var spawnRot = def != null ? def.SpawnRotation : Quaternion.identity;
            var spawnPos = def != null ? def.ScaledSpawn : Vector3.zero;
            palaceFrame.rotation = pivotRot * Quaternion.Inverse(spawnRot);
            palaceFrame.position = arrival - palaceFrame.rotation * spawnPos;
            foreach (var c in palaceContent) if (c != null) c.SetActive(false);
            palaceFrame.gameObject.SetActive(true);

            // The door: the SplatPortal door with her moon gate as its visible frame and a round opening.
            // Built under an inactive holder: the door's OnEnable claims its worlds and must not run before
            // they are set.
            var holder = new GameObject("Moon Gate");
            holder.transform.SetParent(transform, false);
            holder.SetActive(false);
            var doorGo = Instantiate(portalDoorPrefab, holder.transform);
            doorGo.SetActive(true);
            doorGo.transform.SetPositionAndRotation(at, pivotRot);
            var door = doorGo.GetComponent<SplatPortalDoor>();
            door.currentWorld = conservatoryWorld;
            door.nextWorld = palaceWorld;
            door.head = _eye;
            door.apertureSize = new Vector2(MoonGateOpening, MoonGateOpening);
            if (door.aperture != null) door.aperture.localPosition = new Vector3(0f, MoonGateOpeningCentre, 0f);
            door.maskMesh = Disc(MoonGateOpening * 0.5f);
            if (door.doorVisual != null && moonGateModel != null)
            {
                foreach (Transform child in door.doorVisual) child.gameObject.SetActive(false);
                var gateModel = Instantiate(moonGateModel, door.doorVisual);
                gateModel.transform.localPosition = new Vector3(0f, 0f, -0.05f);   // on the visitor's side of the opening
                gateModel.transform.localRotation = Quaternion.identity;
            }
            door.leftLeaf = null; door.rightLeaf = null;   // a moon gate is an open circle, no leaves
            door.enabled = true;
            holder.SetActive(true);
            _door = door;
            Debug.Log("[Curation] the moon gate stands; the Palace is behind it");
        }

        static Mesh Disc(float radius)
        {
            const int n = 64;
            var v = new Vector3[n + 1]; var t = new int[n * 3];
            v[0] = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % n; t[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { vertices = v, triangles = t, name = "Moon gate opening" }; m.RecalculateBounds(); return m;
        }

        // ---- through ----------------------------------------------------------------------------------

        IEnumerator Arrive()
        {
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

            foreach (var g in gateLeftovers) if (g != null) Destroy(g);
            if (opening != null && opening.Company != null) Destroy(opening.Company.gameObject);
            foreach (var l in _lanterns) if (l != null) Destroy(l.gameObject);
            if (_card != null) Destroy(_card);

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
