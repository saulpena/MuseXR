using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// A Marble collider brought into its splat's frame. glTFast negates X converting glTF's
    /// right-handed frame to Unity's; the splat importer does not. So an imported *-collider.glb is the
    /// splat's mirror image, and a teleport ray aimed at the visible floor finds nothing there.
    ///
    /// Negating a MeshCollider's scale is NOT the fix: it turns every triangle inside out. Baking a
    /// copy with X negated AND the winding reversed gives the same surfaces, facing the same way, in
    /// the splat's frame, as an ordinary collider that teleport, gravity and raycasts can all use.
    /// </summary>
    public static class MirroredMesh
    {
        /// <summary>
        /// <paramref name="source"/> moved by <paramref name="toFrame"/> (its node's transform into the
        /// frame), then mirrored in X in that frame. The result is meant for an identity transform
        /// under the frame.
        /// </summary>
        public static Mesh Bake(Mesh source, Matrix4x4 toFrame)
        {
            var verts = source.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = toFrame.MultiplyPoint3x4(verts[i]);
                verts[i] = new Vector3(-v.x, v.y, v.z);
            }

            var mesh = new Mesh { name = source.name + " (mirrored)" };
            if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                for (int i = 0; i + 2 < tris.Length; i += 3)
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                mesh.SetTriangles(tris, s);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
