using MusePico.Journey;
using MuseXR.Interaction;
using TMPro;
using UnityEngine;

namespace MuseXR.Journey
{
    /// <summary>
    /// The Gate's hero work from her updated design (MUSE-VR-design, hero works, P0): the Mona Lisa,
    /// enlarged, alone on a white wall. Aim at it and hold the trigger for a second to replicate a
    /// palm-sized framed copy into the satchel on the left wrist.
    ///
    /// Her plan stands it 6 m tall at the far end facing the arch; here it is 4 m tall beside the walk,
    /// clear of the lanterns and the moon gate, until the Gate's layout is redone to her new plan.
    /// Built at runtime from Resources/Heroes/mona-lisa.jpg (C2RMF scan via Wikimedia Commons, public domain).
    /// </summary>
    public sealed class GateHero : MonoBehaviour
    {
        public GateStage gate;
        public const float PictureHeight = 4f, Along = 11f, Aside = 3.4f;

        void Start()
        {
            if (gate == null) gate = FindAnyObjectByType<GateStage>();
            var tex = Resources.Load<Texture2D>("Heroes/mona-lisa");
            if (gate == null || tex == null) { Debug.LogWarning("[GateHero] no gate or no image"); return; }
            var from = gate.spawn != null ? gate.spawn.position : transform.position;
            var toDoor = gate.Doorway - from; toDoor.y = 0f; toDoor.Normalize();
            var right = Vector3.Cross(Vector3.up, toDoor).normalized;
            var at = from + toDoor * Along + right * Aside;
            var floor = at.y;
            if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) floor = hit.point.y;
            var toWalk = (from + toDoor * (Along - 3f)) - at; toWalk.y = 0f; toWalk.Normalize();
            var faces = Quaternion.LookRotation(-toWalk, Vector3.up);   // +Z away from the viewer reads

            var root = new GameObject("Hero · Mona Lisa").transform;
            root.SetParent(transform, false);
            root.SetPositionAndRotation(new Vector3(at.x, floor, at.z), faces);

            float w = PictureHeight * tex.width / tex.height, h = PictureHeight;
            Quad(root, "Wall", new Vector3(0f, (h + 1.2f) / 2f, 0.03f), new Vector2(w + 1.6f, h + 1.2f), new Color(0.95f, 0.94f, 0.91f), null);

            // The picture and its frame are what is replicated: a palm-sized framed Mona Lisa.
            var picture = new GameObject("Picture").transform;
            picture.SetParent(root, false);
            picture.localPosition = new Vector3(0f, 0.6f + h / 2f, 0f);
            Quad(picture, "Canvas", Vector3.zero, new Vector2(w, h), Color.white, tex);
            var gold = new Color(0.72f, 0.56f, 0.28f);
            const float f = 0.12f;
            Bar(picture, new Vector3(0f, h / 2f + f / 2f, -0.02f), new Vector3(w + 2 * f, f, 0.06f), gold);
            Bar(picture, new Vector3(0f, -h / 2f - f / 2f, -0.02f), new Vector3(w + 2 * f, f, 0.06f), gold);
            Bar(picture, new Vector3(-w / 2f - f / 2f, 0f, -0.02f), new Vector3(f, h, 0.06f), gold);
            Bar(picture, new Vector3(w / 2f + f / 2f, 0f, -0.02f), new Vector3(f, h, 0.06f), gold);
            var box = picture.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(w + 2 * f, h + 2 * f, 0.1f);
            Replicable.Make(picture.gameObject, "Mona Lisa", "gate").replicaName = "Palm-sized framed Mona Lisa";
            CompassTarget.Add(picture.gameObject, 6, "Mona Lisa", "Hold the trigger to replicate");

            // Her label, on the wall below the frame.
            var label = new GameObject("Label").transform;
            label.SetParent(root, false);
            label.localPosition = new Vector3(0f, 0.32f, -0.01f);
            var t = label.gameObject.AddComponent<TextMeshPro>();
            t.text = "<b>Mona Lisa</b>  ·  Leonardo da Vinci  ·  c. 1503–1519  ·  Louvre\n<size=75%>Enlarged about 5×, original 77 × 53 cm  ·  Aim and hold the trigger to replicate</size>";
            t.fontSize = 1.1f; t.alignment = TextAlignmentOptions.Center; t.color = new Color(0.2f, 0.17f, 0.14f);
            t.rectTransform.sizeDelta = new Vector2(w + 1.4f, 0.5f);
            Debug.Log("[GateHero] Mona Lisa stands at " + root.position.ToString("F1"));
            _heroes.Add(root.gameObject);
            if (FindAnyObjectByType<ChapterFeatures>() == null) gameObject.AddComponent<ChapterFeatures>();
            try { BuildVenus(from, toDoor, right); }
            catch (System.Exception ex) { Debug.LogError("[GateHero] Venus: " + ex); }
        }

        [Tooltip("Assets/Art/Heroes/venus-de-milo.glb - SMK's CC0 scan of a plaster cast.")]
        public GameObject venusModel;

        /// <summary>
        /// Her second Gate hero: the Venus de Milo, life size (~2 m), on a white stone drum in the reflecting
        /// pool on the left of the walk; replicable as a 30 cm white stone figurine.
        /// </summary>
        void BuildVenus(Vector3 from, Vector3 toDoor, Vector3 right)
        {
            var model = venusModel;
#if UNITY_EDITOR
            if (model == null) model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Heroes/venus-de-milo.glb");
#endif
            if (model == null) { Debug.LogWarning("[GateHero] no Venus model"); return; }
            var at = from + toDoor * VenusAlong - right * VenusAside;
            var floor = at.y;
            if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) floor = hit.point.y;
            var root = new GameObject("Hero · Venus de Milo").transform;
            root.SetParent(transform, false);
            root.position = new Vector3(at.x, floor, at.z);
            var toWalk = (from + toDoor * VenusAlong) - root.position; toWalk.y = 0f;
            root.rotation = Quaternion.LookRotation(toWalk.normalized, Vector3.up);
            var drum = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            drum.name = "Drum"; Destroy(drum.GetComponent<Collider>());
            drum.transform.SetParent(root, false);
            drum.transform.localPosition = new Vector3(0f, 0.3f, 0f); drum.transform.localScale = new Vector3(0.9f, 0.3f, 0.9f);
            var stone = new Material(Shader.Find("Universal Render Pipeline/Lit")); stone.SetColor("_BaseColor", new Color(0.93f, 0.92f, 0.89f));
            drum.GetComponent<Renderer>().sharedMaterial = stone;
            var figure = Instantiate(model, root);
            figure.name = "Venus";
            var rs = figure.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.y > 1e-4f) figure.transform.localScale *= VenusHeight / b.size.y;
                b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                figure.transform.position += Vector3.up * (root.position.y + 0.6f - b.min.y);
                var c = b.center; figure.transform.position += new Vector3(root.position.x - c.x, 0f, root.position.z - c.z);
            }
            var box = figure.AddComponent<BoxCollider>();
            var lb = new Bounds(figure.transform.InverseTransformPoint(rs.Length > 0 ? rs[0].bounds.center : figure.transform.position), Vector3.zero);
            foreach (var r in rs) { lb.Encapsulate(figure.transform.InverseTransformPoint(r.bounds.min)); lb.Encapsulate(figure.transform.InverseTransformPoint(r.bounds.max)); }
            box.center = lb.center; box.size = lb.size;
            Replicable.Make(figure, "Venus de Milo", "gate-venus").replicaName = "A 30 cm white stone figurine";
            var label = new GameObject("Label").transform;
            label.SetParent(root, false);
            label.localPosition = new Vector3(0f, 0.35f, 0.92f);
            label.localRotation = Quaternion.LookRotation(-Vector3.forward);
            label.localRotation = Quaternion.identity;
            var t = label.gameObject.AddComponent<TextMeshPro>();
            t.text = "<b>Venus de Milo</b>  ·  after the Greek original, c. 150–125 BCE\n<size=70%>3D scan of a plaster cast, SMK National Gallery of Denmark (CC0)</size>";
            t.fontSize = 0.7f; t.alignment = TextAlignmentOptions.Center; t.color = new Color(0.2f, 0.17f, 0.14f);
            t.rectTransform.sizeDelta = new Vector2(1.8f, 0.3f);
            // AddComponent<TextMeshPro> swaps the Transform for a RectTransform: the old reference is dead.
            t.transform.position = root.position + root.forward * 0.95f + Vector3.up * 0.35f;
            t.transform.rotation = Quaternion.LookRotation(-root.forward);
            _heroes.Add(root.gameObject);
        }

        public const float VenusAlong = 8.5f, VenusAside = 3.6f, VenusHeight = 2.05f;

        readonly System.Collections.Generic.List<GameObject> _heroes = new System.Collections.Generic.List<GameObject>();

        /// <summary>The Gate's works belong to the conservatory: when the Gate is cleared away on the way into
        /// the Palace they go with it (they hang under Journey Opening, which lives on through every world).</summary>
        void Update()
        {
            if (_heroes.Count == 0 || (gate != null && gate.isActiveAndEnabled)) return;
            foreach (var h in _heroes) if (h != null) Destroy(h);
            _heroes.Clear();
        }

        static void Quad(Transform parent, string name, Vector3 local, Vector2 size, Color colour, Texture2D tex)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name; Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = local;
            q.transform.localRotation = Quaternion.identity;   // a Quad faces -Z: with +Z away from the viewer it reads
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", colour);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            q.GetComponent<Renderer>().sharedMaterial = m;
        }

        static void Bar(Transform parent, Vector3 local, Vector3 size, Color colour)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "Frame"; Destroy(c.GetComponent<Collider>());
            c.transform.SetParent(parent, false);
            c.transform.localPosition = local; c.transform.localScale = size;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", colour); m.SetFloat("_Metallic", 0.7f); m.SetFloat("_Smoothness", 0.55f);
            c.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
