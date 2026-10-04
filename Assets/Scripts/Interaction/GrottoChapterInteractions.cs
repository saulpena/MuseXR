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
    /// Positions in the capture were found by eye from Editor captures (3 Oct 2026), not from the
    /// collider: the brass stand from the entry (bearing 152, base 6.6 m out); the Buddha by
    /// triangulating the entry (bearing 183.4, 22.6 deg up) and the rail (bearing 185.1, 26 deg up).
    /// </summary>
    public sealed class GrottoChapterInteractions : MonoBehaviour
    {
        [Tooltip("Assets/Art/Props/lamp.glb.")]
        public GameObject lampPrefab;

        public GrottoChapter Chapter { get; private set; }
        public SlotStation Sockets { get; private set; }
        public CompanionGroup Companions { get; private set; }
        public Holdable Lamp { get; private set; }
        public MusePico.Dialogue.JourneyRecord Record { get; } = new MusePico.Dialogue.JourneyRecord();

        /// <summary>The top of the capture's brass lampstand, front-left of the entry.</summary>
        public static readonly Vector3 StandTop = new Vector3(3.1f, 1.58f, -5.0f);
        /// <summary>Where the lamp's flame sits in the generated lamp, its own space (InteractionsDemo's value).</summary>
        public static readonly Vector3 LampFlame = new Vector3(0f, 0.31f, 0f);
        /// <summary>The cliff Buddha's centre, and its size (about 30 m seated).</summary>
        public static readonly Vector3 BuddhaCentre = new Vector3(-5.2f, 33.8f, -76.3f);
        public static readonly Vector2 RimSize = new Vector2(38f, 42f);
        /// <summary>How far the "detail" post stands out from the relief: clear of the niche's altar ledge.</summary>
        public const float DetailFromRelief = 0.75f;
        /// <summary>LampLight's intensity is tuned for 0.43 m from the relief; it is scaled by distance squared so
        /// the carving reads the same from the socket, capped so a lamp held right against it never clips.</summary>
        public const float TunedDistance = 0.43f, MaxBoost = 6f;

        Transform _relief;
        LampLight _lampLight;

        void Update()
        {
            if (_relief == null || _lampLight == null || _lampLight.Light == null) return;
            var d = Vector3.Distance(_lampLight.Light.transform.position, _relief.position);
            var k = Mathf.Clamp((d * d) / (TunedDistance * TunedDistance), 1f, MaxBoost);
            _lampLight.Light.intensity = LampLight.Intensity * k;
            _lampLight.Light.range = Mathf.Max(LampLight.Range, d + 1f);
        }

        /// <summary>Socket heights above the floor: hand height on a post (her 0.8-1.3 m).</summary>
        public const float DetailHeight = 1.0f, WholeHeight = 1.05f;

        IEnumerator Start()
        {
            yield return null;   // after the rig and the layout have woken
            var head = Camera.main != null ? Camera.main.transform : null;
            var entry = head != null ? head.position : Vector3.zero;

            TeleportFloor();

            var relief = Find("Prop relief");
            var detailAt = Find("Interaction Socket · detail");
            var wholeAt = Find("Interaction Socket · whole");
            if (relief == null || detailAt == null || wholeAt == null)
            {
                Debug.LogError("[Grotto] layout objects missing: relief " + (relief != null) + ", detail " + (detailAt != null) + ", whole " + (wholeAt != null));
                yield break;
            }
            foreach (var r in relief.GetComponentsInChildren<Renderer>()) LampLight.MarkRelief(r);

            // Unlit cream: the splat world takes no light, and a lit post read as a blue-grey box (capture).
            var stone = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            stone.SetColor("_BaseColor", new Color(0.86f, 0.79f, 0.67f));
            // "detail" stands just before the niche relief: the layout's point is 1.5 m out, too far for
            // the lamp to light the carving (capture, 3 Oct 2026). In front of the niche's altar ledge.
            var reliefFacing = Flat(relief.forward);
            if (Vector3.Dot(reliefFacing, entry - relief.position) < 0f) reliefFacing = -reliefFacing;
            var detailFloor = new Vector3(relief.position.x, 0f, relief.position.z) + reliefFacing * DetailFromRelief;
            var detail = Socket("Detail", detailFloor, DetailHeight, entry, stone);
            _relief = relief;
            var whole = Socket("Whole", wholeAt.position, WholeHeight, entry, stone);

            // The lamp on the capture's brass stand.
            GameObject lamp;
            var face = Quaternion.LookRotation(Flat(entry - StandTop), Vector3.up);
            if (lampPrefab != null) { lamp = Instantiate(lampPrefab, StandTop, face, transform); lamp.name = "Lamp"; }
            else { lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere); lamp.name = "Lamp"; lamp.transform.SetPositionAndRotation(StandTop + Vector3.up * 0.08f, face); lamp.transform.localScale = Vector3.one * 0.15f; }
            _lampLight = LampLight.Make(lamp, lampPrefab != null ? LampFlame : Vector3.zero);
            Lamp = Holdable.Make(lamp, "Lamp", idleSpin: false);

            Sockets = SlotStation.Make(gameObject, global::MuseXR.Slots.Chapter.Grotto, new[] { detail, whole }, new[] { Lamp },
                                       new[] { "Detail", "Whole" });

            // The companions where her diagram stands them.
            var figures = new Dictionary<string, Transform>();
            foreach (var id in Masters.DefaultTrio) { var m = Find("Mark " + id); if (m != null) figures[id] = m; }
            var groupGo = new GameObject("Companions");
            groupGo.transform.SetParent(transform, false);
            Companions = groupGo.AddComponent<CompanionGroup>();
            Companions.FollowVisitor = false;
            Companions.Head = head;
            var order = new List<string>(); foreach (var id in Masters.DefaultTrio) if (figures.ContainsKey(id)) order.Add(id);
            Companions.Set(order, figures);
            var subtitles = System.Type.GetType("MuseXR.UI.SubtitleRig, MuseXR.UI.Interaction");
            if (subtitles != null) groupGo.AddComponent(subtitles);

            var rim = BuddhaRim(entry);
            Label(whole.position + Vector3.up * 0.35f, entry, "Cliff Buddha: an AI rendition\nreferencing the Longmen Vairocana form", 0.22f);

            Chapter = GrottoChapter.Make(gameObject, Sockets, Companions, Record, rim);
            Chapter.Saved += _ =>
            {
                Debug.Log("[Record] " + Record.SummaryJson());
                Label(Lamp.transform.position + Vector3.up * 0.5f, Camera.main != null ? Camera.main.transform.position : entry, "Kept.", 0.36f);
            };
        }

        /// <summary>A stone post with the socket point on top, facing the visitor's start.</summary>
        Transform Socket(string name, Vector3 floorAt, float height, Vector3 entry, Material stone)
        {
            var floor = new Vector3(floorAt.x, Mathf.Max(0f, floorAt.y), floorAt.z);
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = name + " Post";
            post.transform.SetParent(transform, false);
            post.transform.position = floor + Vector3.up * (height * 0.5f - 0.02f);
            post.transform.localScale = new Vector3(0.16f, height, 0.16f);
            post.GetComponent<Renderer>().sharedMaterial = stone;
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
        void TeleportFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Teleport Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(4f, 1f, 4f);   // 40 x 40 m
            floor.GetComponent<Renderer>().enabled = false;
            floor.SetActive(false);                                  // XRI registers an area once, with its settings
            var area = floor.AddComponent<TeleportationArea>();
            area.interactionLayers = PalaceChapterInteractions.TeleportLayer;
            area.filterSelectionByHitNormal = true;
            floor.SetActive(true);
        }

        static Transform Find(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == name) return t;
            return null;
        }
    }
}
