using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The Grotto's hero works (MUSE-VR-design, 2 Oct 2026, chapter B), from Saul's Tripo models
    /// (Assets/Props/Peach, the same models as her previews):
    ///   - the golden seated Buddha, ~30-40 m on the far cliff over the sea of clouds, in place of the
    ///     capture's own cliff Buddha; one-tap replicate gives "a 20 cm gold Buddha";
    ///   - the five golden Buddhas on their white lotus platform, ~2 m figures, on the terrace;
    ///   - the Guanyin group under plum branches, beside the arch out, "as if seeing you off".
    /// Each is labelled "AI original sculpture" (her honesty rule: not an artefact, not a real site).
    /// Positions are in this object's space (the Grotto at the origin), so it works in its own scene and
    /// chained into GateWorld alike.
    /// </summary>
    public sealed class GrottoHeroes : MonoBehaviour
    {
        [Tooltip("Assets/Props/Peach/buddha-statue.gltf - the golden seated Buddha.")]
        public GameObject seatedBuddha;
        [Tooltip("Assets/Props/Peach/golden-buddha-statues.gltf - five Buddhas on a lotus terrace.")]
        public GameObject fiveBuddhas;
        [Tooltip("Assets/Props/Peach/golden-deity-statue.gltf - the Guanyin group under plum branches.")]
        public GameObject guanyinGroup;

        /// <summary>The capture's cliff Buddha, found by triangulating two captures (GrottoChapterInteractions).</summary>
        public static readonly Vector3 CliffBuddha = GrottoChapterInteractions.BuddhaCentre;
        /// <summary>
        /// The model's seated figure is a third of its height (lotus and plinth below), so to cover the
        /// capture's own Buddha figure for figure the whole model is ~87 m, its base in the clouds at
        /// -12 m. Fitted by eye against the bare capture from the entry (4 Oct): at 42 m it was a small
        /// gold figure on a floating dark plinth with the white Buddha still showing.
        /// </summary>
        public const float CliffHeight = 87f, CliffBaseY = -12.1f;
        /// <summary>Where the visitor replicates it from: the far statue is ~80 m away, the pointer reaches 8.</summary>
        public static readonly Vector3 MiniatureAt = new Vector3(1.6f, 1.05f, -11.3f);

        [System.NonSerialized] public Vector3 fiveAt = new Vector3(-2.6f, 0f, -4.2f);
        [System.NonSerialized] public float fiveHeight = 2.0f;   // the figures: the platform makes the model ~2x their height
        [System.NonSerialized] public Vector3 guanyinAt = new Vector3(-6.4f, 0f, -10.2f);
        [System.NonSerialized] public float guanyinHeight = 2.2f;

        public GameObject Cliff { get; private set; }

        /// <summary>Her "when you light the nearby relief with the lamp, a small light also glows at the far Buddha's chest, as if answering".</summary>
        public const float LampNearRelief = 1.6f;
        Transform _chest, _relief;
        Light _chestLight;
        Material _chestGlow;
        GrottoChapterInteractions _grotto;
        float _glow;

        void Start()
        {
            var entry = transform.TransformPoint(Vector3.zero);
            if (seatedBuddha != null)
            {
                Cliff = Place(seatedBuddha, "Hero · golden seated Buddha", transform.TransformPoint(new Vector3(CliffBuddha.x, CliffBaseY, CliffBuddha.z)), entry, CliffHeight);
                // Her "aim and hold the trigger": a small gold Buddha at the rail, within reach, stands for it.
                var mini = Place(seatedBuddha, "Golden seated Buddha (replicate)", transform.TransformPoint(MiniatureAt), entry, 0.35f);
                // On the same carved sandstone post as the lamp's sockets (generated; Saul, 4 Oct: no shapes made in code).
                var standAt = transform.TransformPoint(new Vector3(MiniatureAt.x, 0f, MiniatureAt.z));
                var stand = PropModels.Spawn("socket-post", transform, standAt, mini.transform.rotation, new Vector3(0f, transform.TransformPoint(MiniatureAt).y - standAt.y, 0f));
                if (stand != null) stand.name = "Stand";
                Replicable.Make(mini, "Golden seated Buddha", "grotto").replicaName = "A 20 cm gold Buddha";
                Label(mini, "Golden seated Buddha  ·  30 m on the cliff", "AI original sculpture, not a real site  ·  hold the trigger to replicate");
                ChestGlow(entry);
            }
            if (fiveBuddhas != null)
            {
                var at = transform.TransformPoint(fiveAt);
                var five = Place(fiveBuddhas, "Hero · five golden Buddhas", at, entry, fiveHeight * 2f);
                Label(five, "Five golden Buddhas", "AI original sculpture  ·  Tripo");
                PointableBounds(five);
                InsightTarget.Add(five, "the five golden Buddhas on the lotus terrace", "an AI sculptor (an original, not an artefact)", "hero-five-buddhas");
            }
            if (guanyinGroup != null)
            {
                var at = transform.TransformPoint(guanyinAt);
                var g = Place(guanyinGroup, "Hero · Guanyin group", at, entry, guanyinHeight);
                Label(g, "Guanyin group under plum branches", "AI original sculpture  ·  Tripo");
                PointableBounds(g);
                InsightTarget.Add(g, "the Guanyin group under the plum branches", "an AI sculptor (an original, not an artefact)", "hero-guanyin");
            }
        }

        void ChestGlow(Vector3 entry)
        {
            // The figure is the model's upper third: its chest about a fifth down from the top, in front.
            var b = Bounds(Cliff);
            var toEntry = entry - b.center; toEntry.y = 0f; toEntry.Normalize();
            _chest = new GameObject("Chest glow").transform;
            _chest.SetParent(Cliff.transform, true);
            _chest.position = new Vector3(b.center.x, b.max.y - b.size.y * 0.2f, b.center.z) + toEntry * (b.extents.z * 0.35f);
            _chest.rotation = Quaternion.LookRotation(-toEntry, Vector3.up);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Glow"; Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(_chest, false);
            q.transform.localScale = Vector3.one * (b.size.y * 0.09f);
            _chestGlow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _chestGlow.SetFloat("_Surface", 1f); _chestGlow.SetFloat("_Blend", 2f);   // additive
            _chestGlow.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _chestGlow.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _chestGlow.SetFloat("_ZWrite", 0f);
            _chestGlow.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _chestGlow.renderQueue = 3100;
            _chestGlow.SetTexture("_BaseMap", SoftDot());
            _chestGlow.SetColor("_BaseColor", new Color(1f, 0.82f, 0.45f, 0f));
            q.GetComponent<Renderer>().sharedMaterial = _chestGlow;
            _chestLight = _chest.gameObject.AddComponent<Light>();
            _chestLight.type = LightType.Point; _chestLight.color = new Color(1f, 0.8f, 0.45f);
            _chestLight.range = b.size.y * 0.25f; _chestLight.intensity = 0f;
        }

        static Texture2D SoftDot()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var d = new Vector2(x - n * 0.5f + 0.5f, y - n * 0.5f + 0.5f).magnitude / (n * 0.5f);
                    var a = Mathf.Clamp01(1f - d); a *= a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }

        void Update()
        {
            if (_chest == null) return;
            if (_grotto == null) _grotto = FindAnyObjectByType<GrottoChapterInteractions>();
            if (_relief == null && _grotto != null)
                foreach (var t in _grotto.transform.root.GetComponentsInChildren<Transform>(true)) if (t.name == "Prop relief") { _relief = t; break; }
            if (_relief == null && transform.parent == null)
                foreach (var r in gameObject.scene.GetRootGameObjects()) foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == "Prop relief") _relief = t;
            var lamp = _grotto != null && _grotto.Lamp != null ? _grotto.Lamp.transform : null;
            bool lit = lamp != null && _relief != null && Vector3.Distance(lamp.position, _relief.position) < LampNearRelief;
            _glow = Mathf.MoveTowards(_glow, lit ? 1f : 0f, Time.deltaTime / 1.2f);
            _chestGlow.SetColor("_BaseColor", new Color(1f, 0.82f, 0.45f, 0.85f * _glow));
            _chestLight.intensity = 6f * _glow;
        }

        /// <summary>The model at <paramref name="height"/> metres, its base on <paramref name="floor"/>, facing the visitor's entry.</summary>
        GameObject Place(GameObject model, string name, Vector3 floor, Vector3 faceToward, float height)
        {
            var go = Instantiate(model, transform);
            go.name = name;
            var toEntry = faceToward - floor; toEntry.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(toEntry.sqrMagnitude > 1e-4f ? toEntry.normalized : Vector3.forward, Vector3.up);
            go.transform.localScale = Vector3.one;
            var b = Bounds(go);
            if (b.size.y > 1e-4f) go.transform.localScale = Vector3.one * (height / b.size.y);
            b = Bounds(go);
            go.transform.position += new Vector3(floor.x - b.center.x, floor.y - b.min.y, floor.z - b.center.z);
            return go;
        }

        /// <summary>
        /// A trigger box round what is drawn: the pointer reaches it (it collides with triggers), the
        /// visitor's body walks round it. The first version made the five Buddhas grabbable, which needs a
        /// solid collider - a 4 m wall across the walk to the arch (full-walk test, 4 Oct). A statue group
        /// that size is not something to pick up anyway.
        /// </summary>
        static void PointableBounds(GameObject hero)
        {
            var b = Bounds(hero);
            var box = hero.AddComponent<BoxCollider>();
            box.center = hero.transform.InverseTransformPoint(b.center);
            var s = hero.transform.lossyScale;
            box.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z);
            box.isTrigger = true;
        }

        /// <summary>No hero's label stands nearer the arrival than this, metres.</summary>
        public const float LabelNearest = 2.5f;

        void Label(GameObject hero, string title, string sub)
        {
            var b = Bounds(hero);
            var eye = transform.TransformPoint(new Vector3(0f, 1.6f, 0f));
            var at = new Vector3(b.center.x, b.min.y + 0.9f, b.center.z);
            var toEye = eye - at; toEye.y = 0f;
            // In front of the work, but never at the visitor: the five Buddhas spread so wide that their
            // half-width carried the label past them onto the arrival spot, a huge plate at the knees
            // (capture, 4 Oct).
            var push = Mathf.Min(Mathf.Max(b.extents.x, b.extents.z) + 0.15f, toEye.magnitude - LabelNearest);
            at += toEye.normalized * Mathf.Max(0f, push);
            var anchor = new GameObject("Label " + title).transform;
            anchor.SetParent(hero.transform, true);
            anchor.SetPositionAndRotation(at, Quaternion.LookRotation(-toEye.normalized, Vector3.up));   // +Z away from the viewer reads
            var c = MuseUi.Canvas(anchor, "Label", 3f, 230f);
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 10f, padY: 7f, gap: 2f, name: "Plate");
            card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var t = MuseUi.Text(card, title, MuseUi.Face.SansSemi, 11f, MuseTheme.Ink, name: "Title"); t.alignment = TextAlignmentOptions.Center;
            var s = MuseUi.Text(card, sub, MuseUi.Face.Sans, 9f, MuseTheme.Ink3, name: "Sub"); s.alignment = TextAlignmentOptions.Center;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
    }
}
