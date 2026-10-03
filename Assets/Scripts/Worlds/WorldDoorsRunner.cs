using System.Collections;
using System.Collections.Generic;
using MusePico.Worlds;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace MuseXR.Worlds
{
    /// <summary>
    /// muse-infinity's exhibition walk with doors instead of a navigator. Her web app moves between
    /// scenes with arrows and a dot per scene; here each world has ONE door, to the next world in her
    /// order (the last leads back to the first), standing where it makes sense in that capture: on a
    /// path, before an arch, against a wall (ExhibitionScene.doorBearing / doorDistance, chosen by
    /// looking). Picking it (controller ray + trigger, or a click without a headset) or walking
    /// through it goes on.
    ///
    /// The change itself follows hers: the world is torn down, the screen goes dark, her chapter and
    /// title come up, and the next world arrives — a cut, not a cross-fade, because that is what the
    /// web build does (<c>goToExhibitionScene</c>: teardown, render, init). Nothing advances on its
    /// own.
    ///
    /// Loading is the <see cref="WorldCycler"/>'s, driven externally, so placement, the fallback
    /// floor and the splat tuning are the same code every other scene uses.
    /// </summary>
    public sealed class WorldDoorsRunner : MonoBehaviour
    {
        [Tooltip("Loads and places the worlds. Must have Driven Externally ticked and the Original set.")]
        public WorldCycler cycler;

        [Tooltip("Her scene thumbnails (muse-infinity/assets/scenes), matched to scenes by file name.")]
        public Texture2D[] thumbnails;

        [Tooltip("The worlds' *-collider.glb models (Assets/Worlds/Colliders), matched by name. Used only to " +
                 "find the real floor under the door, then removed; walking is unchanged.")]
        public GameObject[] colliderModels;

        [Tooltip("Optional. Monet and Picasso, walking with the visitor. Each world gets a NavMesh built " +
                 "from its collider, and the painters arrive beside the visitor through every door.")]
        public PainterEscort escort;

        [Header("Change of scene")]
        public float fadeSeconds = 0.35f;
        public float titleSeconds = 4f;

        /// <summary>The scene now showing, or -1 before the first arrives.</summary>
        public int CurrentIndex { get; private set; } = -1;

        /// <summary>True while a change of scene is under way; the door ignores picks and footsteps.</summary>
        public bool Busy { get; private set; }

        /// <summary>The door standing in the current world (one, to the next world).</summary>
        public IReadOnlyList<WorldDoor> Doors => _doors;

        readonly List<WorldDoor> _doors = new List<WorldDoor>();
        readonly Dictionary<WorldDoor, Vector3> _headLocal = new Dictionary<WorldDoor, Vector3>();
        Transform _doorRoot;
        Camera _camera;
        Material _fade;
        TextMeshPro _title;
        Coroutine _titleRoutine;

        void Start()
        {
            if (cycler == null) cycler = FindObjectOfType<WorldCycler>();
            if (cycler == null) { Debug.LogError("[WorldDoors] no WorldCycler in the scene"); enabled = false; return; }
            if (!cycler.drivenExternally)
                Debug.LogError("[WorldDoors] the WorldCycler is not Driven Externally — it will also cycle on its own.");

            _camera = cycler.xrOrigin != null ? cycler.xrOrigin.Camera : Camera.main;
            cycler.landingFloor = LandingFloor;
            _doorRoot = new GameObject("World Doors").transform;
            BuildFade();
            BuildTitle();
            StartCoroutine(Go(0));
        }

        /// <summary>Go to scene <paramref name="index"/> of <see cref="ExhibitionWorlds.Scenes"/>.</summary>
        public void GoTo(int index)
        {
            if (Busy || index < 0 || index >= ExhibitionWorlds.Scenes.Count || index == CurrentIndex) return;
            StartCoroutine(Go(index));
        }

        IEnumerator Go(int index)
        {
            Busy = true;
            var scene = ExhibitionWorlds.Scenes[index];
            Debug.Log("[WorldDoors] -> " + scene.chapter + " " + scene.title);

            yield return Fade(0f, 1f);
            ClearDoors();
            ShowTitle(scene);

            yield return cycler.ShowWorldByKey(scene.worldKey);
            CurrentIndex = index;
            BuildDoor(index);
            BringEscort();
            CloseProbe();

            yield return Fade(1f, 0f);
            Busy = false;
        }

        void BuildDoor(int current)
        {
            var origin = cycler.xrOrigin != null ? cycler.xrOrigin.transform : _camera.transform;
            var here = ExhibitionWorlds.Scenes[current];
            int next = ExhibitionWorlds.Next(current);
            var pose = WorldDoorLayout.Placed(origin.position, origin.forward, here.doorBearing, here.doorDistance);
            pose.position.y = SeatAt(pose, origin.position.y, here.doorOnColliderFloor);

            var target = ExhibitionWorlds.Scenes[next];
            var door = WorldDoor.Build(_doorRoot, pose, target, Thumbnail(target));
            door.Picked += _ => GoTo(next);
            _doors.Add(door);
            _headLocal[door] = door.transform.InverseTransformPoint(_camera.transform.position);
            Debug.Log("[WorldDoors] door to " + target.chapter + " " + target.title + " at " + pose.position.ToString("F2") +
                      " (" + here.doorReason + ")");
        }

        /// <summary>
        /// Where the visitor lands in <paramref name="world"/>: on the capture's own floor under the
        /// spawn, chosen exactly as her groundAt chooses it (WalkGround.PickGroundHeight, with the
        /// playtested groundY only as the reference), rather than at groundY itself. Measured under
        /// the spawns: groundY put the visitor 1.45 m above the conservatory's floor, 1.9 m above the
        /// water garden's and 7.7 m above the coastal villa's garden, standing in mid-air beside the
        /// parapet. Null (keep groundY) where the collider cannot be trusted or finds nothing.
        /// Called by the WorldCycler as it places the visitor; leaves the probe open for the door.
        /// </summary>
        float? LandingFloor(WorldDefinition world)
        {
            CloseProbe();
            if (!OpenProbe(world)) return null;
            var spawn = world.ScaledSpawn;
            float y = ProbeFloor(spawn);
            Debug.Log("[WorldDoors] landing in " + world.key + ": groundY " + spawn.y.ToString("F2") + " -> " +
                      (float.IsNaN(y) ? "no floor found, keep groundY" : y.ToString("F2")));
            return float.IsNaN(y) ? (float?)null : y;
        }

        /// <summary>
        /// The height to stand the door at. With <paramref name="onCollider"/> and a probe open, the
        /// capture's own floor under the door: the median of three samples (both posts, the middle),
        /// so one stray hit on an arch or a bed cannot lift it. Otherwise, and over a hole in the
        /// collider, the visitor's own floor.
        /// </summary>
        float SeatAt(DoorPose pose, float visitorFloor, bool onCollider)
        {
            if (!onCollider || _probe == null) return visitorFloor;
            var s = WorldDoorLayout.FloorSamples(pose, WorldDoor.Width / 2f);
            float seat = WorldDoorLayout.Median3(ProbeFloor(s[0]), ProbeFloor(s[1]), ProbeFloor(s[2]));
            if (float.IsNaN(seat)) return visitorFloor;
            if (Mathf.Abs(seat - visitorFloor) > WorldDoorLayout.MaxFloorStep)
                Debug.LogWarning("[WorldDoors] door in " + _probeWorld.key + " stands " + (seat - visitorFloor).ToString("F2") +
                                 " m off the visitor's floor: check its placement");
            return seat;
        }

        // ---- the painters ------------------------------------------------------------------------

        NavMeshData _navData;
        NavMeshDataInstance _navInstance;
        readonly List<NavMeshBuildSource> _navSources = new List<NavMeshBuildSource>();

        /// <summary>
        /// Give the painters this world's floor and stand them beside the visitor. The NavMesh is
        /// built from the world's collider (the probe is still open), so they walk on the floor the
        /// visitor sees and are kept out of its walls; where the collider cannot be trusted, a flat
        /// floor at the visitor's height. Built while the screen is dark, so its cost is not seen.
        /// </summary>
        void BringEscort()
        {
            if (escort == null) return;
            var origin = cycler.xrOrigin != null ? cycler.xrOrigin.transform : _camera.transform;
            var floor = origin.position;

            _navSources.Clear();

            if (_probe != null)
            {
                // The collider is X-mirrored relative to the splats (glTFast's handedness flip), so
                // the source carries a mirror matrix. The mesh is used as it is: the NavMesh builder
                // corrects winding for a negative-determinant matrix itself. Measured: a copy with
                // its winding reversed to "cancel" the mirror built walkable surface only on the
                // conservatory's roofs, 3-12 m up, and none on its floor.
                var mirror = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
                foreach (var f in _probe.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh == null) continue;
                    _navSources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh, sourceObject = f.sharedMesh,
                        transform = mirror * f.transform.localToWorldMatrix, area = 0,
                    });
                }
            }
            if (_navSources.Count == 0)
            {
                _navSources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box, size = new Vector3(24f, 0.1f, 24f),
                    transform = Matrix4x4.TRS(floor - Vector3.up * 0.05f, Quaternion.identity, Vector3.one), area = 0,
                });
            }

            if (_navData == null) { _navData = new NavMeshData(0); _navInstance = NavMesh.AddNavMeshData(_navData); }
            var bounds = new Bounds(floor, new Vector3(50f, 20f, 50f));
            NavMeshBuilder.UpdateNavMeshData(_navData, NavMesh.GetSettingsByID(0), _navSources, bounds);

            escort.ArriveBeside(floor, origin.eulerAngles.y);
            Debug.Log("[WorldDoors] painters' floor from " + (_probe != null ? "the collider" : "a flat floor") +
                      ", " + _navSources.Count + " source(s); they arrive beside the visitor");
        }

        // ---- the capture's floor, from its collider -------------------------------------------

        GameObject _probe;
        readonly HashSet<Collider> _probeColliders = new HashSet<Collider>();
        readonly List<float> _probeHits = new List<float>();
        WorldDefinition _probeWorld;

        /// <summary>Put the world's collider in place, invisible, for sampling. False if it has none
        /// or it cannot be trusted.</summary>
        bool OpenProbe(WorldDefinition world)
        {
            var model = world != null ? ColliderFor(world) : null;
            if (model == null) return false;
            _probeWorld = world;
            _probe = new GameObject("Floor Probe " + model.name);
            _probe.transform.localScale = Vector3.one * world.worldScale;
            var instance = Instantiate(model, _probe.transform);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var f in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                var mc = f.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = f.sharedMesh;
                _probeColliders.Add(mc);
            }
            Physics.SyncTransforms();
            return true;
        }

        /// <summary>The capture's floor under a world-space point, by her groundAt rule; NaN where
        /// the collider has nothing under it.</summary>
        float ProbeFloor(Vector3 p)
        {
            float ws = _probeWorld.worldScale;
            float groundY = _probeWorld.groundY * ws;
            _probeHits.Clear();
            // From well above the highest reference either side, so a floor far below groundY (the
            // coastal garden, 7.7 m down) and one above it are both in reach.
            var from = WorldDoorLayout.ToColliderFrame(new Vector3(p.x, groundY + 4f, p.z));
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
                if (_probeColliders.Contains(hit.collider)) _probeHits.Add(hit.point.y);
            return _probeHits.Count == 0 ? float.NaN : WalkGround.PickGroundHeight(_probeHits, groundY, ws);
        }

        void CloseProbe()
        {
            if (_probe != null) Destroy(_probe);
            _probe = null;
            _probeColliders.Clear();
            _probeWorld = null;
        }

        /// <summary>
        /// Worlds whose collider is not in the splat's frame, so sampling it gives nonsense. The
        /// shimmering-spheres capture shipped in muse-infinity as a MESH world (splatUrl null); its
        /// numbers there are in mesh space, about 11x from this .spz, and its collider came out the
        /// same way: every door "found floor" 1.3-1.5 m above the visitor. Doors there stand on groundY.
        /// </summary>
        static readonly string[] ColliderFrameUntrusted = { "fantasy-realm-of-shimmering-spheres" };

        GameObject ColliderFor(WorldDefinition world)
        {
            if (colliderModels == null) return null;
            var baseKey = world.key.EndsWith(WorldCatalog.SmallSuffix)
                ? world.key.Substring(0, world.key.Length - WorldCatalog.SmallSuffix.Length) : world.key;
            if (System.Array.IndexOf(ColliderFrameUntrusted, baseKey) >= 0) return null;
            foreach (var m in colliderModels) if (m != null && m.name == baseKey + "-collider") return m;
            return null;
        }

        void ClearDoors()
        {
            foreach (var d in _doors) if (d != null) Destroy(d.gameObject);
            _doors.Clear();
            _headLocal.Clear();
        }

        void Update()
        {
            if (Busy || _camera == null) return;
            if (ClickedDoor()) return;
            var head = _camera.transform.position;
            foreach (var door in _doors)
            {
                var now = door.transform.InverseTransformPoint(head);
                if (_headLocal.TryGetValue(door, out var before) &&
                    WorldDoorLayout.Crossed(before, now, WorldDoor.Width / 2f))
                {
                    Debug.Log("[WorldDoors] walked through " + door.Scene.title);
                    door.Pick();
                    return;   // the doors are about to be torn down
                }
                _headLocal[door] = now;
            }
        }

        /// <summary>
        /// Without a headset (the Editor without Link), a left click on a door opens it, as the
        /// journey's plates do. Triggers are hit on purpose: a door's collider is a trigger so it
        /// does not block walking through it. Off whenever an XR device is active.
        /// </summary>
        bool ClickedDoor()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) return false;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            var hits = Physics.RaycastAll(ray, 60f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var door = hit.collider.GetComponentInParent<WorldDoor>();
                if (door == null) continue;
                Debug.Log("[WorldDoors] clicked " + door.Scene.title);
                door.Pick();
                return true;
            }
            return false;
        }

        Texture2D Thumbnail(ExhibitionScene scene)
        {
            if (thumbnails != null)
                foreach (var t in thumbnails) if (t != null && t.name == scene.thumbnail) return t;
            Debug.LogWarning("[WorldDoors] no thumbnail named " + scene.thumbnail);
            return null;
        }

        // ---- the dark between scenes --------------------------------------------------------

        void BuildFade()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Scene Fade";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_camera.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.25f);
            quad.transform.localScale = new Vector3(2f, 2f, 1f);
            _fade = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Scene Fade" };
            _fade.SetFloat("_Surface", 1f);
            _fade.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _fade.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _fade.SetFloat("_ZWrite", 0f);
            _fade.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            _fade.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _fade.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            SetFade(1f);   // start dark; the first scene fades in
            quad.GetComponent<MeshRenderer>().sharedMaterial = _fade;
        }

        void SetFade(float a) => _fade.SetColor("_BaseColor", new Color(0f, 0f, 0f, a));

        IEnumerator Fade(float from, float to)
        {
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                SetFade(Mathf.Lerp(from, to, t / fadeSeconds));
                yield return null;
            }
            SetFade(to);
        }

        // ---- her chapter and title, as the web shows them on arrival --------------------------

        void BuildTitle()
        {
            _title = new GameObject("Scene Title").AddComponent<TextMeshPro>();
            _title.transform.SetParent(_camera.transform, false);
            _title.transform.localPosition = new Vector3(0f, 0.12f, 1.6f);
            _title.rectTransform.sizeDelta = new Vector2(1.6f, 0.5f);
            _title.alignment = TextAlignmentOptions.Center;
            _title.enableWordWrapping = true;
            _title.fontSize = 1.6f;
            _title.color = Color.white;
            // Drawn over the fade quad, which is in the Overlay queue.
            _title.renderer.sortingOrder = 10;
            _title.fontMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay + 10;
            _title.alpha = 0f;
        }

        void ShowTitle(ExhibitionScene scene)
        {
            _title.text = "<size=50%><color=#C9AA72><cspace=0.2em>" + scene.chapter + "</cspace></color></size>\n" + scene.title;
            if (_titleRoutine != null) StopCoroutine(_titleRoutine);
            _titleRoutine = StartCoroutine(TitleFor(titleSeconds));
        }

        IEnumerator TitleFor(float seconds)
        {
            _title.alpha = 1f;
            yield return new WaitForSecondsRealtime(seconds);
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                _title.alpha = 1f - t / 0.6f;
                yield return null;
            }
            _title.alpha = 0f;
        }

        void OnDestroy()
        {
            if (_navInstance.valid) _navInstance.Remove();
            CloseProbe();
            if (cycler != null && cycler.landingFloor == (System.Func<WorldDefinition, float?>)LandingFloor) cycler.landingFloor = null;
            ClearDoors();
            if (_doorRoot != null) Destroy(_doorRoot.gameObject);
            if (_fade != null) Destroy(_fade);
        }
    }
}
