using System.Collections;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Brings her chapter B to life in the laid-out Grotto (Tests/GrottoChapter.unity, built by
    /// ChapterLayout from her diagram B). It finds what the layout placed - "Prop relief",
    /// "Interaction Socket · detail", "Interaction Socket · whole", "Mark monet / van_gogh / socrates" -
    /// and adds:
    ///
    ///   a teleport floor over the terrace (invisible);
    ///   the lamp, on the capture's own brass stand front-left, grip-grabbable, its light reaching
    ///   only the relief (LampLight);
    ///   the two sockets on stone posts: "detail" just before the niche relief, "whole" on the rail;
    ///   the gold rim round the distant cliff Buddha, and its AI-rendition label;
    ///   the companions on her marks, voicing the two ways of seeing, with subtitles;
    ///   GrottoChapter: chime, light / rim, companions, then A keeps grotto{lampSlot, exhibitId}.
    ///
    /// Everything stands where her diagram B puts it (Saul, 3 Oct 2026: "exactly the diagram"), read
    /// from the layout's objects - "Prop lamp-stand", "Prop relief", "Interaction Socket · detail" /
    /// "· whole", the marks - EXCEPT the exit, which is the capture's own arch (see <see cref="arch"/>).
    /// The booth works are stretched to fill the capture's own gold frames. The Buddha, which only the capture has, was found by triangulating two captures:
    /// the entry (bearing 183.4, 22.6 deg up) and the rail (bearing 185.1, 26 deg up).
    /// </summary>
    public sealed class GrottoChapterInteractions : MonoBehaviour
    {
        [Tooltip("Assets/Art/Props/lamp.glb.")]
        public GameObject lampPrefab;

        [Tooltip("Assets/Worlds/Colliders/grotto-hall-of-time-collider.glb: the terrace rises ~2.7 m to the arch, so teleport needs the capture's own floor.")]
        public GameObject colliderModel;

        /// <summary>
        /// The exit: a Moon Gate with its own carved arch (grotto-arch.glb, the gate's frame) on the
        /// terrace's right, a few metres from the sockets. Nothing is there until A keeps the choice;
        /// then the arch rises out of the floor with Van Gogh's studio already showing inside it.
        /// (Saul, 3 Oct 2026: the capture's own arch door "looks too bad to use"; bring the gate back,
        /// closer, and let me see the other world.)
        /// </summary>
        [Tooltip("The Moon Gate with the carved arch as its frame; next world: Van Gogh's studio.")]
        public MuseXR.Worlds.MoonGate arch;

        public GrottoChapter Chapter { get; private set; }
        public SlotStation Sockets { get; private set; }
        public CompanionGroup Companions { get; private set; }
        public Holdable Lamp { get; private set; }
        public ChoicePreview Preview { get; private set; }
        /// <summary>The "hear each first" card: between the two posts, a little toward the visitor, at eye height.</summary>
        public const float PreviewUp = 2.0f, PreviewBefore = 0.6f;
        public MusePico.Dialogue.JourneyRecord Record { get; } = new MusePico.Dialogue.JourneyRecord();

        /// <summary>Her brass stand: the lamp "at reachable height, seated too".</summary>
        public const float StandHeight = 0.95f;
        /// <summary>Where the lamp's flame sits in the generated lamp, its own space (InteractionsDemo's value).</summary>
        public static readonly Vector3 LampFlame = new Vector3(0f, 0.31f, 0f);
        /// <summary>The cliff Buddha's centre, and its size (about 30 m seated).</summary>
        public static readonly Vector3 BuddhaCentre = new Vector3(-5.2f, 33.8f, -76.3f);
        public static readonly Vector2 RimSize = new Vector2(38f, 42f);
        /// <summary>LampLight's intensity is tuned for 0.43 m from the relief; it is scaled by distance squared so
        /// the carving reads the same from the socket, capped so a lamp held right against it never clips.</summary>
        public const float TunedDistance = 0.43f, MaxBoost = 6f;

        Transform _relief;
        LampLight _lampLight;

        void Update()
        {
            UpdateGuide();
            if (_relief == null || _lampLight == null || _lampLight.Light == null) return;
            var d = Vector3.Distance(_lampLight.Light.transform.position, _relief.position);
            var k = Mathf.Clamp((d * d) / (TunedDistance * TunedDistance), 1f, MaxBoost);
            _lampLight.Light.intensity = LampLight.Intensity * k;
            _lampLight.Light.range = Mathf.Max(LampLight.Range, d + 1f);
        }

        /// <summary>Socket heights above the floor: hand height on a post (her 0.8-1.3 m).</summary>
        public const float DetailHeight = 1.0f, WholeHeight = 1.05f;

        GameObject _floor;

        // The floor exists before the first physics frame. Made after a yield, gravity had already
        // dropped the rig a hair below y 0, the one-sided plane appeared above it, and it fell for
        // ever (measured: y -4,227 in a Grotto run; the Gate walk hit the same).
        void Awake() => _floor = TeleportFloor();

        IEnumerator Start()
        {
            yield return null;   // after the rig and the layout have woken
            var head = Camera.main != null ? Camera.main.transform : null;
            var entry = head != null ? head.position : Vector3.zero;

            var teleportFloor = _floor;
            MuseXR.Worlds.WorldDefinition grotto = null;
            foreach (var w in MuseXR.Worlds.WorldCatalog.Small) if (w.key == "grotto-hall-of-time-500k") grotto = w;
            _probe = colliderModel != null ? MuseXR.Worlds.CaptureProbe.Open(grotto, new[] { colliderModel }) : null;
            if (_probe != null) _probe.MakeTeleportable();
            else Debug.LogWarning("[Grotto] no capture collider: teleport only on the flat floor, not up to the arch");

            var relief = Find("Prop relief");
            var detailAt = Find("Interaction Socket · detail");
            var wholeAt = Find("Interaction Socket · whole");
            var standAt = Find("Prop lamp-stand");
            if (relief == null || detailAt == null || wholeAt == null || standAt == null)
            {
                Debug.LogError("[Grotto] layout objects missing: relief " + (relief != null) + ", detail " + (detailAt != null) +
                               ", whole " + (wholeAt != null) + ", lamp-stand " + (standAt != null));
                yield break;
            }
            foreach (var r in relief.GetComponentsInChildren<Renderer>()) LampLight.MarkRelief(r);

            // Unlit cream: the splat world takes no light, and a lit post read as a blue-grey box (capture).
            var stone = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            stone.SetColor("_BaseColor", new Color(0.86f, 0.79f, 0.67f));
            // "detail" on her socket point, ~0.7 m before the relief on the wall.
            var detail = Socket("Detail", detailAt.position, DetailHeight, entry, stone, Icon.Magnifier);
            _relief = relief;
            var whole = Socket("Whole", wholeAt.position, WholeHeight, entry, stone, Icon.Mountain);

            // Her brass stand, front-left of the entry, the lamp on it.
            var standTop = BrassStand(standAt.position);
            GameObject lamp;
            var face = Quaternion.LookRotation(Flat(entry - standTop), Vector3.up);
            if (lampPrefab != null) { lamp = Instantiate(lampPrefab, standTop, face, transform); lamp.name = "Lamp"; }
            else { lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere); lamp.name = "Lamp"; lamp.transform.SetPositionAndRotation(standTop + Vector3.up * 0.08f, face); lamp.transform.localScale = Vector3.one * 0.15f; }
            _lampLight = LampLight.Make(lamp, lampPrefab != null ? LampFlame : Vector3.zero);
            Lamp = Holdable.Make(lamp, "Lamp", idleSpin: false);

            Sockets = SlotStation.Make(gameObject, global::MuseXR.Slots.Chapter.Grotto, new[] { detail, whole }, new[] { Lamp },
                                       new[] { "Detail", "Whole" });

            // The compass (musexr-bb, 3c2b476): the lamp, then its two stands, then the arch - before the
            // paintings (30).
            var lampTarget = CompassTarget.Add(Lamp.gameObject, 21, "The lamp: grip to take it");
            Lamp.Grabbed += _ => lampTarget.MarkDone();
            var stands = new[] { CompassTarget.Add(Find("Detail Post").gameObject, 22, "DETAIL: set the lamp by the relief"),
                                 CompassTarget.Add(Find("Whole Post").gameObject, 22, "WHOLE: set the lamp at the rail") };
            Sockets.Cue += (s, e) => { if (e.Cue == SlotCue.Placed) foreach (var t in stands) t.MarkDone(); };
            // The rule: every painting and interactable object - click it or walk up to it, and a master speaks.
            InsightTarget.Add(Lamp.gameObject, "the brass lamp");
            InsightTarget.Add(relief.gameObject, "Buddhist Votive Stele", "a Western Wei carver (551)", "aic-29149");   // her design doc: the relief the lamp lights

            // Saul, 4 Oct: hear the companions on BOTH ways of seeing before the lamp can be set in either.
            // The lamp can still be taken and held to the relief (that is looking, not choosing); it only
            // will not seat until both sockets have been heard, and floats home if set down early.
            var detailTalk = InsightTarget.Add(Find("Detail Post").gameObject, "looking at the detail: the carved stele, close, under the lamp", "a way of seeing", "grotto-detail");
            var wholeTalk = InsightTarget.Add(Find("Whole Post").gameObject, "looking at the whole: the cliff Buddha across the sea of clouds", "a way of seeing", "grotto-whole");
            var mid = (detail.position + whole.position) * 0.5f;
            var toPosts = Flat(mid - entry);
            Preview = ChoicePreview.Make(transform, new Vector3(mid.x, entry.y + PreviewUp, mid.z) - toPosts * PreviewBefore, toPosts,
                "Stop 2  ·  detail or the whole",
                new[] { (detailTalk, "Look at detail  ·  the socket by the relief"), (wholeTalk, "Look at the whole  ·  the socket on the railing") },
                "Take the lamp. Put it where you want to see clearly");
            Sockets.SeatGate = () => Preview.Ready;
            Sockets.Refused += _ => Preview.Nudge();
            Sockets.Cue += (s, e) => { if (e.Cue == SlotCue.Placed && Preview != null) Preview.Close(); };
            foreach (var booth in new[] { ("Work Gandhara", "Buddha Worshipped by the Gods Indra and Brahma", "a Gandharan sculptor"),
                                           ("Work Tang / N. Wei", "Buddha", "a Tang dynasty sculptor") })
            {
                var w = Find(booth.Item1);
                if (w == null) continue;
                // The work itself (its Canvas), grabbable like every hung painting: tap to hear a master,
                // hold to take it down, two hands to scale it.
                var canvas = w.GetComponentInChildren<MeshRenderer>(true);
                InsightTarget.AddGrabbable(canvas != null ? canvas.gameObject : w.gameObject, booth.Item2, booth.Item3);
            }

            // The companions where her diagram stands them.
            var figures = new Dictionary<string, Transform>();
            foreach (var id in Masters.DefaultTrio) { var m = Find("Mark " + id); if (m != null) figures[id] = m; }
            var groupGo = new GameObject("Companions");
            groupGo.transform.SetParent(transform, false);
            Companions = groupGo.AddComponent<CompanionGroup>();
            Companions.FollowVisitor = false;
            Companions.Crowd = true;   // Saul, 3 Oct: always a crowd beside the visitor, never in front
            Companions.Head = head;
            var order = new List<string>(); foreach (var id in Masters.DefaultTrio) if (figures.ContainsKey(id)) order.Add(id);
            Companions.Set(order, figures);
            var subtitles = System.Type.GetType("MuseXR.UI.SubtitleRig, MuseXR.UI.Interaction");
            if (subtitles != null) groupGo.AddComponent(subtitles);

            var rim = BuddhaRim(entry);
            Label(whole.position + Vector3.up * 0.35f, entry, "The cliff Buddha: an AI rendition,\nnot a real site", 0.22f);

            BoothLabel("Work Gandhara", "GANDHARA", "Kushan period · 1st-2nd century");
            // Her design doc (2 Oct 2026) names AIC 86380, the Tang Buddha, for the right booth (it was Met 42719, Northern Wei).
            BoothLabel("Work Tang / N. Wei", "CHINA · TANG DYNASTY", "c. 725-750");

            Chapter = GrottoChapter.Make(gameObject, Sockets, Companions, Record, rim);
            _standFloor = standAt.position; _detailFloor = detailAt.position; _wholeFloor = wholeAt.position;
            BuildGuide();
            Chapter.Saved += _ =>
            {
                if (Record.Grotto != null) JourneyMemory.Record.SetGrotto(Record.Grotto);   // for the roundtable
                Debug.Log("[Record] " + Record.SummaryJson());
                OpenArch(teleportFloor, Camera.main != null ? Camera.main.transform.position : entry);
            };
        }

        // ---- the exit: her arch, cobalt and gold, into Van Gogh's studio ------------------------

        /// <summary>
        /// A brass stand on the floor at <paramref name="floorAt"/>: foot, column and tray, warm and
        /// unlit like the splats around it. Returns the tray's top, where the lamp sits.
        /// </summary>
        Vector3 BrassStand(Vector3 floorAt)
        {
            var brass = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            brass.SetColor("_BaseColor", new Color(0.72f, 0.55f, 0.27f));
            var root = new GameObject("Brass Stand").transform;
            root.SetParent(transform, false);
            root.position = floorAt;
            void Part(PrimitiveType t, string n, float y, Vector3 scale)
            {
                var g = GameObject.CreatePrimitive(t);
                g.name = n;
                DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(root, false);
                g.transform.localPosition = new Vector3(0f, y, 0f);
                g.transform.localScale = scale;
                g.GetComponent<Renderer>().sharedMaterial = brass;
            }
            Part(PrimitiveType.Cylinder, "Foot", 0.02f, new Vector3(0.32f, 0.02f, 0.32f));
            Part(PrimitiveType.Cylinder, "Column", StandHeight * 0.5f, new Vector3(0.05f, StandHeight * 0.5f, 0.05f));
            Part(PrimitiveType.Cylinder, "Tray", StandHeight - 0.01f, new Vector3(0.22f, 0.01f, 0.22f));
            return floorAt + Vector3.up * StandHeight;
        }

        MuseXR.Worlds.CaptureProbe _probe;

        /// <summary>Testing a journey: the chapter's interaction counts as done, and the arch rises.</summary>
        public void CompleteChapter() => OpenArch(_floor, Camera.main != null ? Camera.main.transform.position : Vector3.zero);

        /// <summary>After A keeps: the arch opens onto Van Gogh's studio; walking through leaves the grotto.</summary>
        void OpenArch(GameObject teleportFloor, Vector3 eye)
        {
            Label(Lamp.transform.position + Vector3.up * 0.5f, eye, "Kept.  The arch is open, ahead and to your right.\nWalk through it.", 0.3f);
            if (arch == null) { Debug.LogError("[Grotto] no arch Moon Gate in the scene"); return; }
            var here = FindAnyObjectByType<GaussianSplatting.Runtime.GaussianSplatRenderer>();
            foreach (var f in Companions.Figures.Values) f.SetParent(Companions.transform, true);   // they come along
            var props = new List<GameObject>();
            var layout = Find("Chapter Grotto"); if (layout != null) props.Add(layout.gameObject);
            var vis = GameObject.Find("Interaction Visuals"); if (vis != null) props.Add(vis);
            foreach (Transform c in transform) if (c != Companions.transform && c.gameObject != teleportFloor) props.Add(c.gameObject);
            if (_probe != null && _probe.Root != null) props.Add(_probe.Root.gameObject);
            if (!arch.Open(here, props, showNow: true)) return;
            var archTarget = CompassTarget.Add(arch.gameObject, 29, "The arch: walk through it");
            arch.Crossed += () => archTarget.MarkDone();
            Companions.StopTurns();
            Companions.FollowVisitor = true;
            arch.Crossed += () =>
            {
                // Van Gogh's floor: the flat teleport floor moves up to the threshold, under the arrival
                // (the gate's own landing pad covers the first 6 m).
                if (teleportFloor != null)
                {
                    teleportFloor.SetActive(false);
                    var at = arch.transform.position + arch.transform.forward * MuseXR.Worlds.MoonGate.ArrivalPastDoor;
                    teleportFloor.transform.position = new Vector3(at.x, arch.transform.position.y - 0.02f, at.z);
                    teleportFloor.SetActive(true);
                }
                Companions.PlaceAll();
                Debug.Log("[Grotto] through the arch: in Van Gogh's studio");
            };
        }

        // ---- the guide: what to do now, and where -----------------------------------------
        //
        // Saul, headset test 3 Oct 2026: "Where is the lantern? There is nothing to do... what am I
        // supposed to even do?" One instruction at a time, shown where the visitor is looking when the
        // step changes (and then it stays put - moving text made him sick), and a pulsing gold ring
        // on the floor at the place to go next.

        enum Step { None, TakeLamp, SetLamp, Listen, Keep, GoThrough, Done }
        Step _step = Step.None;
        Vector3 _standFloor, _detailFloor, _wholeFloor;
        TMPro.TextMeshPro _guideText;
        Transform _guide;
        readonly List<Renderer> _rings = new List<Renderer>();
        Material _ringMat;

        void BuildGuide()
        {
            _guide = new GameObject("Guide").transform;
            _guide.SetParent(transform, false);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Guide Plate";
            DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(_guide, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            plate.transform.localScale = new Vector3(1.0f, 0.3f, 1f);
            plate.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.13f, 0.1f, 0.08f));
            _guideText = new GameObject("Guide Text").AddComponent<TMPro.TextMeshPro>();
            _guideText.transform.SetParent(_guide, false);
            _guideText.rectTransform.sizeDelta = new Vector2(0.92f, 0.25f);
            _guideText.enableAutoSizing = true; _guideText.fontSizeMin = 0.25f; _guideText.fontSizeMax = 0.45f;
            _guideText.alignment = TMPro.TextAlignmentOptions.Center;
            _guideText.color = new Color(1f, 0.93f, 0.78f);

            _ringMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _ringMat.SetFloat("_Surface", 1f); _ringMat.SetFloat("_Blend", 2f);
            _ringMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _ringMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _ringMat.SetFloat("_ZWrite", 0f); _ringMat.SetFloat("_Cull", 0f);
            _ringMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _ringMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _ringMat.SetTexture("_BaseMap", RingTexture());
        }

        Step CurrentStep()
        {
            if (Chapter == null) return Step.None;
            if (arch != null && arch.IsOpen) return arch.Door != null && arch.Door.HasCrossed ? Step.Done : Step.GoThrough;
            if (Chapter.Flow.Current == GrottoFlow.Phase.Placed) return Chapter.Listening ? Step.Listen : Step.Keep;
            return Lamp != null && Lamp.State == Holdable.Mode.Held ? Step.SetLamp : Step.TakeLamp;
        }

        void UpdateGuide()
        {
            if (_guide == null) return;
            var step = CurrentStep();
            if (step != _step) { _step = step; ShowStep(step); }
            // the rings pulse in brightness only: nothing moves
            var k = 0.55f + 0.45f * Mathf.Sin(Time.time * 3f);
            _ringMat.SetColor("_BaseColor", new Color(1f, 0.74f, 0.3f) * k);
        }

        void ShowStep(Step step)
        {
            foreach (var r in _rings) if (r != null) Destroy(r.gameObject);
            _rings.Clear();
            string text = null;
            switch (step)
            {
                case Step.TakeLamp:
                    text = "<b>Take the lamp</b> from the brass stand on your left.\nPoint at it and hold GRIP.";
                    Ring(_standFloor);
                    break;
                case Step.SetLamp:
                    text = "Hold the lamp up to the <b>carved relief</b> on the left wall.\nThen set it on a stand: <b>DETAIL</b> by the relief, or <b>WHOLE</b> at the rail for the cliff Buddha.";
                    Ring(_detailFloor); Ring(_wholeFloor);
                    break;
                case Step.Listen:
                    text = "Your companions are speaking.\n<b>A</b>: next.";
                    break;
                case Step.Keep:
                    text = "<b>A</b> keeps the lamp here.\nOr lift it out and set it on the other stand.";
                    break;
                case Step.GoThrough:
                    text = "An <b>arch</b> has risen on your right, with Van Gogh's studio inside it.\nTeleport onto the step in front of it to go through.";
                    if (arch != null) Ring(arch.transform.position - arch.transform.forward * (MuseXR.Worlds.MoonGate.StepDepth * 0.5f));
                    break;
            }
            _guide.gameObject.SetActive(text != null);
            if (text == null) return;
            _guideText.text = text;
            // Where the visitor is looking now, a little below eye level; then it stays put.
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            var fwd = Flat(head.forward);
            var at = head.position + fwd * 1.6f + Vector3.up * -0.3f;
            // When the arch rises, the visitor looks at it: the panel goes beside it, never across the
            // opening (it covered Van Gogh's studio in the first capture).
            if (step == Step.GoThrough && arch != null)
                at = head.position + (Quaternion.Euler(0f, -35f, 0f) * fwd) * 1.6f + Vector3.up * -0.3f;   // close, off to the left
            _guide.SetPositionAndRotation(at, Quaternion.LookRotation(Flat(at - head.position), Vector3.up));
        }

        void Ring(Vector3 floorAt)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Go Here";
            DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.SetPositionAndRotation(new Vector3(floorAt.x, floorAt.y + 0.03f, floorAt.z), Quaternion.Euler(90f, 0f, 0f));
            q.transform.localScale = Vector3.one * 1.0f;
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = _ringMat;
            _rings.Add(r);
        }

        // ---- labels and icons --------------------------------------------------------------

        /// <summary>Her booths "with separate labels (region, period)": a plate under each booth's work, always shown.</summary>
        void BoothLabel(string work, string region, string period)
        {
            var w = Find(work);
            if (w == null) { Debug.LogWarning("[Grotto] no " + work); return; }
            var fwd = Flat(w.forward);   // a work faces INTO its wall: +Z away from the viewer reads
            var at = new Vector3(w.position.x, BoothLabelHeight, w.position.z) - fwd * 0.04f;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Booth Plate " + region;
            DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(transform, false);
            plate.transform.SetPositionAndRotation(at + fwd * 0.005f, Quaternion.LookRotation(fwd, Vector3.up));
            plate.transform.localScale = new Vector3(0.9f, 0.24f, 1f);
            plate.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.18f, 0.14f, 0.1f));
            var t = new GameObject("Booth Label " + region).AddComponent<TMPro.TextMeshPro>();
            t.transform.SetParent(transform, false);
            t.transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));
            t.rectTransform.sizeDelta = new Vector2(0.84f, 0.2f);
            t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = 0.6f;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.color = new Color(0.95f, 0.85f, 0.6f);
            t.text = "<b>" + region + "</b>\n<size=70%>" + period + "</size>";
        }

        /// <summary>Booth plates at eye level under the works (which hang 2.6-2.8 m up).</summary>
        public const float BoothLabelHeight = 1.55f;

        enum Icon { Magnifier, Mountain }

        static Material Unlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        /// <summary>
        /// Her socket text and icons (magnifier, mountain) that "stay legible under any light": dark
        /// ink on a cream plate, unlit, on the post's face toward the visitor's start.
        /// </summary>
        void SocketSign(Transform post, string word, Icon icon, Vector3 entry, float top)
        {
            var toEntry = Flat(entry - post.position);
            var face = post.position + toEntry * 0.085f;
            var rot = Quaternion.LookRotation(-toEntry, Vector3.up);   // +Z away from the viewer reads
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = word + " Sign";
            DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(transform, false);
            // Just under the post's top, above the slot card (which carries the word and covered a
            // sign lower down - capture, 3 Oct 2026).
            plate.transform.SetPositionAndRotation(new Vector3(face.x, top - 0.1f, face.z), rot);
            plate.transform.localScale = new Vector3(0.13f, 0.13f, 1f);
            var m = Unlit(Color.white);
            m.SetTexture("_BaseMap", IconTexture(icon));
            plate.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// <summary>A magnifier or a mountain in dark ink on cream.</summary>
        static Texture2D IconTexture(Icon icon)
        {
            const int w = 128, h = 128;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "socket-" + icon, wrapMode = TextureWrapMode.Clamp };
            var cream = new Color(0.95f, 0.91f, 0.82f); var ink = new Color(0.2f, 0.15f, 0.1f);
            var px = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;          // v up
                float iy = v;                                            // the whole plate
                bool on = false;
                if (iy >= 0f)
                {
                    float px0 = u - 0.5f, py0 = iy - 0.5f;
                    if (icon == Icon.Magnifier)
                    {
                        float cx = px0 + 0.08f, cy = py0 - 0.08f;
                        float r = Mathf.Sqrt(cx * cx + cy * cy);
                        on = Mathf.Abs(r - 0.2f) < 0.045f;                                   // the lens ring
                        float hx = px0 + 0.08f + 0.2f * 0.707f, hy = py0 - 0.08f + 0.2f * 0.707f;   // handle from the rim, down-left
                        float along = (-hx - hy) * 0.707f, across = (hx - hy) * 0.707f;
                        on |= along > 0f && along < 0.24f && Mathf.Abs(across) < 0.045f;
                    }
                    else
                    {
                        // two peaks on a base line
                        float b = -0.28f;
                        bool big = py0 > b && py0 < b + 0.5f - Mathf.Abs(px0 + 0.05f) * 1.3f;
                        bool small = py0 > b && py0 < b + 0.32f - Mathf.Abs(px0 - 0.22f) * 1.3f;
                        on = big || small;
                        bool snow = py0 > b + 0.38f - Mathf.Abs(px0 + 0.05f) * 1.3f && big;
                        if (snow) on = false;
                    }
                }
                px[y * w + x] = on ? ink : cream;
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>A stone post with the socket point on top, facing the visitor's start.</summary>
        Transform Socket(string name, Vector3 floorAt, float height, Vector3 entry, Material stone, Icon icon)
        {
            var floor = new Vector3(floorAt.x, Mathf.Max(0f, floorAt.y), floorAt.z);
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = name + " Post";
            post.transform.SetParent(transform, false);
            post.transform.position = floor + Vector3.up * (height * 0.5f - 0.02f);
            post.transform.localScale = new Vector3(0.16f, height, 0.16f);
            post.GetComponent<Renderer>().sharedMaterial = stone;
            SocketSign(post.transform, name, icon, entry, floor.y + height);
            var socket = new GameObject("Socket " + name).transform;
            socket.SetParent(transform, false);
            socket.SetPositionAndRotation(floor + Vector3.up * height, Quaternion.LookRotation(Flat(entry - floor), Vector3.up));
            return socket;
        }

        /// <summary>
        /// The gold rim: a mandorla - a soft gold ring the shape of the seated figure - round the
        /// distant Buddha. Additive, so it only adds gold light; splats write no depth, so it reads
        /// as round the figure from anywhere on the terrace, and it stands at the figure's measured
        /// distance so it stays on it as the visitor walks.
        /// </summary>
        Renderer BuddhaRim(Vector3 entry)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Buddha Rim";
            DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            var away = Flat(BuddhaCentre - entry);   // +Z away from the viewer reads (and Quad faces -Z)
            q.transform.SetPositionAndRotation(BuddhaCentre, Quaternion.LookRotation(away, Vector3.up));
            q.transform.localScale = new Vector3(RimSize.x, RimSize.y, 1f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", RingTexture());
            m.SetColor("_BaseColor", Color.black);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = m;
            return r;
        }

        /// <summary>An elliptical ring, bright at its edge and fading inwards and outwards.</summary>
        static Texture2D RingTexture()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "buddha-rim" };
            var px = new Color[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);   // 0 centre, 1 edge
                var a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.14f);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color(a, a, a, a);
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        void Label(Vector3 at, Vector3 viewer, string text, float size)
        {
            var t = new GameObject("Label").AddComponent<TMPro.TextMeshPro>();
            t.transform.SetParent(transform, false);
            t.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Flat(at - viewer), Vector3.up));
            t.rectTransform.sizeDelta = new Vector2(1.4f, 0.3f);
            t.fontSize = size;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.color = new Color(1f, 0.96f, 0.88f);
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(40, 28, 16, 255);
            t.text = text;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }

        /// <summary>An invisible floor at the terrace, teleportable everywhere (the rig selects on layer bit 31).</summary>
        GameObject TeleportFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Teleport Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(8f, 1f, 8f);   // 80 x 80 m: a safety net under the capture's own floor, which reaches the arch ~37 m out
            floor.GetComponent<Renderer>().enabled = false;
            floor.SetActive(false);                                  // XRI registers an area once, with its settings
            var area = floor.AddComponent<TeleportationArea>();
            area.interactionLayers = PalaceChapterInteractions.TeleportLayer;
            area.filterSelectionByHitNormal = true;
            floor.SetActive(true);
            return floor;
        }

        static Transform Find(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == name) return t;
            return null;
        }
    }
}
