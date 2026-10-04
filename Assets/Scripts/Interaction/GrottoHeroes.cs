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

        void Start()
        {
            var entry = transform.TransformPoint(Vector3.zero);
            if (seatedBuddha != null)
            {
                Cliff = Place(seatedBuddha, "Hero · golden seated Buddha", transform.TransformPoint(new Vector3(CliffBuddha.x, CliffBaseY, CliffBuddha.z)), entry, CliffHeight);
                // Her "aim and hold the trigger": a small gold Buddha at the rail, within reach, stands for it.
                var mini = Place(seatedBuddha, "Golden seated Buddha (replicate)", transform.TransformPoint(MiniatureAt), entry, 0.35f);
                var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stand.name = "Stand"; Destroy(stand.GetComponent<Collider>());
                stand.transform.SetParent(transform, false);
                stand.transform.position = transform.TransformPoint(new Vector3(MiniatureAt.x, MiniatureAt.y * 0.5f, MiniatureAt.z));
                stand.transform.localScale = new Vector3(0.32f, MiniatureAt.y * 0.5f, 0.32f);
                stand.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color32(0xec, 0xe6, 0xd8, 0xff) };
                Replicable.Make(mini, "Golden seated Buddha", "grotto").replicaName = "A 20 cm gold Buddha";
                Label(mini, "Golden seated Buddha  ·  30 m on the cliff", "AI original sculpture, not a real site  ·  hold the trigger to replicate");
            }
            if (fiveBuddhas != null)
            {
                var at = transform.TransformPoint(fiveAt);
                var five = Place(fiveBuddhas, "Hero · five golden Buddhas", at, entry, fiveHeight * 2f);
                Label(five, "Five golden Buddhas", "AI original sculpture  ·  Tripo");
                InsightTarget.AddGrabbable(five, "the five golden Buddhas on the lotus terrace", "an AI sculptor (an original, not an artefact)", "hero-five-buddhas");
            }
            if (guanyinGroup != null)
            {
                var at = transform.TransformPoint(guanyinAt);
                var g = Place(guanyinGroup, "Hero · Guanyin group", at, entry, guanyinHeight);
                Label(g, "Guanyin group under plum branches", "AI original sculpture  ·  Tripo");
                InsightTarget.Add(g, "the Guanyin group under the plum branches", "an AI sculptor (an original, not an artefact)", "hero-guanyin");
            }
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

        void Label(GameObject hero, string title, string sub)
        {
            var b = Bounds(hero);
            var eye = transform.TransformPoint(new Vector3(0f, 1.6f, 0f));
            var at = new Vector3(b.center.x, b.min.y + 0.9f, b.center.z);
            var toEye = eye - at; toEye.y = 0f;
            at += toEye.normalized * (Mathf.Max(b.extents.x, b.extents.z) + 0.15f);   // in front of the work
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
