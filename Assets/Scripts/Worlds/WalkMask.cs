using System.Collections.Generic;
using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>
    /// Where a person can stand in a splat world, worked out from the splats themselves.
    ///
    /// <b>Why from the splats.</b> The worlds in SplatPortal have no walls anyone can collide with:
    /// they are all drawn over one invisible floor, so a NavMesh baked on it lets the painters stand
    /// inside a splat wall whenever the visitor walks beside one. Marble ships collider meshes for
    /// only two of the four worlds, and those are partial and X-mirrored. Every world does have its
    /// splats, though, and a Chisel room is simple: a floor at the visitor's floor height, walls and
    /// furniture standing on it, nothing floating in the middle.
    ///
    /// <b>The rule, per floor cell.</b> Walkable if there is floor under it or next to it, nothing
    /// solid at body height, and it can be reached from a seed (the world's spawn) without crossing
    /// either. Requiring floor is what stops the fill leaking out of a doorway: outside the building
    /// there is no floor. Measured on the four SplatPortal worlds (28 Sep 2026): walls, pillars and
    /// the gallery's bench come out solid, and the interiors come out clear.
    ///
    /// Pure arithmetic over points, so it is tested in EditMode; the scene side is
    /// <see cref="SplatWalkArea"/> and the editor bake.
    /// </summary>
    public static class WalkMask
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Grid cell size, metres.")]
            public float cell = 0.5f;
            [Tooltip("Splats within this band around the floor height count as floor.")]
            public float floorBelow = 0.25f, floorAbove = 0.2f;
            [Tooltip("Splats in this band above the floor count as something a body would walk into.")]
            public float bodyFrom = 0.4f, bodyTo = 1.8f;
            [Tooltip("Floor splats a cell needs to count as floor.")]
            public int floorMin = 3;
            [Tooltip("Body-height splats that make a cell solid. 4 per 0.5 m cell catches walls and " +
                     "pillars in all four worlds and leaves their interiors clear.")]
            public int solidMin = 4;
            [Tooltip("Only splats at least this opaque count. Faint ones are haze and floaters.")]
            public float minOpacity = 0.5f;
            [Tooltip("Half-width of the square searched round the seeds, metres.")]
            public float reach = 40f;
            [Tooltip("Close cracks in walls and notches in pillars up to this many cells wide (grow the " +
                     "solid cells, then shrink them back). 2 cells = 1 m: seals the one-cell gaps " +
                     "found in the Buddha Hall's walls and pillars, leaves 2.4 m doorways open.")]
            public int closeCells = 2;
        }

        public sealed class Grid
        {
            public float x0, z0, cell;
            public int width, height;
            public int[] floor, body;
            public bool[] walkable;

            public int Index(int x, int z) => z * width + x;
            public bool InRange(int x, int z) => x >= 0 && z >= 0 && x < width && z < height;
            public bool Walkable(int x, int z) => InRange(x, z) && walkable[Index(x, z)];

            public bool WalkableAt(Vector2 p)
            {
                int x = Mathf.FloorToInt((p.x - x0) / cell), z = Mathf.FloorToInt((p.y - z0) / cell);
                return Walkable(x, z);
            }

            public Vector2 CellCentre(int x, int z) => new Vector2(x0 + (x + 0.5f) * cell, z0 + (z + 0.5f) * cell);

            public int WalkableCount
            {
                get { int n = 0; foreach (var w in walkable) if (w) n++; return n; }
            }
        }

        /// <summary>Opacity from the logit a splat file stores it as.</summary>
        public static float Opacity(float logit) => 1f / (1f + Mathf.Exp(-logit));

        /// <summary>
        /// Build the walkable grid.
        /// </summary>
        /// <param name="points">Opaque splat centres, in world space.</param>
        /// <param name="floorY">The visitor's floor height, world space.</param>
        /// <param name="seeds">Floor points known to be inside, e.g. the spawn.</param>
        public static Grid Build(IReadOnlyList<Vector3> points, float floorY, IReadOnlyList<Vector2> seeds, Settings s)
        {
            s ??= new Settings();
            var centre = Vector2.zero;
            foreach (var p in seeds) centre += p;
            if (seeds.Count > 0) centre /= seeds.Count;

            int n = Mathf.CeilToInt(2f * s.reach / s.cell);
            var g = new Grid
            {
                cell = s.cell, width = n, height = n,
                x0 = centre.x - s.reach, z0 = centre.y - s.reach,
                floor = new int[n * n], body = new int[n * n], walkable = new bool[n * n],
            };

            foreach (var p in points)
            {
                int x = Mathf.FloorToInt((p.x - g.x0) / s.cell), z = Mathf.FloorToInt((p.z - g.z0) / s.cell);
                if (!g.InRange(x, z)) continue;
                float h = p.y - floorY;
                if (h >= -s.floorBelow && h <= s.floorAbove) g.floor[g.Index(x, z)]++;
                else if (h >= s.bodyFrom && h <= s.bodyTo) g.body[g.Index(x, z)]++;
            }

            var solid = new bool[n * n];
            for (int i = 0; i < solid.Length; i++) solid[i] = g.body[i] >= s.solidMin;
            solid = Close(solid, n, s.closeCells);

            // A cell can be stood on if it is not solid and it or a neighbour shows floor. The
            // neighbour rule bridges dark floor patches that shed few opaque splats.
            var open = new bool[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                if (solid[g.Index(x, z)]) continue;
                bool floored = false;
                for (int dz = -1; dz <= 1 && !floored; dz++)
                for (int dx = -1; dx <= 1 && !floored; dx++)
                    if (g.InRange(x + dx, z + dz) && g.floor[g.Index(x + dx, z + dz)] >= s.floorMin) floored = true;
                open[g.Index(x, z)] = floored;
            }

            // Flood from every seed, 4-connected, so a wall one cell thick is a wall.
            var queue = new Queue<int>();
            foreach (var seed in seeds)
            {
                int start = NearestOpen(g, open, seed, 6);
                if (start < 0 || g.walkable[start]) continue;
                g.walkable[start] = true;
                queue.Enqueue(start);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % n, z = i / n;
                Visit(g, open, queue, x + 1, z);
                Visit(g, open, queue, x - 1, z);
                Visit(g, open, queue, x, z + 1);
                Visit(g, open, queue, x, z - 1);
            }
            return g;
        }

        /// <summary>
        /// Morphological closing: grow every solid cell by <paramref name="gapCells"/>/2 (square
        /// neighbourhood), then shrink by the same. Gaps up to <paramref name="gapCells"/> wide
        /// fill; nothing wider changes, and the solid never ends up bigger than it started except
        /// inside those gaps.
        /// </summary>
        public static bool[] Close(bool[] solid, int n, int gapCells)
        {
            int r = gapCells / 2;
            if (r <= 0) return solid;
            return Morph(Morph(solid, n, r, grow: true), n, r, grow: false);
        }

        static bool[] Morph(bool[] src, int n, int r, bool grow)
        {
            var dst = new bool[src.Length];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                bool any = false, all = true;
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = x + dx, zz = z + dz;
                    bool v = xx >= 0 && zz >= 0 && xx < n && zz < n ? src[zz * n + xx] : !grow;
                    any |= v; all &= v;
                }
                dst[z * n + x] = grow ? any : all;
            }
            return dst;
        }

        static void Visit(Grid g, bool[] open, Queue<int> queue, int x, int z)
        {
            if (!g.InRange(x, z)) return;
            int i = g.Index(x, z);
            if (!open[i] || g.walkable[i]) return;
            g.walkable[i] = true;
            queue.Enqueue(i);
        }

        /// <summary>The open cell nearest a seed, within <paramref name="radiusCells"/>, or -1.
        /// A spawn can sit on a patch of dark floor that sheds no opaque splats.</summary>
        static int NearestOpen(Grid g, bool[] open, Vector2 seed, int radiusCells)
        {
            int sx = Mathf.FloorToInt((seed.x - g.x0) / g.cell), sz = Mathf.FloorToInt((seed.y - g.z0) / g.cell);
            int best = -1, bestD = int.MaxValue;
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                int x = sx + dx, z = sz + dz;
                if (!g.InRange(x, z) || !open[g.Index(x, z)]) continue;
                int d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = g.Index(x, z); }
            }
            return best;
        }

        /// <summary>
        /// The walkable cells as rectangles, each row's runs merged, as (min, max) floor corners.
        /// A 30 m room at 0.5 m is a few hundred rectangles rather than thousands of cells.
        /// </summary>
        public static List<(Vector2 min, Vector2 max)> Rectangles(Grid g)
        {
            var result = new List<(Vector2, Vector2)>();
            for (int z = 0; z < g.height; z++)
            {
                int x = 0;
                while (x < g.width)
                {
                    if (!g.Walkable(x, z)) { x++; continue; }
                    int start = x;
                    while (x < g.width && g.Walkable(x, z)) x++;
                    result.Add((new Vector2(g.x0 + start * g.cell, g.z0 + z * g.cell),
                                new Vector2(g.x0 + x * g.cell, g.z0 + (z + 1) * g.cell)));
                }
            }
            return result;
        }
    }
}
