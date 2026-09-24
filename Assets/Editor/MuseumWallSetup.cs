using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MusePico.Dialogue;
using MuseXR.Worlds;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Hangs the artworks in the open scene on the walls the capture actually has.
    ///
    /// <b>Why a tool and not a one-off.</b> The exhibition spine is eight chapters, each a
    /// different capture with its own wall, and the two placeholder worlds will be re-exported.
    /// Every one of them needs this pass, and the two facts it encodes are each worth an afternoon
    /// if they have to be rediscovered.
    ///
    /// <b>The collider is X-mirrored relative to everything else.</b> glTFast negates X converting
    /// glTF's right-handed frame to Unity's; three.js, which Skylar's numbers were measured in,
    /// does not. So her profile and our imported collider disagree by a reflection. Measured on
    /// van-gogh-500k: casting from her spawn as imported strikes 6 walls of 12 and finds NO floor
    /// beneath the visitor; mirrored, it strikes 12 of 12 and finds floor at y 0.31 against her
    /// groundY of 0. <c>WalkRig.mirrorColliderX</c> already carried this fix for ground-snapping.
    ///
    /// <b>Mirror the query, not the collider.</b> Negating one axis of a MeshCollider's scale
    /// inverts the mesh and returns hits at 0.1-0.2 m in every direction. <see cref="Mirror"/> is
    /// its own inverse and is applied to the origin, the direction and the result.
    /// </summary>
    public static class MuseumWallSetup
    {
        /// <summary>How far apart to stand the probes that sweep the room, in metres.</summary>
        const float ProbeSpacing = 4f;

        /// <summary>Degrees between rays at each probe. 15 gives 24 rays, plenty at a 1.5 m dedupe.</summary>
        const int ProbeStepDegrees = 15;

        const float ProbeRange = 30f;

        /// <summary>Two hits closer together than this describe the same piece of wall.</summary>
        const float DedupeRadius = 1.5f;

        /// <summary>How far outside the walk box a wall may be and still be this room's wall.</summary>
        const float OutsideBoxAllowance = 6f;

        /// <summary>glTF is right-handed, Unity is left-handed, and glTFast resolves that on X.</summary>
        public static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        /// <summary>
        /// Sweep every chapter's collider once and store the result, so the runtime never has to.
        ///
        /// The sweep is the good placement, but it costs an 85k-triangle glTF import per capture —
        /// unaffordable on a chapter change, free in the Editor. The geometry never moves, so the
        /// answer never changes.
        ///
        /// A capture with no collider is skipped rather than faked: it falls back to the playtested
        /// walk box at runtime, which is weaker and never absent.
        /// </summary>
        [MenuItem("MuseXR/Museum/Bake Wall Anchors (All Chapters)")]
        public static void BakeAllChapters()
        {
            const string path = "Assets/Museum/WallAnchors.asset";
            var store = AssetDatabase.LoadAssetAtPath<MusePico.Journey.WallAnchors>(path);
            if (store == null)
            {
                store = ScriptableObject.CreateInstance<MusePico.Journey.WallAnchors>();
                AssetDatabase.CreateAsset(store, path);
            }

            var report = new System.Text.StringBuilder("[MuseumWallSetup] baking wall anchors\n");
            var chapters = new List<ExhibitionChapter>(ExhibitionSpine.Chapters) { ExhibitionSpine.Final };

            foreach (var chapter in chapters)
            {
                var key = chapter.EffectiveWorldKey + WorldCatalog.SmallSuffix;
                WorldDefinition world = null;
                foreach (var w in WorldCatalog.Get(WorldSet.Small)) if (w.key == key) { world = w; break; }

                if (world == null)
                {
                    report.Append("  ").Append(chapter.Chapter).Append("  no world '").Append(key).Append("'\n");
                    continue;
                }

                var colliderRoot = LoadCollider(world);
                if (colliderRoot == null)
                {
                    report.Append("  ").Append(chapter.Chapter).Append("  ").Append(key)
                          .Append("  NO COLLIDER — falls back to the walk box\n");
                    continue;
                }

                try
                {
                    var height = world.groundY * world.worldScale + GalleryWall.DefaultHeight;
                    var struck = Sweep(world, colliderRoot, height);
                    var supported = WallSupport.Supported(struck);
                    var hung = WallGallery.Lay(supported, world.ScaledSpawn, height,
                                               ArtworkCatalog.PerChapter);

                    var anchors = new MusePico.Journey.WallAnchor[hung.Count];
                    for (var i = 0; i < hung.Count; i++)
                        anchors[i] = new MusePico.Journey.WallAnchor
                        {
                            position = hung[i].Position,
                            // Stored as the QUAD rotation, because that is what the runtime puts on
                            // the mesh. Storing the facing and re-deriving it at load is one more
                            // place to forget the 180.
                            rotation = hung[i].QuadRotation,
                        };
                    store.Set(key, anchors);

                    report.Append("  ").Append(chapter.Chapter.PadRight(20)).Append(key.PadRight(44))
                          .Append(struck.Count).Append(" struck, ").Append(supported.Count)
                          .Append(" supported, ").Append(hung.Count).Append(" anchors\n");
                }
                finally
                {
                    Object.DestroyImmediate(colliderRoot);
                }
            }

            EditorUtility.SetDirty(store);
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        [MenuItem("MuseXR/Museum/Hang Artworks On Walls")]
        public static void HangArtworks()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var world = WorldInScene();
            if (world == null)
            {
                Debug.LogError("[MuseumWallSetup] No GaussianSplatRenderer in the scene, so there is " +
                               "no way to tell which capture these walls belong to. Load a world first.");
                return;
            }

            var wallRoot = GameObject.Find("Wall");
            if (wallRoot == null || wallRoot.transform.childCount == 0)
            {
                Debug.LogError("[MuseumWallSetup] No 'Wall' root with artworks under it.");
                return;
            }

            var colliderRoot = LoadCollider(world);
            if (colliderRoot == null)
            {
                Debug.LogWarning("[MuseumWallSetup] " + world.key + " ships no collider; leaving the " +
                                 "playtested box placement alone rather than guessing at walls.");
                return;
            }

            try
            {
                var height = world.groundY * world.worldScale + GalleryWall.DefaultHeight;
                var struck = Sweep(world, colliderRoot, height);

                // A per-hit test can only see one hit, so a triangle alone in mid-air passes all of
                // them. Only surfaces that are part of a continuous plane are real wall.
                var surfaces = WallSupport.Supported(struck);

                var hung = WallGallery.Lay(surfaces, world.ScaledSpawn, height, wallRoot.transform.childCount);

                for (var i = 0; i < hung.Count; i++)
                {
                    var t = wallRoot.transform.GetChild(i);
                    t.position = hung[i].Position;
                    t.rotation = hung[i].QuadRotation;   // a Unity Quad's +Z points INTO the wall
                }

                var remaining = wallRoot.transform.childCount - hung.Count;
                Debug.Log("[MuseumWallSetup] " + world.key + ": struck " + struck.Count +
                          " surfaces, " + surfaces.Count + " of them supported wall, hung " +
                          hung.Count + "/" + wallRoot.transform.childCount + "." +
                          (remaining > 0 ? " " + remaining + " left on the walk box: the wall is full." : ""));

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                // Never leave 85k triangles of probe geometry in a saved scene. A root named
                // World_* or Collider surviving a session is how 176 MB got into three of them.
                Object.DestroyImmediate(colliderRoot);
            }
        }

        /// <summary>
        /// Every wall-like surface the capture has, in OUR frame.
        ///
        /// Probes stand on a grid across the walk box at picture height and fan out. Only hits on
        /// the world collider count: the artworks carry MeshColliders of their own so they can be
        /// pointed at, and without this filter a sweep strikes the works it placed last time and
        /// feeds its own output back in. Placement then depends on placement, and two identical
        /// runs disagree. That happened; this is the fix.
        /// </summary>
        static List<WallSurface> Sweep(WorldDefinition world, GameObject colliderRoot, float height)
        {
            var mine = new HashSet<Collider>(colliderRoot.GetComponentsInChildren<Collider>());
            var box = world.ScaledWalkBounds;
            var keep = new Bounds(box.center,
                box.size + new Vector3(OutsideBoxAllowance, 10000f, OutsideBoxAllowance));

            var found = new List<WallSurface>();
            Physics.SyncTransforms();

            for (var ox = box.min.x + 2f; ox <= box.max.x - 2f; ox += ProbeSpacing)
            for (var oz = box.min.z + 2f; oz <= box.max.z - 2f; oz += ProbeSpacing)
            {
                var origin = new Vector3(ox, height, oz);
                for (var a = 0; a < 360; a += ProbeStepDegrees)
                {
                    var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    var hits = Physics.RaycastAll(Mirror(origin), Mirror(dir), ProbeRange);
                    System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));

                    foreach (var h in hits)
                    {
                        if (!mine.Contains(h.collider)) continue;   // not the world: the art, or the rig

                        var point = Mirror(h.point);
                        var normal = Mirror(h.normal);
                        if (!WallSnap.IsWall(normal)) break;        // floor or ceiling: this ray is done
                        if (!keep.Contains(new Vector3(point.x, box.center.y, point.z))) break;

                        var already = false;
                        foreach (var f in found)
                            if (Vector3.Distance(f.Point, point) < DedupeRadius) { already = true; break; }
                        if (!already) found.Add(new WallSurface(point, WallSnap.FaceIntoRoom(normal, dir)));
                        break;                                      // first wall only; behind it is another room
                    }
                }
            }
            return found;
        }

        static GameObject LoadCollider(WorldDefinition world)
        {
            var key = world.key.EndsWith(WorldCatalog.SmallSuffix)
                ? world.key.Substring(0, world.key.Length - WorldCatalog.SmallSuffix.Length)
                : world.key;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Worlds/Colliders/" + key + "-collider.glb");
            if (prefab == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "Collider (probe)";
            instance.hideFlags = HideFlags.DontSave;
            instance.transform.localScale = Vector3.one * world.worldScale;

            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                var mc = filter.gameObject.GetComponent<MeshCollider>();
                if (mc == null) mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                var r = filter.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = false;
            }
            return instance;
        }

        /// <summary>
        /// Which capture the open scene is showing, read from the splat renderer rather than from a
        /// field someone has to keep in step with it.
        /// </summary>
        static WorldDefinition WorldInScene()
        {
            foreach (var m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (m == null || !m.GetType().Name.Contains("GaussianSplatRenderer")) continue;

                var field = m.GetType().GetField("m_Asset",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                var asset = field?.GetValue(m) as Object;
                if (asset == null) continue;

                foreach (var w in WorldCatalog.Get(WorldSet.Small))
                    if (w.key == asset.name) return w;
            }
            return null;
        }
    }
}
