using System.Collections.Generic;
using MusePico.Worlds;
using UnityEngine;
using UnityEngine.AI;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Each world's real floor, read from its collider, for everything that stands on it.
    ///
    /// <b>The visitor</b> lands on it instead of on the playtested groundY, by her groundAt rule
    /// (<see cref="WorldCycler.landingFloor"/>). Measured under the spawns: groundY stood the visitor
    /// 1.45 m above the conservatory's floor, 1.9 m above the water garden's and 7.7 m above the
    /// coastal villa's garden.
    ///
    /// <b>The painters</b> get a NavMesh built from the same collider on every world change, so they
    /// walk on the floor the visitor sees, and are stood beside the visitor (they cannot walk from one
    /// world into another: each world is loaded at its own place). A world without a trusted collider
    /// gets a flat floor at the visitor's height.
    ///
    /// <b>Doors</b> read the floor through <see cref="Current"/> (the world at the origin) or a probe
    /// of their own opened with <see cref="Open"/> for a world waiting behind them.
    /// </summary>
    public sealed class CaptureFloor : MonoBehaviour
    {
        public WorldCycler cycler;

        [Tooltip("The worlds' *-collider.glb models (Assets/Worlds/Colliders), matched by name.")]
        public GameObject[] colliderModels;

        [Tooltip("Make each world's floor a teleport target. Skylar's VR plan moves by teleport and snap turn only.")]
        public bool teleportable = true;

        [Tooltip("Optional: the painters, given each world's floor and stood beside the visitor on arrival.")]
        public PainterEscort escort;

        /// <summary>The probe of the world now at the origin, or null if it has no trusted collider.</summary>
        public CaptureProbe Current { get; private set; }

        NavMeshData _navData;
        NavMeshDataInstance _navInstance;
        readonly List<NavMeshBuildSource> _sources = new List<NavMeshBuildSource>();

        void Awake()
        {
            if (cycler == null) cycler = FindAnyObjectByType<WorldCycler>();
            if (cycler == null) return;
            cycler.landingFloor = Landing;
            cycler.WorldChanged += OnWorldChanged;
        }

        void OnDestroy()
        {
            if (cycler != null)
            {
                cycler.WorldChanged -= OnWorldChanged;
                if (cycler.landingFloor == (System.Func<WorldDefinition, float?>)Landing) cycler.landingFloor = null;
            }
            Current?.Dispose();
            if (_navInstance.valid) _navInstance.Remove();
        }

        /// <summary>A probe for a world standing somewhere else (behind a door), under <paramref name="frame"/>.</summary>
        public CaptureProbe Open(WorldDefinition world, Transform frame) => CaptureProbe.Open(world, colliderModels, frame);

        /// <summary>
        /// Hand over a probe that has become the current world's: a door's next world, now moved to the
        /// origin. Its root is taken out of the door's frame, which is about to be destroyed.
        /// </summary>
        public void Adopt(CaptureProbe probe)
        {
            if (probe == Current) return;
            Current?.Dispose();
            Current = probe;
            if (probe?.Root != null) probe.Root.SetParent(null, true);
        }

        float? Landing(WorldDefinition world)
        {
            Replace(world);
            if (Current == null) return null;
            float y = Current.Floor(world.ScaledSpawn);
            Debug.Log($"[CaptureFloor] landing in {world.key}: groundY {world.ScaledSpawn.y:F2} -> " +
                      (float.IsNaN(y) ? "no floor found, keep groundY" : y.ToString("F2")));
            return float.IsNaN(y) ? (float?)null : y;
        }

        void Replace(WorldDefinition world)
        {
            if (Current != null && Current.World == world) return;
            Current?.Dispose();
            Current = CaptureProbe.Open(world, colliderModels);
            if (teleportable) Current?.MakeTeleportable();
        }

        void OnWorldChanged(WorldDefinition world)
        {
            Replace(world);
            if (escort == null) return;

            var origin = cycler.xrOrigin != null ? cycler.xrOrigin.transform : null;
            var floor = origin != null ? origin.position : new Vector3(0f, cycler.FloorY, 0f);
            BuildNavMesh(floor, Current);
            escort.ArriveBeside(floor, origin != null ? origin.eulerAngles.y : 0f);
        }

        /// <summary>
        /// The visitor has stepped into another world without the cycler changing it (through a
        /// painting, JourneyPaintingPortal): build the painters' floor from that world's probe and
        /// bring them to the visitor's side. Null means the current world again.
        /// </summary>
        public void ArriveIn(CaptureProbe probe)
        {
            if (teleportable) probe?.MakeTeleportable();
            if (escort == null) return;
            var origin = cycler.xrOrigin != null ? cycler.xrOrigin.transform : null;
            var floor = origin != null ? origin.position : new Vector3(0f, cycler.FloorY, 0f);
            BuildNavMesh(floor, probe ?? Current);
            escort.ArriveBeside(floor, origin != null ? origin.eulerAngles.y : 0f);
        }

        void BuildNavMesh(Vector3 floor, CaptureProbe probe)
        {
            _sources.Clear();
            if (probe != null)
            {
                // The collider is X-mirrored relative to the splats; the source carries the mirror and
                // the mesh is used as it is. Measured: the builder corrects winding for a negative
                // matrix itself, and a winding-reversed copy made only roofs walkable.
                var mirror = probe.MirrorInFrame();
                foreach (var f in probe.Meshes)
                {
                    if (f.sharedMesh == null) continue;
                    _sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh, sourceObject = f.sharedMesh,
                        transform = mirror * f.transform.localToWorldMatrix, area = 0,
                    });
                }
            }
            if (_sources.Count == 0)
            {
                _sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box, size = new Vector3(24f, 0.1f, 24f),
                    transform = Matrix4x4.TRS(floor - Vector3.up * 0.05f, Quaternion.identity, Vector3.one), area = 0,
                });
            }

            if (_navData == null) { _navData = new NavMeshData(0); _navInstance = NavMesh.AddNavMeshData(_navData); }
            NavMeshBuilder.UpdateNavMeshData(_navData, NavMesh.GetSettingsByID(0), _sources,
                                             new Bounds(floor, new Vector3(50f, 20f, 50f)));
            Debug.Log("[CaptureFloor] painters' floor from " + (probe != null ? probe.World.key + "'s collider" : "a flat floor"));
        }
    }
}
