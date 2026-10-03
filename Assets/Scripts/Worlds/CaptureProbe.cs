using System.Collections.Generic;
using MusePico.Worlds;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// A world's <c>*-collider.glb</c>, put in place invisibly to read the capture's real floor: where
    /// the visitor lands, where a door stands, what the painters walk on.
    ///
    /// <b>In whatever frame the world stands.</b> Created under <paramref name="parent"/> (a door's
    /// pivot for a world waiting behind a door, nothing for the world at the origin), so it moves with
    /// its world. Queries are world-space in and out.
    ///
    /// <b>Mirrored queries.</b> glTFast negates X importing these meshes; the splats never had that
    /// flip. Mirror the query in the world's frame, never the collider: a negatively scaled
    /// MeshCollider is inside out and a downward ray goes through its floors.
    ///
    /// <b>Colliders off except while sampling.</b> A concave MeshCollider cannot be a trigger, and
    /// left on it would stop the visitor's CharacterController on its patchy walls and catch the
    /// controller ray. They are enabled for the length of one query only.
    /// </summary>
    public sealed class CaptureProbe : System.IDisposable
    {
        /// <summary>
        /// Captures whose collider is not in their splat's frame. The shimmering-spheres world shipped
        /// in muse-infinity as a mesh world; its collider is about 9 m across against a ~48 m splat and
        /// every sample under it read ~1.4 m above the visitor.
        /// </summary>
        public static readonly string[] Untrusted = { "fantasy-realm-of-shimmering-spheres" };

        public readonly WorldDefinition World;
        readonly GameObject _root;
        readonly List<MeshCollider> _colliders = new List<MeshCollider>();
        readonly HashSet<Collider> _mine = new HashSet<Collider>();
        readonly List<float> _hits = new List<float>();

        /// <summary>The probe's root: a child of the world's frame, scaled by its worldScale.</summary>
        public Transform Root => _root != null ? _root.transform : null;

        /// <summary>The collider meshes, for building a NavMesh from the same geometry.</summary>
        public IEnumerable<MeshFilter> Meshes => _root.GetComponentsInChildren<MeshFilter>(true);

        CaptureProbe(WorldDefinition world, GameObject model, Transform parent)
        {
            World = world;
            _root = new GameObject("Capture Probe " + world.key);
            _root.transform.SetParent(parent, false);
            _root.transform.localScale = Vector3.one * world.worldScale;
            var instance = Object.Instantiate(model, _root.transform);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var f in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                var mc = f.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = f.sharedMesh;
                mc.enabled = false;
                _colliders.Add(mc);
                _mine.Add(mc);
            }
        }

        /// <summary>The base key a world's collider is named after: its key without the 500k suffix.</summary>
        public static string BaseKey(string key) =>
            key != null && key.EndsWith(WorldCatalog.SmallSuffix)
                ? key.Substring(0, key.Length - WorldCatalog.SmallSuffix.Length) : key;

        /// <summary>A probe for <paramref name="world"/>, or null when it has no collider in
        /// <paramref name="models"/> or its collider cannot be trusted.</summary>
        public static CaptureProbe Open(WorldDefinition world, GameObject[] models, Transform parent = null)
        {
            if (world == null || models == null) return null;
            var baseKey = BaseKey(world.key);
            if (System.Array.IndexOf(Untrusted, baseKey) >= 0) return null;
            foreach (var m in models)
                if (m != null && m.name == baseKey + "-collider") return new CaptureProbe(world, m, parent);
            return null;
        }

        /// <summary>
        /// The capture's floor under <paramref name="worldPoint"/>, chosen by her groundAt rule
        /// (<see cref="WalkGround.PickGroundHeight"/>, with the playtested groundY as the reference);
        /// NaN where the collider has nothing under it.
        /// </summary>
        public float Floor(Vector3 worldPoint)
        {
            if (_root == null) return float.NaN;
            var frame = _root.transform.parent;
            float ws = World.worldScale;
            float groundY = World.groundY * ws;

            var local = frame != null ? frame.InverseTransformPoint(worldPoint) : worldPoint;
            var fromLocal = new Vector3(-local.x, groundY + 4f, local.z);
            var from = frame != null ? frame.TransformPoint(fromLocal) : fromLocal;
            var down = frame != null ? -frame.up : Vector3.down;

            foreach (var c in _colliders) c.enabled = true;
            Physics.SyncTransforms();
            _hits.Clear();
            foreach (var hit in Physics.RaycastAll(from, down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!_mine.Contains(hit.collider)) continue;
                _hits.Add(frame != null ? frame.InverseTransformPoint(hit.point).y : hit.point.y);
            }
            foreach (var c in _colliders) c.enabled = false;

            if (_hits.Count == 0) return float.NaN;
            float y = WalkGround.PickGroundHeight(_hits, groundY, ws);
            return frame != null ? frame.TransformPoint(new Vector3(local.x, y, local.z)).y : y;
        }

        /// <summary>
        /// Distance along a ray to the capture's collider, or null when it hits nothing within
        /// <paramref name="far"/>. World-space in; the query is mirrored in the world's frame like
        /// <see cref="Floor"/>. Her museum3d.js aims these at the collider to find walls.
        /// </summary>
        public float? Ray(Vector3 origin, Vector3 direction, float far)
        {
            if (_root == null) return null;
            var frame = _root.transform.parent;
            var o = frame != null ? frame.InverseTransformPoint(origin) : origin;
            var d = frame != null ? frame.InverseTransformDirection(direction) : direction;
            o.x = -o.x; d.x = -d.x;
            if (frame != null) { o = frame.TransformPoint(o); d = frame.TransformDirection(d); }

            foreach (var c in _colliders) c.enabled = true;
            Physics.SyncTransforms();
            float? best = null;
            foreach (var hit in Physics.RaycastAll(o, d.normalized, far, ~0, QueryTriggerInteraction.Ignore))
                if (_mine.Contains(hit.collider) && (best == null || hit.distance < best)) best = hit.distance;
            foreach (var c in _colliders) c.enabled = false;
            return best;
        }

        /// <summary>The mirror the collider needs in its world's frame, for NavMesh sources.</summary>
        public Matrix4x4 MirrorInFrame()
        {
            var frame = _root.transform.parent;
            var m = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            return frame != null ? frame.localToWorldMatrix * m * frame.worldToLocalMatrix : m;
        }

        GameObject _teleportFloor;

        /// <summary>
        /// Her plan's locomotion is teleport only, so the capture's floor must be a teleport target.
        /// The probe's own colliders stay mirrored and switched off (its queries mirror instead); this
        /// adds a baked copy in the splat's frame (<see cref="MirroredMesh"/>), always on, on the layer
        /// pointers pass through, with a TeleportationArea that accepts only upward-facing hits.
        /// </summary>
        public void MakeTeleportable()
        {
            if (_root == null || _teleportFloor != null) return;
            _teleportFloor = new GameObject("Teleport Floor");
            _teleportFloor.SetActive(false);   // XRI registers the area once, with its colliders set
            _teleportFloor.transform.SetParent(_root.transform, false);
            if (PhysicsBounds.Layer >= 0) _teleportFloor.layer = PhysicsBounds.Layer;

            var area = _teleportFloor.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            area.colliders.Clear();
            var rootInverse = _root.transform.worldToLocalMatrix;
            foreach (var f in _root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || f.transform.IsChildOf(_teleportFloor.transform)) continue;
                var mc = _teleportFloor.AddComponent<MeshCollider>();
                mc.sharedMesh = MirroredMesh.Bake(f.sharedMesh, rootInverse * f.transform.localToWorldMatrix);
                area.colliders.Add(mc);
            }
            // The rig's teleport interactors select on the "Teleport" interaction layer, bit 31.
            area.interactionLayers = 1 << 31;
            area.filterSelectionByHitNormal = true;
            _teleportFloor.SetActive(true);
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }
}
