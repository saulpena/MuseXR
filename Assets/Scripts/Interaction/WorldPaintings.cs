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

        public List<Transform> Hung { get; } = new List<Transform>();

        void Start() => Hang();

        public void Hang()
        {
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
                var aspect = tex != null && tex.height > 0 ? tex.width / (float)tex.height : 1.3f;
                var centre = hangs[i].centre; centre.y = floorY + HangAboveFloor;
                Build(works[i], tex, centre, MuseXR.Worlds.WebGalleryLayout.QuadRotation(centre, hangs[i].faces),
                      MuseXR.Worlds.WebGalleryLayout.CanvasSize(aspect));
            }
            probe?.Dispose();
            Debug.Log($"[Paintings] {worldKey}: {Hung.Count} works hung as the journey hangs them, {hangs.FindAll(h => h.onWall).Count} on a wall");
        }

        void Build(ArtworkRecord record, Texture2D tex, Vector3 position, Quaternion quadRotation, Vector2 canvas)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = record.id;
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(position, quadRotation);   // a Quad faces its -Z: +Z into the wall
            go.transform.localScale = new Vector3(canvas.x, canvas.y, 1f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (tex != null) m.SetTexture("_BaseMap", tex);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            Layer(go.transform, PrimitiveType.Cube, "Frame (walnut)", new Color32(0x5e, 0x40, 0x28, 0xff), canvas.x, canvas.y, 0.24f, 0.05f, 0.045f);
            Layer(go.transform, PrimitiveType.Quad, "Mat", new Color32(0xf5, 0xf2, 0xea, 0xff), canvas.x, canvas.y, 0.16f, 1f, 0.016f);
            Layer(go.transform, PrimitiveType.Cube, "Fillet (gold)", new Color32(0xc9, 0xaa, 0x72, 0xff), canvas.x, canvas.y, 0.04f, 0.012f, 0.008f);
            var target = CompassTarget.Add(go, CompassOrder, record.title);
            var p = Pointable.Make(go, record.id);
            p.Selected += (_, __) => target.MarkDone();
            InsightTarget.Add(go, record.title, record.artist, record.id);   // click or walk up: a master's insight
            Hung.Add(go.transform);
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
