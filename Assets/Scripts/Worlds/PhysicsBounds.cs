using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>
    /// An invisible floor and four walls closing a walkable box.
    ///
    /// <b>Why every splat world needs one.</b> A Gaussian splat is not geometry — it cannot be
    /// collided with or raycast. So a world made only of splats has nothing for a
    /// CharacterController to stand on, and under gravity the visitor falls out of it immediately.
    /// Two cases, both ending here:
    ///
    ///   - <b>Skylar's worlds</b> ship a <c>*-collider.glb</c>, which is real geometry and does the
    ///     real work. But her collider is a shell: no furniture, and the floor is a patch rather
    ///     than a full floor — along the van-gogh gallery only 12 of 28 sample rays hit anything.
    ///     A hole in the floor under gravity is a bottomless fall, so this backs it up.
    ///   - <b>World Labs' samples</b> ship no collider at all and never will; the CDN export is
    ///     splats only. Here this box <i>is</i> the floor, not a backstop.
    ///
    /// Physics only — no renderers, nothing to see. Sized in world units, after worldScale.
    /// </summary>
    public static class PhysicsBounds
    {
        public const float DefaultWallHeight = 4f;
        const float Thickness = 1f;

        /// <summary>
        /// The layer for INVISIBLE physics-only geometry: these walls, and the Marble collider mesh
        /// <c>WalkRig</c> loads. Both stop a walker; neither may stop a POINTING ray. In
        /// SunlitMuseum the walk box's "Wall +X" stands 0.3 m in front of the capture's wall and
        /// the Marble collider 0.2 m, so on Default they caught every controller ray and no
        /// painting behind them could be reached or grabbed. The controllers' Near-Far rays leave
        /// this layer out; the teleport rays in <c>MuseXR Rig.prefab</c> include it, so an arc still
        /// lands on the collider floor and still cannot pass a wall. Unmasked physics queries
        /// (ground snapping, the party's ground rays) include it as before. The box floor stays on
        /// Default: it is only ever walked on.
        /// </summary>
        public const string LayerName = "PhysicsBounds";

        /// <summary>The layer index, or -1 if the project has lost the layer (then Default is used).</summary>
        public static int Layer => LayerMask.NameToLayer(LayerName);

        /// <summary>
        /// Builds the floor and walls under <paramref name="name"/>. <paramref name="floorY"/> is
        /// the walking surface: the box top sits exactly there, so a Floor-tracked XR Origin placed
        /// at the same Y is standing on it rather than inside it.
        /// </summary>
        public static GameObject Build(string name, float minX, float maxX, float minZ, float maxZ,
                                       float floorY, float wallHeight = DefaultWallHeight)
        {
            float sizeX = Mathf.Max(0.1f, maxX - minX);
            float sizeZ = Mathf.Max(0.1f, maxZ - minZ);
            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;

            var root = new GameObject(name);

            Box(root, "Floor", new Vector3(cx, floorY - Thickness * 0.5f, cz),
                new Vector3(sizeX, Thickness, sizeZ));

            float wy = floorY + wallHeight * 0.5f;
            var wallLayer = Layer;
            Box(root, "Wall -X", new Vector3(minX - Thickness * 0.5f, wy, cz), new Vector3(Thickness, wallHeight, sizeZ), wallLayer);
            Box(root, "Wall +X", new Vector3(maxX + Thickness * 0.5f, wy, cz), new Vector3(Thickness, wallHeight, sizeZ), wallLayer);
            Box(root, "Wall -Z", new Vector3(cx, wy, minZ - Thickness * 0.5f), new Vector3(sizeX, wallHeight, Thickness), wallLayer);
            Box(root, "Wall +Z", new Vector3(cx, wy, maxZ + Thickness * 0.5f), new Vector3(sizeX, wallHeight, Thickness), wallLayer);

            Physics.SyncTransforms();
            return root;
        }

        /// <summary>
        /// The same box derived from a splat asset's own bounds, for worlds with no authored walk
        /// box — which is every World Labs sample. Inset slightly so the visitor cannot press
        /// against the outer shell of the capture, where the splats degrade into noise.
        /// </summary>
        public static GameObject BuildFromSplatBounds(string name, Vector3 boundsMin, Vector3 boundsMax,
                                                      float worldScale, float floorY,
                                                      float inset = 0.5f,
                                                      float wallHeight = DefaultWallHeight)
        {
            var min = boundsMin * worldScale;
            var max = boundsMax * worldScale;
            return Build(name, min.x + inset, max.x - inset, min.z + inset, max.z - inset,
                         floorY, wallHeight);
        }

        static void Box(GameObject parent, string name, Vector3 centre, Vector3 size, int layer = -1)
        {
            var go = new GameObject(name);
            if (layer >= 0) go.layer = layer;
            go.transform.SetParent(parent.transform, false);
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = size;
        }
    }
}
