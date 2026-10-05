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

        /// <summary>
        /// Her updated script's lantern lines (MUSE-VR-design, 2 Oct 2026), fixed: the path the question
        /// takes, the same for every question - so no model call, nothing to label as a fallback.
        /// </summary>
        public static readonly string[] HerLines =
        {
            "Stop 1 · The Palace · first, what you inherited from others",
            "Stop 2 · The Grotto · then, yourself against a much longer time",
            "Stop 3 · The Van Gogh studio · turning feeling into something you make",
            "Stop 4 · The Monet garden · finally, deciding what is worth stopping for",
        };

        /// <summary>A lantern's line as a master says it: her middots read as pauses.</summary>
        static string Spoken(string line) => line.Replace(" · ", ", ") + ".";

        // Inside the pointer's 8 m reach from the spawn, all four (at 6.5 + 2.6 m steps three were beyond it,
        // so pointing at them did nothing - headset test).
        [System.NonSerialized] public float firstLantern = 3.6f, lanternStep = 1.9f;   // metres down the walk from the spawn
        const float LanternAside = 1.5f;   // metres either side of the walk's centre line
        const float ModelOpeningCentre = 2.02f;   // measured on the generated model (Docs/Doors/moon-gate.measure.txt)
        const float ModelOpeningRadius = 1.275f;
        const float GateScale = 1.3f;
        const float MorphSeconds = 2.2f;
        const float PortalOpenSeconds = 1.0f;   // the opening widens over the morph's last second                                    // a wider opening to see the Palace through
        const float GateCentre = ModelOpeningRadius * GateScale + 0.05f;   // the opening's foot just above the floor

        // The new gate (Saul, 5 Oct): the Palace lantern flies to the Palace's front door and fades out; once it
        // is gone Skylar's Forbidden City hall fades in; once the hall is whole the moon gate's frame fades in on
        // its front door and the Palace world opens inside it. Walking through the gate is the way in: the world
        // and the crossing moved from the walk to the hall's door. False: the old lantern-becomes-gate morph.
        public static bool PalaceGate = true;
        const float FlySeconds = 1.8f, LanternFadeSeconds = 1.0f, PalaceFadeSeconds = 1.6f, GateFadeSeconds = 1.0f;
        // The moon gate's frame as Saul placed it on the door in Play (5 Oct), kept in the hall's own model space
        // so it follows the hall when the hall is moved or scaled: turned to face out, scaled with it.
        static readonly Vector3 GateInPalace = new Vector3(-0.01153f, 0.05285f, 0.17937f);
        const float GateScaleInPalace = 0.032025f;
        // The gate is lower than a standing head (its opening ~1.3 m across at this scale), and the portal counts a
        // walk-through only when the eye passes inside the opening. Within this distance of the plane, inside its
        // width, the visitor is carried HopDistance through: a jump across the plane, which the portal counts at any
        // height (PortalSequence.TeleportedThrough).
        const float HopWithin = 0.35f, HopDistance = 0.7f;
        // Where Saul stood and scaled the hall in Play (5 Oct, second pass: larger, so its door is tall enough),
        // in this object's space: fixed, the same every run.
        static readonly Vector3 PalaceAt = new Vector3(-0.040f, -0.510f, -30.500f);
        const float PalaceYaw = 358.70f, PalaceScale = 18.9066f;
        // Skylar's hall (Resources/Heroes/palace-gate, a Tripo model ~1 unit across, front +Z), measured on
        // orthographic front and side renders: the front door is the bay between the two central columns,
        // from the terrace top to the beam, its face at z 0.176; the front stairs are a 33-degree ramp from
        // the ground at z 0.414 to the terrace at z 0.308.
        static readonly Vector3 DoorCentre = new Vector3(0.008f, 0.117f, 0.176f);
        const float DoorWidth = 0.146f, DoorHeight = 0.090f, TerraceTop = 0.072f;
        const float StairsFoot = 0.414f, StairsTop = 0.308f, StairsLeft = -0.049f, StairsRight = 0.063f, StairsGround = 0.002f;

        readonly string[] _lines = new string[4];
        bool _fallback = true, _begun, _crossed;
        readonly List<Transform> _lanterns = new List<Transform>();
        readonly List<Light> _lights = new List<Light>();
        GameObject _card;
        readonly List<Transform> _tags = new List<Transform>();
        TextMeshProUGUI _cardKicker, _cardLine, _cardNote;
        MoonGate _gate;
        // Her 2.3 has no timer: the visitor points at lanterns to hear them, and the first becomes the gate
        // when they choose to walk on (A) - an 8 s timer opened it before anyone had pointed at anything.
        readonly HashSet<int> _heard = new HashSet<int>();
        bool _awaitingWalkOn;
        Vector3 _toDoor, _from, _lastEye;
        Transform _eye;

        [System.Serializable] public class BakedLantern { public string question; public List<string> lines = new List<string>(); }
        [System.Serializable] public class BakedLanterns { public List<BakedLantern> items = new List<BakedLantern>(); }

        void Start()
        {
            if (opening == null) opening = FindAnyObjectByType<JourneyOpening>();
            if (gate == null) gate = FindAnyObjectByType<GateStage>();
            for (var i = 0; i < 4; i++) _lines[i] = HerLines[i];
            _fallback = false;   // her script, not a stand-in
            if (palaceFrame != null) palaceFrame.gameObject.SetActive(false);
            Shader.SetGlobalFloat("_PortalKeepBehind", 0f);   // a gate fade cut short must not leave every portal see-through
        }

        float _quietFor, _companyFor;
        bool _waitLogged, _hopped;

        void Update()
        {
            // Saul, 5 Oct: the lanterns come only once every chosen master has finished answering the question -
            // the round marked done, nobody's voice still playing - and a beat after.
            if (!_begun && opening != null && opening.Company != null && opening.Company.Current == CompanyStage.Phase.Done
                && !opening.Company.Group.Busy && !opening.Speaking)
            {
                _quietFor += Time.deltaTime;
                if (_quietFor >= 1f) { _begun = true; StartCoroutine(Unfold()); }
            }
            else _quietFor = 0f;
            HopThrough();
            // Why the lanterns have not come, said once after the company has stood 20 s (Saul, 5 Oct: "the
            // lanterns now never appear", and the log could not say which of the three waits held them).
            if (!_begun && !_waitLogged && opening != null && opening.Company != null && (_companyFor += Time.deltaTime) > 20f)
            {
                _waitLogged = true;
                var g = opening.Company.Group;
                Debug.LogWarning("[Curation] lanterns still waiting: company " + opening.Company.Current
                    + ", group busy " + (g != null && g.Busy) + (g != null && g.Turns != null ? " (turns " + g.Turns.Current + ")" : "")
                    + ", opening speaking " + opening.Speaking);
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
            for (var i = 0; i < 4; i++) { var sp = SpeakerFor(i); if (sp != null) opening.VoiceFor(sp, Spoken(_lines[i])); }   // fetch ahead
            var side = Vector3.Cross(Vector3.up, toDoor).normalized;
            // The generated lantern is required (Saul, 5 Oct: no shapes made in code): without it there are no lanterns.
            if (lanternModel == null) { Debug.LogError("[Curation] no lantern model assigned: no lanterns"); yield break; }

            for (var i = 0; i < 4; i++)
            {
                // The first stands on the walk (it becomes the gate); the rest step out to alternate sides, so
                // from the spawn all four are seen at once instead of hiding behind one another.
                // Two pairs flanking the walk, mirror-symmetric: Palace | Grotto, then Van Gogh | Monet.
                var at = from + toDoor * (firstLantern + lanternStep * (i / 2)) + side * (LanternAside * (i % 2 == 0 ? -1f : 1f));
                var lantern = new GameObject("Lantern " + Chapters[i]).transform;
                lantern.SetParent(transform, false);
                lantern.SetPositionAndRotation(at, face);
                var model = Instantiate(lanternModel, lantern);
                model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
                FitHeight(model.transform, LanternHeight);
                var light = new GameObject("Glow").AddComponent<Light>();
                light.transform.SetParent(lantern, false); light.transform.localPosition = new Vector3(0f, 1.42f, 0f);
                light.type = LightType.Point; light.range = 3.5f; light.intensity = 0f; light.color = new Color(1f, 0.78f, 0.45f);
                var box = lantern.gameObject.AddComponent<BoxCollider>();
                FitCollider(lantern, box);
                // A trigger: the pointer still hits it, but CompanionGroup's "is the mark free" ray ignores
                // it - a solid lantern box made every flank mark read blocked and dropped a companion right
                // in front of the visitor (headset test).
                box.isTrigger = true;
                var index = i;
                var p = Pointable.Make(lantern.gameObject, Keys[i]);
                p.Label = Chapters[i] + " lantern";
                CompassTarget.Add(lantern.gameObject, 10 + i, Chapters[i] + " lantern", "Point to hear why");   // done when pointed at
                p.Selected += (_, __) => Hear(index);
                p.Hovering += _ => light.intensity = 3.2f;     // brightens under the laser
                p.Unhovered += _ => light.intensity = 1.6f;
                Tag(lantern, Chapters[i], Subtitles[i], i < 2 ? 2.05f : 2.75f);   // the far pair's names above the near pair's, as seen from the spawn
                _lanterns.Add(lantern); _lights.Add(light);
                Appear.In(lantern.gameObject, 0.7f);   // the lantern and its name come up with its glow
                // It lights: its glow comes up.
                for (float t = 0f; t < 0.7f; t += Time.deltaTime)
                {
                    light.intensity = 1.6f * (t / 0.7f);
                    yield return null;
                }
            }
            // Saul, 4 Oct: the question stays over the arch the whole time at the Gate (it used to be taken down here).
            _toDoor = toDoor;
            _from = from;
            ShowCard(-1);
            _awaitingWalkOn = true;
            ConfirmInput.Take(this);
        }

        const float LanternHeight = 1.9f;

        /// <summary>The box round what is drawn, in the lantern's own space: the old 1.2 x 2.2 m box
        /// overlapped its neighbour from the visitor's eye, and the wrong lantern answered.</summary>
        static void FitCollider(Transform lantern, BoxCollider box)
        {
            var rs = lantern.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { box.center = new Vector3(0f, 1.1f, 0f); box.size = new Vector3(0.5f, 2.2f, 0.5f); return; }
            var b = new Bounds(lantern.InverseTransformPoint(rs[0].bounds.center), Vector3.zero);
            foreach (var r in rs)
            {
                var rb = r.bounds;
                for (var i = 0; i < 8; i++)
                    b.Encapsulate(lantern.InverseTransformPoint(rb.center + Vector3.Scale(rb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
            box.center = b.center; box.size = b.size;
        }

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
            if (who != null) StartCoroutine(opening.Say(who, Spoken(_lines[index])));
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
            group.Crowd = true;   // beside the visitor, never in front, walking with them
        }

        static readonly CompanionMarks.Mark[] Flanks =
            // Wide of the lanterns (within ~25 degrees of the walk) yet under CompanionGroup.BehindDegrees once
            // the visitor glances aside, so they are not re-marked every time the head turns.
            // Her rule: off the main path, 1.5-2.2 m, within +-60 degrees of forward, never behind.
            { new CompanionMarks.Mark(-52f, 1.7f), new CompanionMarks.Mark(42f, 1.6f), new CompanionMarks.Mark(58f, 2.2f) };

        static IEnumerable<CompanionMarks.Mark> Flank(int order)
        {
            var m = Flanks[Mathf.Clamp(order, 0, Flanks.Length - 1)];
            yield return m;
            yield return new CompanionMarks.Mark(m.Bearing, 1.5f);
            yield return new CompanionMarks.Mark(-m.Bearing, m.Distance);
        }

        void Tag(Transform lantern, string chapter, string subtitle, float height = 2.05f)
        {
            var anchor = new GameObject("Name").transform;
            _tags.Add(anchor);
            anchor.SetParent(lantern, false);
            anchor.position = lantern.position + Vector3.up * height;
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
                var c = MuseUi.Canvas(anchor, "Curation", 3f, 360f);
                var glass = MuseUi.Glass(c, 360f, gap: 8f);
                _cardKicker = MuseUi.Kicker(glass, "Your path", MuseTheme.Gold);
                _cardLine = MuseUi.Title(glass, "", 20f);
                _cardNote = MuseUi.Body(glass, "");
                _card = anchor.gameObject;
                Appear.In(_card, 0.5f);   // eased in, never popped (Saul, 5 Oct)
            }
            PlaceCard();
            if (index < 0)
            {
                _cardKicker.text = "Your path";
                _cardLine.text = "Four rooms will answer your question";
                _cardNote.text = "Point at a lantern and pull the trigger to hear why it fits.";
                return;
            }
            _heard.Add(index);
            var who = SpeakerFor(index);
            _cardKicker.text = (who != null ? Masters.Name(who) + "  ·  on the " : "") + Chapters[index] + "  ·  " + Subtitles[index];
            _cardLine.text = _lines[index];
            _cardNote.text = (_fallback ? "Local fallback - written ahead, not generated for your question.\n" : "")
                             + (_awaitingWalkOn ? "Point at another lantern, or press A to walk on: the first opens the way." : "");
        }

        /// <summary>
        /// The card stands where the visitor can read it: 2.3 m ahead and a little right of where they look,
        /// set once - when it changes, or after a teleport - and never following the head.
        /// </summary>
        void PlaceCard()
        {
            if (_card == null || _eye == null) return;
            var fwd = _eye.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : _toDoor;
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            // High and a little to the right: at eye height it sat across the companions' heads (headset test).
            var at = _eye.position + fwd * 2.6f + Vector3.up * 1.15f;   // above the lanterns and their names, not across them (4 Oct)
            _card.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - new Vector3(_eye.position.x, at.y, _eye.position.z), Vector3.up));
            _lastEye = _eye.position;
            _lastPlaced = Time.time;
        }

        float _lastPlaced;

        /// <summary>Once the gate stands its card has said its piece; left up, it floated in the opening.</summary>
        IEnumerator RetireCard()
        {
            yield return new WaitForSeconds(5f);
            if (_card != null) Appear.Out(_card, 0.6f);
        }

        void LateUpdate()
        {
            // A lantern's name tag that stands between the eye and the open card is stepped aside: the
            // pointed lantern's "Palace · Court of Keeping" covered Monet's line (live run, 4 Oct). The card
            // names the chapter in its kicker, so nothing is lost while it is up.
            var cardUp = _card != null && _card.activeInHierarchy && _eye != null;
            foreach (var tag in _tags)
            {
                if (tag == null) continue;
                var hide = false;
                if (cardUp)
                {
                    var toCard = _card.transform.position - _eye.position; var toTag = tag.position - _eye.position;
                    hide = toTag.magnitude < toCard.magnitude + 0.5f && Vector3.Angle(toCard, toTag) < 24f;
                }
                Appear.Set(tag.gameObject, !hide, 0.25f);
            }
            // A teleport moves the eye in one frame: bring the card to the new spot. Walking (smooth
            // locomotion) never jumps, so also when the visitor has walked up to it or past it - at most
            // once per 1.5 s, so it does not jitter.
            if (_card != null && _card.activeInHierarchy && _eye != null)
            {
                var eyeFlat = new Vector3(_eye.position.x, 0f, _eye.position.z);
                var toCard = _card.transform.position - _eye.position; toCard.y = 0f;
                var fwd = _eye.forward; fwd.y = 0f;
                bool jumped = Vector3.Distance(eyeFlat, new Vector3(_lastEye.x, 0f, _lastEye.z)) > 0.5f;
                bool tooClose = toCard.magnitude < 1.6f;
                bool outOfView = fwd.sqrMagnitude > 1e-4f && toCard.sqrMagnitude > 1e-4f && Vector3.Dot(fwd.normalized, toCard.normalized) < 0.5f;
                if (jumped || ((tooClose || outOfView) && Time.time - _lastPlaced > 1.5f)) PlaceCard();
            }
            if (_eye != null) _lastEye = _eye.position;
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
            StartCoroutine(MakeGate(_toDoor));
            return true;
        }

        public bool Redo() => false;

        // ---- the moon gate --------------------------------------------------------------------------

        IEnumerator MakeGate(Vector3 toDoor)
        {
            // The walk's record, for the roundtable and Your world, taken now: the Gate goes with the
            // conservatory when the visitor is through, so read at arrival it was already gone and the
            // question reached the roundtable empty ("you asked no question aloud" - full-walk test, 4 Oct).
            if (gate != null) JourneyMemory.Record.SetQuestion(gate.Flow.Question);
            if (opening != null && opening.Companions != null && opening.Companions.Count > 0) JourneyMemory.Record.SetCompanions(opening.Companions);
            var first = _lanterns[0];
            // On the walk's centre line beyond the lanterns, and always ahead of the visitor wherever they
            // now stand - never on top of them.
            var along = Mathf.Max(firstLantern + lanternStep + 2.4f, Vector3.Dot(_eye.position - _from, toDoor) + 4f);
            // The Palace's stairs reach ~7.4 m in front of its door: the visitor stands before them, not on them
            // between the railings (first run, 5 Oct: the gate 8 m ahead put the railings beside the visitor).
            var at = _from + toDoor * along; at.y = _from.y;
            // Stand it on the floor that is actually there (measured 0.4 m above the spawn at this spot).
            if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var floorHit, 6f, ~0, QueryTriggerInteraction.Ignore))
                at.y = floorHit.point.y;
            if (PalaceGate) { yield return PalaceRises(first); yield break; }
            CutTunnel(at, Quaternion.LookRotation(toDoor, Vector3.up));
            if (_card != null)
            {
                _cardKicker.text = "Palace  ·  Court of Keeping";
                _cardLine.text = "The Palace lantern becomes a moon gate";
                _cardNote.text = "Walk through the moon gate. The exhibition begins.";
                PlaceCard();
            }

            // The shared Moon Gate (the Palace -> Grotto transition, Docs/MOON-GATE.md): it loads the Palace
            // behind the gate so its spawn lies just through it, reveals it in the keyhole as the visitor
            // looks, and clears the conservatory once they are through.
            WorldDefinition def = null;
            foreach (var w in WorldCatalog.Small) if (w.key.StartsWith("palace-court-of-keeping")) def = w;
            var mg = Instantiate(moonGatePrefab, at, Quaternion.LookRotation(toDoor, Vector3.up), transform);
            Transform frame = null;
            if (moonGateModel != null)
            {
                // Her generated gate, 1.3x, sunk so its round opening meets the floor: walked or teleported
                // through, not stepped over a sill 0.75 m up.
                frame = Instantiate(moonGateModel, mg.transform).transform;
                frame.name = "Moon Gate Frame";
                frame.localPosition = new Vector3(0f, GateCentre - ModelOpeningCentre * GateScale, -0.05f);
                frame.localRotation = Quaternion.identity;
                frame.localScale = Vector3.zero;
            }

            mg.nextWorldAsset = palaceWorld.m_Asset;
            mg.nextWorldKey = def != null ? def.key : "palace-court-of-keeping-500k";
            // Her generated gate's own round opening, no passage: the keyhole is exactly the hole in the frame.
            mg.radius = ModelOpeningRadius * GateScale; mg.centreHeight = GateCentre; mg.passageWidth = 1.4f;
            var props = new List<GameObject>();
            foreach (var g in gateLeftovers) if (g != null) props.Add(g);
            foreach (var l in _lanterns) if (l != null) props.Add(l.gameObject);
            if (_card != null) props.Add(_card);
            if (opening != null && opening.Company != null) props.Add(opening.Company.gameObject);
            if (!mg.Open(conservatoryWorld, props, _eye)) yield break;

            // Her Palace chapter (layout, props, interactions) stands on the gate's Palace: its frame takes
            // the pivot's pose, the gate's world becomes the one drawn, and our copy of the splat sleeps.
            var pivot = mg.NextWorld.transform.parent;
            palaceFrame.SetPositionAndRotation(pivot.position, pivot.rotation);
            palaceWorld.gameObject.SetActive(false);
            foreach (var c in palaceContent) if (c != null) c.SetActive(false);
            palaceFrame.gameObject.SetActive(true);
            pivot.SetParent(palaceFrame, true);   // MoonGate.Open already cut the Palace's spawn floaters

            // The Palace shows through as the gate forms, not seconds later: no waiting to be looked at,
            // no separate appear step, and the opening widens in step with the morph (it is passable at
            // 60%). Measured before: ~0.4 s look + 2.5 s appear + 2.1 s to passable, after the 2.2 s morph,
            // and a teleport made in that time landed beyond the gate in the old world.
            var sequence = mg.Door.Sequence;
            sequence.AppearSeconds = 0.01f;
            sequence.OpenSeconds = PortalOpenSeconds;
            sequence.CloseSeconds = 0.6f;   // arrival follows the crossing closely
            // Only the morph opens it: left on, its own "near and looking" trigger opened the circle while
            // the gate was still growing (measured: 43% open with the frame at 63%).
            sequence.TriggerDistance = 0f;

            // What the Palace holds is there before the visitor arrives: its layout and props stand in the
            // world seen through the gate. The companions' figures on its marks wait for the crossing (the
            // companions are walking beside the visitor); the interactions wake on arrival, at the origin.
            foreach (var c in palaceContent) if (c != null && c.name.StartsWith("Chapter")) c.SetActive(true);
            ShowMarks(false);
            mg.Crossed += () => { ShowMarks(true); if (opening != null && opening.Companions.Count > 0) SwapFigures(opening.Companions); };


            // The morph: the Palace lantern drifts to the gate and rises into the opening, glowing brighter
            // and shrinking, while the gate grows round it. Nothing pops.
            var glow = first.GetComponentInChildren<Light>();
            Vector3 p0 = first.position, p1 = at + Vector3.up * (GateCentre - 1.42f);
            var s0 = first.localScale;
            const float duration = MorphSeconds;
            var requested = false;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                // The opening shows the Palace only once the gate stands round it, never a bare circle first.
                if (!requested && t >= duration - PortalOpenSeconds * 0.5f) { sequence.RequestOpen(); requested = true; }
                var k = Mathf.SmoothStep(0f, 1f, t / duration);
                first.position = Vector3.Lerp(p0, p1, k);
                first.localScale = s0 * Mathf.Lerp(1f, 0.25f, Mathf.SmoothStep(0f, 1f, (t / duration - 0.5f) * 2f));
                if (glow != null) { glow.intensity = Mathf.Lerp(1.6f, 6f, k); glow.range = Mathf.Lerp(3.5f, 6f, k); }
                if (frame != null) frame.localScale = Vector3.one * GateScale * Mathf.SmoothStep(0f, 1f, t / (duration * 0.65f));
                yield return null;
            }
            if (frame != null) frame.localScale = Vector3.one * GateScale;
            if (!requested) sequence.RequestOpen();
            Appear.Out(first.gameObject, 0.3f);
            mg.Arrived += () => StartCoroutine(Arrive());
            foreach (var l in _lanterns) if (l != null) { var lt = l.GetComponent<CompassTarget>(); if (lt != null) lt.MarkDone(); }
            CompassTarget.Add(mg.gameObject, 20, "The moon gate", "Walk through to the Palace");
            StartCoroutine(RetireCard());
            _gate = mg;
            Debug.Log("[Curation] the moon gate stands; the Palace is behind it");
        }

        /// <summary>
        /// The new gate, as far as it goes: the lantern flies to the Palace's front door and fades out; then the
        /// hall fades in where Saul placed it; then the moon gate's frame fades in on its front door.
        /// </summary>
        IEnumerator PalaceRises(Transform lantern)
        {
            if (_card != null)
            {
                _cardKicker.text = "Palace  ·  Court of Keeping";
                _cardLine.text = "The Palace lantern shows the way";
                _cardNote.text = "";
                PlaceCard();
            }
            var palace = PlacePalace();
            if (palace == null) yield break;
            var pt = palace.transform;
            var door = pt.TransformPoint(DoorCentre);
            var glow = lantern.GetComponentInChildren<Light>();
            // Its name card stays behind: it flew along as a white tile (5 Oct). Out of _tags, which re-shows them.
            var tag = lantern.Find("Name");
            if (tag != null) { _tags.Remove(tag); Appear.Out(tag.gameObject, 0.3f); }
            // Its glow (1.42 m up the lantern) arrives at the door's centre, just in front of it.
            Vector3 p0 = lantern.position, p1 = door + pt.forward * 0.6f - Vector3.up * 1.42f;
            for (float t = 0f; t < FlySeconds; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / FlySeconds);
                lantern.position = Vector3.Lerp(p0, p1, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;   // an arc, so the flight reads
                if (glow != null) glow.intensity = Mathf.Lerp(1.6f, 4f, k);
                yield return null;
            }
            lantern.position = p1;
            Appear.Out(lantern.gameObject, LanternFadeSeconds);
            for (float t = 0f; t < LanternFadeSeconds; t += Time.deltaTime)
            {
                if (glow != null) glow.intensity = 4f * (1f - t / LanternFadeSeconds);
                yield return null;
            }
            Appear.In(palace, PalaceFadeSeconds);
            yield return new WaitForSeconds(PalaceFadeSeconds);
            var gate = PlaceMoonGate(pt);
            if (gate == null) { Debug.LogError("[Curation] no moon gate model: no way into the Palace"); yield break; }
            // The frame and the world inside it fade in together (Saul, 5 Oct: the gate "just appeared"). The
            // opening is there at once; what comes up is the Palace splat's opacity, with the frame's.
            var mg = OpenPalaceGate(gate.transform, pt);
            var world = mg != null ? mg.NextWorld : null;
            // The hall's red door shows through the circle until the Palace has come up over it: the portal's
            // erase cross-fades (PortalMask.shader, _PortalKeepBehind) instead of cutting it to black first.
            var keepBehind = Shader.PropertyToID("_PortalKeepBehind");
            Shader.SetGlobalFloat(keepBehind, 1f);
            if (world != null) world.m_OpacityScale = 0f;
            Appear.In(gate, GateFadeSeconds);
            if (mg != null) mg.Door.Sequence.RequestOpen();
            for (float t = 0f; t < GateFadeSeconds; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / GateFadeSeconds);
                if (world != null) world.m_OpacityScale = k;
                Shader.SetGlobalFloat(keepBehind, 1f - k);
                yield return null;
            }
            if (world != null) world.m_OpacityScale = 1f;
            Shader.SetGlobalFloat(keepBehind, 0f);
            foreach (var l in _lanterns) if (l != null) { var lt = l.GetComponent<CompassTarget>(); if (lt != null) lt.MarkDone(); }
            Debug.Log("[Curation] the Palace stands, the moon gate on its door, the Palace world inside it" + (mg != null ? "" : " - THE GATE DID NOT OPEN"));
        }

        /// <summary>
        /// The Palace world behind the moon gate on the hall's door: the shared MoonGate set on the terrace under
        /// the frame's opening, its keyhole the frame's own circle. On the way through, the hall and the frame go
        /// with the conservatory; on arrival the Palace chapter takes over (Arrive).
        /// </summary>
        MoonGate OpenPalaceGate(Transform frame, Transform palace)
        {
            var s = frame.lossyScale.x;
            var centre = frame.position + Vector3.up * (ModelOpeningCentre * s);
            var through = frame.forward; through.y = 0f; through.Normalize();   // +Z into the next world
            var threshold = centre; threshold.y = palace.TransformPoint(new Vector3(0f, TerraceTop, DoorCentre.z)).y;
            CutTunnel(threshold, Quaternion.LookRotation(through, Vector3.up));
            if (_card != null)
            {
                _cardKicker.text = "Palace  ·  Court of Keeping";
                _cardLine.text = "The Palace lantern becomes a moon gate";
                _cardNote.text = "Walk through the moon gate. The exhibition begins.";
            }

            WorldDefinition def = null;
            foreach (var w in WorldCatalog.Small) if (w.key.StartsWith("palace-court-of-keeping")) def = w;
            var mg = Instantiate(moonGatePrefab, threshold, Quaternion.LookRotation(through, Vector3.up), transform);
            mg.nextWorldAsset = palaceWorld.m_Asset;
            mg.nextWorldKey = def != null ? def.key : "palace-court-of-keeping-500k";
            mg.radius = ModelOpeningRadius * s;
            mg.centreHeight = centre.y - threshold.y;
            mg.passageWidth = 0f;   // the round opening only: the frame's circle is the keyhole
            var props = new List<GameObject>();
            foreach (var g in gateLeftovers) if (g != null) props.Add(g);
            foreach (var l in _lanterns) if (l != null) props.Add(l.gameObject);
            if (_card != null) props.Add(_card);
            if (this.opening != null && this.opening.Company != null) props.Add(this.opening.Company.gameObject);
            props.Add(palace.gameObject);   // the hall and its frame are the outside of the gate: behind the visitor once through
            props.Add(frame.gameObject);
            if (!mg.Open(conservatoryWorld, props, _eye)) return null;

            // Her Palace chapter stands on the gate's Palace, as with the old gate.
            var pivot = mg.NextWorld.transform.parent;
            palaceFrame.SetPositionAndRotation(pivot.position, pivot.rotation);
            palaceWorld.gameObject.SetActive(false);
            foreach (var c in palaceContent) if (c != null) c.SetActive(false);
            palaceFrame.gameObject.SetActive(true);
            pivot.SetParent(palaceFrame, true);
            var sequence = mg.Door.Sequence;
            sequence.AppearSeconds = 0.01f;
            sequence.OpenSeconds = 0.05f;   // open at once: the world fades in by its opacity, not as a widening circle
            sequence.CloseSeconds = 0.6f;
            sequence.TriggerDistance = 0f;   // the fade opens it, not a look
            foreach (var c in palaceContent) if (c != null && c.name.StartsWith("Chapter")) c.SetActive(true);
            ShowMarks(false);
            mg.Crossed += () => { ShowMarks(true); if (this.opening != null && this.opening.Companions.Count > 0) SwapFigures(this.opening.Companions); };
            mg.Arrived += () => StartCoroutine(Arrive());
            CompassTarget.Add(mg.gameObject, 20, "The moon gate", "Walk through to the Palace");
            StartCoroutine(RetireCard());
            _gate = mg; _hopped = false;
            return mg;
        }

        /// <summary>
        /// The step through the low gate: the eye within <see cref="HopWithin"/> of the plane, inside the
        /// opening's width, once the gate is passable - carried <see cref="HopDistance"/> through.
        /// </summary>
        void HopThrough()
        {
            if (!PalaceGate || _gate == null || _hopped || _crossed || _gate.Door == null || _eye == null) return;
            if (_gate.Door.Sequence.Opening < MoonGate.PassableOpening) return;
            var local = _gate.transform.InverseTransformPoint(_eye.position);
            if (local.z >= 0f || local.z < -HopWithin || Mathf.Abs(local.x) > _gate.radius) return;
            var rig = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (rig == null) return;
            _hopped = true;
            var cc = rig.GetComponent<CharacterController>();
            var had = cc != null && cc.enabled;
            if (had) cc.enabled = false;
            var through = _gate.transform.forward; through.y = 0f;
            rig.transform.position += through.normalized * HopDistance;
            Physics.SyncTransforms();
            if (had) cc.enabled = true;
            Debug.Log("[Curation] stepped through the moon gate");
        }

        /// <summary>Skylar's hall where Saul placed it, inactive (Appear.In shows it), with the walkable ramp and terrace.</summary>
        GameObject PlacePalace()
        {
            var model = Resources.Load<GameObject>("Heroes/palace-gate");
            if (model == null) { Debug.LogError("[Curation] no palace model at Resources/Heroes/palace-gate"); return null; }
            var go = Instantiate(model, transform);
            go.name = "Palace Hall (Skylar)";
            go.transform.localPosition = PalaceAt;
            go.transform.localRotation = Quaternion.Euler(0f, PalaceYaw, 0f);
            go.transform.localScale = Vector3.one * PalaceScale;
            Walkable(go.transform);
            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// Colliders for smooth locomotion up the front: a slab along the stairs (33 degrees, under the
        /// CharacterController's 45-degree limit) and one over the terrace to the door. In the model's own
        /// units; the walls get none, so the door stays open to walk through.
        /// </summary>
        static void Walkable(Transform palace)
        {
            const float slab = 0.01f;   // 16 cm at Saul's scale, under the surface
            var run = StairsFoot - StairsTop; var rise = TerraceTop - StairsGround;
            var ramp = new GameObject("Walk Ramp").transform;
            ramp.SetParent(palace, false);
            var tilt = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            ramp.localRotation = Quaternion.Euler(tilt, 0f, 0f);   // +Z runs down the stairs toward the visitor
            var mid = new Vector3((StairsLeft + StairsRight) / 2f, (StairsGround + TerraceTop) / 2f, (StairsFoot + StairsTop) / 2f);
            ramp.localPosition = mid - ramp.localRotation * Vector3.up * (slab / 2f);
            var rb = ramp.gameObject.AddComponent<BoxCollider>();
            rb.size = new Vector3(StairsRight - StairsLeft, slab, Mathf.Sqrt(run * run + rise * rise) + 0.004f);
            var terrace = new GameObject("Walk Terrace").transform;
            terrace.SetParent(palace, false);
            terrace.localPosition = new Vector3(DoorCentre.x, TerraceTop - slab / 2f, (StairsTop + DoorCentre.z) / 2f - 0.01f);
            var tb = terrace.gameObject.AddComponent<BoxCollider>();
            tb.size = new Vector3(DoorWidth, slab, StairsTop - DoorCentre.z + 0.03f);   // a little past the door face
        }

        /// <summary>Her moon gate's frame on the Palace's front door, where Saul placed it on the hall; inactive (Appear.In shows it).</summary>
        GameObject PlaceMoonGate(Transform palace)
        {
            if (moonGateModel == null) return null;
            var frame = Instantiate(moonGateModel, transform);
            frame.name = "Moon Gate (on the Palace door)";
            frame.transform.SetPositionAndRotation(palace.TransformPoint(GateInPalace), palace.rotation * Quaternion.Euler(0f, 180f, 0f));
            frame.transform.localScale = Vector3.one * (palace.lossyScale.x * GateScaleInPalace);
            frame.SetActive(false);
            return frame;
        }

        /// <summary>
        /// Take the conservatory's own splats out of the way through the gate: an inverted box cutout on
        /// its renderer, the opening's width and height, 1.6 m either side of the gate, above the floor.
        /// Left in, foliage and wall splats sat in the opening and hid half of the Palace (headset test).
        /// </summary>
        void CutTunnel(Vector3 floor, Quaternion facing)
        {
            if (conservatoryWorld == null) return;
            var cut = new GameObject("Moon Gate Tunnel (splat cutout)").AddComponent<GaussianCutout>();
            cut.transform.SetParent(transform, false);
            var half = new Vector3(ModelOpeningRadius * GateScale + 0.25f, 1.85f, 1.6f);   // the box spans -1..1 locally
            cut.transform.SetPositionAndRotation(floor + Vector3.up * (0.12f + half.y), facing);
            cut.transform.localScale = half;
            cut.m_Type = GaussianCutout.Type.Box;
            cut.m_Invert = true;   // inside the box is removed, everything else stays
            var list = new List<GaussianCutout>();
            if (conservatoryWorld.m_Cutouts != null) list.AddRange(conservatoryWorld.m_Cutouts);
            list.Add(cut);
            conservatoryWorld.m_Cutouts = list.ToArray();
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

            // The walk's record, for the roundtable and Your world: the question asked at the Gate and who came.
            if (gate != null) JourneyMemory.Record.SetQuestion(gate.Flow.Question);
            if (opening != null && opening.Companions != null && opening.Companions.Count > 0) JourneyMemory.Record.SetCompanions(opening.Companions);
            // The visitor's companions stand on her three marks in the Palace, in their speaking order.
            if (opening != null && opening.Companions != null && opening.Companions.Count > 0)
            {
                Masters.Company = opening.Companions;
                SwapFigures(opening.Companions);
            }
            yield return null;
            foreach (var c in palaceContent) if (c != null && !c.activeSelf) Appear.In(c, ChapterLink.ArriveFade);   // nothing pops (Saul, 5 Oct)
            for (var i = 0; i < 30 && GameObject.Find("Teleport Floor") == null; i++) yield return null;
            yield return new WaitForFixedUpdate();
            if (gravity != null) gravity.enabled = true;
            Debug.Log("[Curation] through the moon gate: the Palace chapter begins with " + string.Join(", ", Masters.Company));
        }

        /// <summary>The figures standing on the Palace's three marks, shown or hidden.</summary>
        void ShowMarks(bool on)
        {
            for (var i = 0; i < Masters.DefaultTrio.Count; i++)
            {
                var mark = FindDeep(palaceFrame, "Mark " + Masters.DefaultTrio[i]);
                if (mark == null) continue;
                foreach (var r in mark.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            }
        }

        bool _swapped;

        void SwapFigures(IReadOnlyList<string> company)
        {
            if (_swapped) return;
            _swapped = true;
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
