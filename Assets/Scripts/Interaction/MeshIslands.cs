using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The separate parts of one mesh: triangles joined through shared corners form one part. Corners at the same
    /// place count as shared (a model's vertices are split along its UV seams, which would cut one figure in pieces).
    /// Used to give each figure of a multi-figure model its own click box.
    /// </summary>
    public static class MeshIslands
    {
        /// <summary>Corners closer than this (m, in the mesh's own units) are one.</summary>
        public const float Weld = 0.001f;

        /// <summary>
        /// The local bounds of each part whose longest side is at least <paramref name="minSize"/>, largest first, at
        /// most <paramref name="max"/> of them.
        /// </summary>
        public static List<Bounds> Bounds(Mesh mesh, float minSize, int max)
        {
            var result = new List<Bounds>();
            if (mesh == null) return result;
            var v = mesh.vertices;
            var t = mesh.triangles;
            if (v.Length == 0 || t.Length == 0) return result;

            // Weld: every corner to the first corner at its place.
            var canon = new int[v.Length];
            var seen = new Dictionary<Vector3Int, int>(v.Length);
            for (var i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x / Weld), Mathf.RoundToInt(v[i].y / Weld), Mathf.RoundToInt(v[i].z / Weld));
                if (seen.TryGetValue(key, out var first)) canon[i] = first;
                else { seen[key] = i; canon[i] = i; }
            }

            var parent = new int[v.Length];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
            for (var i = 0; i + 2 < t.Length; i += 3)
            {
                Join(canon[t[i]], canon[t[i + 1]]);
                Join(canon[t[i]], canon[t[i + 2]]);
            }

            var parts = new Dictionary<int, Bounds>();
            for (var i = 0; i < t.Length; i++)
            {
                var root = Find(canon[t[i]]);
                var p = v[t[i]];
                if (parts.TryGetValue(root, out var b)) { b.Encapsulate(p); parts[root] = b; }
                else parts[root] = new Bounds(p, Vector3.zero);
            }
            foreach (var b in parts.Values)
                if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) >= minSize) result.Add(b);
            result.Sort((a, b) => b.size.sqrMagnitude.CompareTo(a.size.sqrMagnitude));
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
            return result;
        }

        /// <summary>
        /// The figures standing on a group joined into one piece (the five Buddhas and their terrace are one connected
        /// mesh): the geometry above <paramref name="above"/> of the mesh's height, clustered by where it stands on the
        /// floor plan. Each cluster's box runs from <paramref name="from"/> of the height to its top. Largest first.
        /// </summary>
        public static List<Bounds> Figures(Mesh mesh, float above, float from, int grid, int minCells, int max)
        {
            var result = new List<Bounds>();
            if (mesh == null) return result;
            var v = mesh.vertices;
            if (v.Length == 0) return result;
            var b = mesh.bounds;
            var cut = b.min.y + above * b.size.y;
            var cell = Mathf.Max(b.size.x, b.size.z) / Mathf.Max(4, grid);
            if (cell <= 0f) return result;
            int nx = Mathf.CeilToInt(b.size.x / cell) + 1, nz = Mathf.CeilToInt(b.size.z / cell) + 1;
            var occupied = new bool[nx, nz];
            int CX(float x) => Mathf.Clamp((int)((x - b.min.x) / cell), 0, nx - 1);
            int CZ(float z) => Mathf.Clamp((int)((z - b.min.z) / cell), 0, nz - 1);
            foreach (var p in v) if (p.y >= cut) occupied[CX(p.x), CZ(p.z)] = true;

            var label = new int[nx, nz];
            var cells = new List<int>();
            var next = 0;
            var stack = new Stack<(int, int)>();
            for (var i = 0; i < nx; i++)
                for (var j = 0; j < nz; j++)
                {
                    if (!occupied[i, j] || label[i, j] != 0) continue;
                    next++; var count = 0;
                    stack.Push((i, j)); label[i, j] = next;
                    while (stack.Count > 0)
                    {
                        var (ci, cj) = stack.Pop(); count++;
                        for (var di = -1; di <= 1; di++)
                            for (var dj = -1; dj <= 1; dj++)
                            {
                                int ni = ci + di, nj = cj + dj;
                                if (ni < 0 || nj < 0 || ni >= nx || nj >= nz || !occupied[ni, nj] || label[ni, nj] != 0) continue;
                                label[ni, nj] = next; stack.Push((ni, nj));
                            }
                    }
                    cells.Add(count);
                }

            var boxes = new Dictionary<int, Bounds>();
            foreach (var p in v)
            {
                if (p.y < cut) continue;
                var id = label[CX(p.x), CZ(p.z)];
                if (id == 0 || cells[id - 1] < minCells) continue;
                if (boxes.TryGetValue(id, out var bb)) { bb.Encapsulate(p); boxes[id] = bb; } else boxes[id] = new Bounds(p, Vector3.zero);
            }
            var floor = b.min.y + from * b.size.y;
            foreach (var bb in boxes.Values)
            {
                var lo = new Vector3(bb.min.x, Mathf.Min(floor, bb.min.y), bb.min.z);
                var r = new Bounds(); r.SetMinMax(lo, bb.max);
                result.Add(r);
            }
            result.Sort((x, y) => y.size.sqrMagnitude.CompareTo(x.size.sqrMagnitude));
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
            return result;
        }
    }
}
