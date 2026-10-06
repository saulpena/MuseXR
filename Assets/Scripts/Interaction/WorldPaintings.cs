using System.Collections.Generic;
using MusePico.Dialogue;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A world's paintings, the same works at the same places as the journey hangs them in
    /// Museum.unity (MuseumJourneyRunner.HangHerWay): her collection from artworks.json, laid out by
    /// <see cref="MuseXR.Worlds.WebGalleryLayout.Place"/> from the world's playtested spawn and facing,
    /// asking the world's collider her ground and wall questions, each canvas centred 1.6 m above the
    /// visitor's floor in her walnut / mat / gold frame. The world must stand at the origin, as it does
    /// in every chapter scene and in the journey. Each work is a compass target (order 30) that is done
    /// once pointed at.
    /// </summary>
    public sealed class WorldPaintings : MonoBehaviour
    {
        [Tooltip("The world's key in WorldCatalog.Small, e.g. van-gogh-inspired-gallery-interior-500k.")]
        public string worldKey;
        [Tooltip("Her collection in artworks.json (chapters[].sceneId), e.g. burning-sky.")]
        public string collectionId;
        [Tooltip("Assets/Museum/artworks.json.")]
        public TextAsset artworksJson;
        [Tooltip("The works' images, named by id (the same textures Museum.unity's runner holds).")]
        public Texture2D[] artworkImages;
        [Tooltip("The world's collider (Assets/Worlds/Colliders/*-collider.glb), for her wall and floor questions.")]
        public GameObject colliderModel;

        /// <summary>Museum.unity's: canvas centre above the visitor's floor.</summary>
        public const float HangAboveFloor = 1.6f;
        public const int CompassOrder = 30;

        /// <summary>
        /// One work hung where her design doc puts it (MUSE-VR-design, 2 Oct 2026): usually inside one of
        /// the capture's own empty frames, filling its canvas at the work's true proportions (the pale
        /// capture canvas around it reads as a mat), or free-standing in her walnut frame.
        /// </summary>
        [System.Serializable]
        public class Placement
        {
            [Tooltip("The work's id in artworks.json, e.g. aic-80607.")]
            public string id;
            [Tooltip("Centre of the canvas, world space (the world stands at the origin).")]
            public Vector3 centre;
            [Tooltip("The way the canvas faces: toward the visitor, out of the wall.")]
            public Vector3 facing = Vector3.forward;
            [Tooltip("Stands on an easel instead of hanging: for a spot with no wall behind it (Saul, 5 Oct). centre.y is then ignored.")]
            public bool easel;
            [Tooltip("With easel: the floor under it (frame space), measured from the world's collider.")]
            public float floor;
            [Tooltip("The canvas it must fit inside, metres (width, height); the work keeps its proportions.")]
            public Vector2 canvas = new Vector2(1.2f, 1.2f);
            [Tooltip("Her walnut / mat / gold frame round it: off when it hangs in a frame the capture already has.")]
            public bool frame;
            [Tooltip("Fill the whole canvas, cropping the work to its shape (never stretching) round the focus point, instead of matting it.")]
            public bool fill;
            [Tooltip("With fill: the point of the work the crop keeps centred, 0-1 across and up (e.g. where the figure stands).")]
            public Vector2 focus = new Vector2(0.5f, 0.5f);
        }

        [Tooltip("Her design doc's positions. Empty: the journey's automatic layout (WebGalleryLayout).")]
        public Placement[] placements;

        public List<Transform> Hung { get; } = new List<Transform>();

        void Start() => Hang();

        public void Hang()
        {
            if (placements != null && placements.Length > 0) { HangPlaced(); return; }
            MuseXR.Worlds.WorldDefinition world = null;
            foreach (var w in MuseXR.Worlds.WorldCatalog.Small) if (w.key == worldKey) world = w;
            if (world == null || artworksJson == null) { Debug.LogError("[Paintings] world '" + worldKey + "' or artworks.json missing"); return; }
            var works = ArtworkCatalog.For(ArtworkCatalog.Parse(artworksJson.text), collectionId);
            if (works.Count == 0) { Debug.LogError("[Paintings] no works in collection '" + collectionId + "'"); return; }

            var probe = colliderModel != null ? MuseXR.Worlds.CaptureProbe.Open(world, new[] { colliderModel }) : null;
            // The visitor's floor as the journey takes it: the capture's floor at the spawn, else the playtested groundY.
            var spawnXZ = world.ScaledSpawn;
            float floorY = world.groundY * world.worldScale;
            if (probe != null) { var f = probe.Floor(new Vector3(spawnXZ.x, floorY, spawnXZ.z)); if (!float.IsNaN(f)) floorY = f; }
            Bounds? walk = world.HasWalkBounds ? world.ScaledWalkBounds : (Bounds?)null;
            System.Func<float, float, float> ground = (x, z) =>
            {
                if (probe != null) return probe.Floor(new Vector3(x, floorY, z));
                if (walk == null) return floorY;
                var b = walk.Value;
                return x >= b.min.x && x <= b.max.x && z >= b.min.z && z <= b.max.z ? floorY : float.NaN;
            };
            System.Func<Vector3, Vector3, float, float?> wallRay = (o, d, far) => probe != null ? probe.Ray(o, d, far) : null;

            var spawn = new Vector3(spawnXZ.x, floorY, spawnXZ.z);
            var hangs = MuseXR.Worlds.WebGalleryLayout.Place(works.Count, spawn, world.SpawnRotation * Vector3.forward, walk, ground, wallRay);
            for (var i = 0; i < hangs.Count; i++)
            {
                var tex = Image(works[i].id);
                var aspect = MuseXR.Worlds.PictureAspect.Of(tex, 1.3f);
                var centre = hangs[i].centre; centre.y = floorY + HangAboveFloor;
                Build(works[i], tex, centre, MuseXR.Worlds.WebGalleryLayout.QuadRotation(centre, hangs[i].faces),
                      MuseXR.Worlds.WebGalleryLayout.CanvasSize(aspect));
            }
            probe?.Dispose();
            Debug.Log($"[Paintings] {worldKey}: {Hung.Count} works hung as the journey hangs them, {hangs.FindAll(h => h.onWall).Count} on a wall");
        }

        void HangPlaced()
        {
            if (artworksJson == null) { Debug.LogError("[Paintings] artworks.json missing"); return; }
            var works = new Dictionary<string, ArtworkRecord>();
            foreach (var w in ArtworkCatalog.For(ArtworkCatalog.Parse(artworksJson.text), collectionId)) works[w.id] = w;
            foreach (var p in placements)
            {
                if (!works.TryGetValue(p.id, out var record)) { Debug.LogError("[Paintings] '" + p.id + "' is not in collection '" + collectionId + "'"); continue; }
                var tex = Image(p.id);
                var size = p.fill ? p.canvas : Fit(tex, p.canvas);
                var centre = p.centre;
                if (p.easel) { centre.y = p.floor + EaselShelf + size.y * 0.5f + 0.02f; Easel(p, centre); }
                // In this object's space, not the world's: chained into GateWorld a chapter can wake while
                // its frame still stands behind a gate, and its works must hang in its world, wherever that is.
                Build(record, tex, transform.TransformPoint(centre),
                      transform.rotation * MuseXR.Worlds.WebGalleryLayout.QuadRotation(centre, centre + p.facing), size, p.frame);
                // In a frame the capture already has: a mat over its whole canvas, so the capture's own
                // painted canvas does not show round a work of other proportions.
                if (p.fill) Crop(Hung[Hung.Count - 1], tex, p.canvas, p.focus);
                else if (!p.frame) Mat(Hung[Hung.Count - 1], size, p.canvas);
            }
            Debug.Log($"[Paintings] {worldKey}: {Hung.Count} works hung where her design doc puts them");
        }

        /// <summary>Where the easel model's shelf takes the canvas: height above its floor, and how far behind the canvas the easel stands.</summary>
        public const float EaselShelf = 0.84f, EaselBehind = 0.31f;

        /// <summary>
        /// The generated easel under a free-standing work (Saul, 5 Oct: paintings floating in mid-garden). A layout easel of
        /// cubes already standing there (Monet's) gives way to it, so one easel holds one painting.
        /// </summary>
        void Easel(Placement p, Vector3 centre)
        {
            var n = new Vector3(p.facing.x, 0f, p.facing.z).normalized;
            var floor = new Vector3(centre.x, p.floor, centre.z);
            var at = transform.TransformPoint(floor - n * EaselBehind);
            var go = MuseXR.Interaction.PropModels.Spawn("easel", transform, at, transform.rotation * Quaternion.LookRotation(n, Vector3.up), new Vector3(0f, 1.8f, 0f));
            if (go != null) go.name = "Easel · " + p.id;
            var root = transform.parent != null ? transform.parent : transform;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Easel ") || t.Find("Ledge") == null) continue;
                var d = t.position - transform.TransformPoint(floor); d.y = 0f;
                if (d.magnitude < 1.5f) t.gameObject.SetActive(false);
            }
        }

        /// <summary>The part of the work, at its own proportions, that covers <paramref name="box"/> round <paramref name="focus"/>.</summary>
        public static Rect CropRect(float workAspect, Vector2 box, Vector2 focus)
        {
            float boxAspect = box.x / box.y;
            float w = 1f, h = 1f;
            if (workAspect > boxAspect) w = boxAspect / workAspect; else h = workAspect / boxAspect;
            float x = Mathf.Clamp(focus.x - w * 0.5f, 0f, 1f - w), y = Mathf.Clamp(focus.y - h * 0.5f, 0f, 1f - h);
            return new Rect(x, y, w, h);
        }

        static void Crop(Transform canvas, Texture2D tex, Vector2 box, Vector2 focus)
        {
            if (tex == null || tex.height == 0) return;
            var r = CropRect(MuseXR.Worlds.PictureAspect.Of(tex), box, focus);
            var m = canvas.GetComponent<MeshRenderer>().sharedMaterial;
            m.SetTextureScale("_BaseMap", new Vector2(r.width, r.height));
            m.SetTextureOffset("_BaseMap", new Vector2(r.x, r.y));
        }

        static void Mat(Transform canvas, Vector2 size, Vector2 box)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Mat (canvas)";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(canvas, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(box.x / size.x, box.y / size.y, 1f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", new Color32(0xd6, 0xca, 0xb0, 0xff));
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// <summary>The largest size of the work's proportions inside <paramref name="box"/>.</summary>
        public static Vector2 Fit(Texture2D tex, Vector2 box)
        {
            var aspect = MuseXR.Worlds.PictureAspect.Of(tex, 1.3f);
            return aspect >= box.x / box.y ? new Vector2(box.x, box.x / aspect) : new Vector2(box.y * aspect, box.y);
        }

        void Build(ArtworkRecord record, Texture2D tex, Vector3 position, Quaternion quadRotation, Vector2 canvas, bool frame = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = record.id;
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(position, quadRotation);   // a Quad faces its -Z: +Z into the wall
            go.transform.localScale = new Vector3(canvas.x, canvas.y, 1f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (tex != null) m.SetTexture("_BaseMap", tex);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (frame)
            {
                Layer(go.transform, PrimitiveType.Cube, "Frame (walnut)", new Color32(0x5e, 0x40, 0x28, 0xff), canvas.x, canvas.y, 0.24f, 0.05f, 0.045f);
                Layer(go.transform, PrimitiveType.Quad, "Mat", new Color32(0xf5, 0xf2, 0xea, 0xff), canvas.x, canvas.y, 0.16f, 1f, 0.016f);
                Layer(go.transform, PrimitiveType.Cube, "Fillet (gold)", new Color32(0xc9, 0xaa, 0x72, 0xff), canvas.x, canvas.y, 0.04f, 0.012f, 0.008f);
            }
            else Layer(go.transform, PrimitiveType.Cube, "Fillet (gold)", new Color32(0xc9, 0xaa, 0x72, 0xff), canvas.x, canvas.y, 0.03f, 0.012f, 0.008f);   // a hairline against the capture's canvas
            var target = CompassTarget.Add(go, CompassOrder, record.title);
            // Tap: a master speaks about it (and the compass moves on); hold: take it off the wall;
            // two hands: scale it. Walking up to it also has a master speak (MasterInsights).
            var insight = InsightTarget.AddGrabbable(go, record.title, record.artist, record.id, target.MarkDone);
            Watch(go, record, canvas, insight);
            Hung.Add(go.transform);
        }

        /// <summary>
        /// Her card (4.1) and her "seen" (>= 4 s of gaze): a ray on the work for 0.4 s, or standing within
        /// 1.2 m of its viewing mark (2.1 m out in front, on the floor), puts up the card; gaze goes to the
        /// journey record, which is where Your world finds the works the visitor stayed with.
        /// </summary>
        static void Watch(GameObject go, ArtworkRecord record, Vector2 size, InsightTarget insight)
        {
            var mark = new GameObject("Viewing mark").transform;
            mark.SetParent(go.transform.parent, false);
            var facing = -go.transform.forward; facing.y = 0f;
            mark.position = go.transform.position + facing.normalized * 2.1f - Vector3.up * HangAboveFloor;
            var grab = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            var point = go.GetComponent<Pointable>();   // the trigger ray's own target on it (InsightTarget.AddGrabbable)
            var w = ArtworkWatcher.Make(go, record.id, mark, () => (grab != null && grab.isHovered && !grab.isSelected) || (point != null && Pointer.AnyOn(point)));
            w.CardWanted += _ => ArtworkCard.Show(record, go.transform, size, insight);
            go.AddComponent<DwellReporter>().Watcher = w;
        }

        /// <summary>Her picture frame, as Museum.unity builds it (MuseumJourneyRunner.AddFrame).</summary>
        static void Layer(Transform canvas, PrimitiveType shape, string name, Color colour,
                          float w, float h, float margin, float depth, float behind)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(canvas, false);
            go.transform.localPosition = new Vector3(0f, 0f, behind);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3((w + margin) / w, (h + margin) / h, depth);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", colour);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        Texture2D Image(string id)
        {
            if (artworkImages == null) return null;
            foreach (var t in artworkImages) if (t != null && t.name == id) return t;
            return null;
        }
    }
}
