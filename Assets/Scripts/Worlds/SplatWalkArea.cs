using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MusePico.Worlds
{
    /// <summary>
    /// The floor of one splat world that a person could actually stand on, as a flat mesh baked
    /// from the world's own splats by <see cref="WalkMask"/>.
    ///
    /// Put it on a child of the world's <c>GaussianSplatRenderer</c>, so it moves with the world
    /// (the temple follows the painting while it is carried) and is destroyed with it. The mesh is
    /// in this transform's local space; nothing here renders.
    ///
    /// Re-bake after changing a world: <c>MuseXR &gt; Painters &gt; Bake Walk Areas</c>.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class SplatWalkArea : MonoBehaviour
    {
        [Tooltip("Floor points known to be inside, in this world's local space. The world's own " +
                 "origin (its spawn) is always used; add more if part of the room is cut off.")]
        public Vector3[] extraSeeds = new Vector3[0];

        [Tooltip("What the last bake measured. Written by the bake; for reading, not editing.")]
        [TextArea] public string bakeReport;

        public Mesh Mesh => GetComponent<MeshFilter>().sharedMesh;

        /// <summary>The walk area as a NavMesh build source, where it stands right now.</summary>
        public bool TryGetSource(out NavMeshBuildSource source)
        {
            source = default;
            var mesh = Mesh;
            if (mesh == null) return false;
            source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = mesh,
                transform = transform.localToWorldMatrix,
                area = 0,
            };
            return true;
        }

        public Bounds WorldBounds
        {
            get
            {
                var mesh = Mesh;
                if (mesh == null) return new Bounds(transform.position, Vector3.zero);
                var b = mesh.bounds;
                var m = transform.localToWorldMatrix;
                var w = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
                for (int c = 0; c < 8; c++)
                    w.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                return w;
            }
        }

        /// <summary>
        /// A flat mesh of the grid's walkable rectangles at <paramref name="floorY"/>, expressed in
        /// <paramref name="worldToLocal"/>'s space.
        /// </summary>
        public static Mesh BuildMesh(WalkMask.Grid grid, float floorY, Matrix4x4 worldToLocal, string name)
        {
            var rects = WalkMask.Rectangles(grid);
            var vertices = new List<Vector3>(rects.Count * 4);
            var triangles = new List<int>(rects.Count * 6);
            foreach (var (min, max) in rects)
            {
                int v = vertices.Count;
                vertices.Add(worldToLocal.MultiplyPoint3x4(new Vector3(min.x, floorY, min.y)));
                vertices.Add(worldToLocal.MultiplyPoint3x4(new Vector3(min.x, floorY, max.y)));
                vertices.Add(worldToLocal.MultiplyPoint3x4(new Vector3(max.x, floorY, max.y)));
                vertices.Add(worldToLocal.MultiplyPoint3x4(new Vector3(max.x, floorY, min.y)));
                // Wound so the face points up in world space (clockwise seen from above).
                triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
            }
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
