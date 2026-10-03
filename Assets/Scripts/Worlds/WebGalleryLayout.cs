using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>Where one painting hangs: the centre of its canvas, and the point it turns to face.</summary>
    public struct WebHang
    {
        public Vector3 centre;
        public Vector3 faces;
        /// <summary>True when it backs onto a wall (or hedge, bank); false when it stands free.</summary>
        public bool onWall;
    }

    /// <summary>
    /// muse-infinity's own picture layout (museum3d.js <c>placeArtworksInWorld</c>), ported rule for
    /// rule so the journey hangs the chapter's works where the web app does:
    ///
    ///   * up to eight works, in tour order, along the direction the visitor arrives facing: the
    ///     first about 7 m ahead, then every 6.5 m, walking a stop in only as far as real ground
    ///     carries it (full, 80, 62, 48, 35 per cent);
    ///   * alternately on the visitor's left and right of that walk line;
    ///   * against a solid stretch of wall when five probe rays all hit within 0.6 m of each other
    ///     (tried at the stop and 2.5 m either way along the walk), else against whatever a single
    ///     centre ray meets (a hedge, a bank) standing 0.55 m proud, else freestanding 3.2 m out
    ///     (pulled in, or swapped to the other side, until it stands over ground);
    ///   * kept 0.5 m off the tightest of three wall rays (centre and both frame edges);
    ///   * freestanding ones clamped 0.4 m inside the walk bounds;
    ///   * hovering 0.35 m above the ground with the canvas centre 1.5 m above that, turned back
    ///     toward the walk line.
    ///
    /// The ground and wall tests are handed in, so the rule is the same whatever answers them
    /// (the world's collider at runtime, a fake in tests).
    /// </summary>
    public static class WebGalleryLayout
    {
        public const float Lateral = 3.2f;
        public const float CanvasAboveGround = 0.35f + 1.5f;
        /// <summary>Her canvas: the longer side 1.15 m.</summary>
        public const float LongSide = 1.15f;

        /// <param name="ground">The floor height at an (x, z), or NaN where there is no ground.</param>
        /// <param name="wall">Distance from a point along a horizontal direction to a wall, or null.</param>
        public static List<WebHang> Place(int count, Vector3 spawn, Vector3 facing, Bounds? walkable,
                                          System.Func<float, float, float> ground,
                                          System.Func<Vector3, Vector3, float, float?> wall)
        {
            var hangs = new List<WebHang>();
            var fwd = new Vector3(facing.x, 0f, facing.z).normalized;
            // HER right, in her coordinates: (-fwd.z, 0, fwd.x). The splats are read into Unity's
            // left-handed frame unconverted, so every capture is a mirror image of hers and her
            // right is the Unity visitor's left. Using her right hangs each work at her numbers,
            // against the same stretch of the same capture.
            var right = new Vector3(-fwd.z, 0f, fwd.x);
            bool Has(float x, float z) => !float.IsNaN(ground(x, z));
            float G(float x, float z) { var y = ground(x, z); return float.IsNaN(y) ? spawn.y : y; }

            int n = Mathf.Min(count, 8);
            for (int i = 0; i < n; i++)
            {
                float sideSign = i % 2 == 0 ? -1f : 1f;
                var side = right * sideSign;

                float planned = 7f + i * 6.5f;
                float reach = planned * 0.35f;
                foreach (var factor in new[] { 1f, 0.8f, 0.62f, 0.48f, 0.35f })
                {
                    float r = planned * factor;
                    var t = spawn + fwd * r;
                    if (Has(t.x, t.z) && Has(t.x + side.x * Lateral, t.z + side.z * Lateral)) { reach = r; break; }
                }
                var c0 = spawn + fwd * reach;

                // Five probes: centre, below, above, and 0.6 m along the wall each way.
                float? WallAt(float ax, float az)
                {
                    float gy = G(ax, az);
                    var probes = new[] { (0f, 1.5f), (0f, 0.9f), (0f, 2.0f), (-0.6f, 1.5f), (0.6f, 1.5f) };
                    var d = new List<float>();
                    foreach (var (along, up) in probes)
                    {
                        var o = new Vector3(ax + fwd.x * along, gy + up, az + fwd.z * along);
                        var hit = wall(o, side, Lateral + 8f);
                        if (hit == null) return null;
                        d.Add(hit.Value);
                    }
                    d.Sort();
                    if (d[4] - d[0] > 0.6f) return null;
                    return d[2];
                }

                float wx = c0.x, wz = c0.z;
                float? wallDist = null;
                foreach (var shift in new[] { 0f, 2.5f, -2.5f })
                {
                    float ax = c0.x + fwd.x * shift, az = c0.z + fwd.z * shift;
                    var dd = WallAt(ax, az);
                    if (dd != null && dd > 1.2f) { wallDist = dd; wx = ax; wz = az; break; }
                }

                if (wallDist == null)
                {
                    var near = wall(new Vector3(wx, G(wx, wz) + 1.5f, wz), side, 14f);
                    if (near != null && near > 1.4f)
                    {
                        float backed = near.Value - 0.55f;
                        if (Has(wx + side.x * backed, wz + side.z * backed)) wallDist = near;
                    }
                }

                if (wallDist != null)
                {
                    float nearest = wallDist.Value;
                    foreach (var edge in new[] { -0.85f, 0.85f })
                    {
                        float ex = wx + fwd.x * edge, ez = wz + fwd.z * edge;
                        var h = wall(new Vector3(ex, G(ex, ez) + 1.5f, ez), side, wallDist.Value + 6f);
                        if (h != null) nearest = Mathf.Min(nearest, h.Value);
                    }
                    wallDist = Mathf.Max(1.2f, nearest - 0.5f);
                }

                float off = wallDist ?? Lateral;
                if (wallDist == null)
                {
                    foreach (var candidate in new[] { Lateral, 2.2f, 1.4f, -Lateral, -2.2f })
                        if (Has(wx + side.x * candidate, wz + side.z * candidate)) { off = candidate; break; }
                }

                float px = wx + side.x * off, pz = wz + side.z * off;
                if (wallDist == null && walkable.HasValue)
                {
                    var b = walkable.Value;
                    px = Mathf.Clamp(px, b.min.x + 0.4f, b.max.x - 0.4f);
                    pz = Mathf.Clamp(pz, b.min.z + 0.4f, b.max.z - 0.4f);
                }

                hangs.Add(new WebHang
                {
                    centre = new Vector3(px, G(px, pz) + CanvasAboveGround, pz),
                    faces = new Vector3(wx, 0f, wz),
                    onWall = wallDist != null,
                });
            }
            return hangs;
        }

        /// <summary>Her canvas size for an image of this aspect: the longer side 1.15 m.</summary>
        public static Vector2 CanvasSize(float aspect) =>
            aspect >= 1f ? new Vector2(LongSide, LongSide / aspect) : new Vector2(LongSide * aspect, LongSide);

        /// <summary>The Quad rotation that shows the canvas toward <paramref name="faces"/> (a Quad shows its -Z).</summary>
        public static Quaternion QuadRotation(Vector3 centre, Vector3 faces)
        {
            var toward = new Vector3(faces.x - centre.x, 0f, faces.z - centre.z);
            if (toward.sqrMagnitude < 1e-6f) toward = Vector3.forward;
            return Quaternion.LookRotation(-toward.normalized, Vector3.up);
        }
    }
}
