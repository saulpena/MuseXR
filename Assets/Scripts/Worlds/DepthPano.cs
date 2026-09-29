using System;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// The depth panorama Marble's <c>pano:depth_to_rgb</c> takes: a 2:1 equirectangular image of
    /// RADIAL distance from one capture point, log-encoded between the nearest and farthest surface.
    ///
    /// The encoding is World Labs' own, copied from their example
    /// (worldlabs-api-examples/web-chisel-depth-png, index.html): near = white, far = black,
    /// <c>1 - (ln d - ln zMin) / (ln zMax - ln zMin)</c>, and a ray that hits nothing is black.
    /// zMin/zMax are sent alongside the PNG so the service can decode it. The pixel layout follows
    /// the same example: the image centre looks forward, the top row straight up, and left-to-right
    /// sweeps the way the viewer turns right. In Unity, forward is +Z.
    ///
    /// Pure maths plus a distance callback, so it is testable with no scene and no physics.
    /// </summary>
    public static class DepthPano
    {
        public const int Width = 2048;
        public const int Height = 1024;

        /// <summary>Direction for image coordinates u, v in [0, 1], v = 0 the TOP row.</summary>
        public static Vector3 Direction(float u, float v)
        {
            float theta = u * 2f * Mathf.PI - Mathf.PI;
            float phi = v * Mathf.PI;
            float s = Mathf.Sin(phi);
            // three.js writes (sin phi sin theta, cos phi, -cos theta sin phi) with forward -Z;
            // Unity's forward is +Z, so the z term changes sign.
            return new Vector3(s * Mathf.Sin(theta), Mathf.Cos(phi), Mathf.Cos(theta) * s);
        }

        /// <summary>Direction through the centre of pixel (x, y), y = 0 the top row.</summary>
        public static Vector3 PixelDirection(int x, int y, int width, int height) =>
            Direction((x + 0.5f) / width, (y + 0.5f) / height);

        public sealed class Result
        {
            public int width, height;
            /// <summary>Radial distance per pixel, row 0 at the top. 0 means nothing was hit.</summary>
            public float[] depth;
            public float zMin, zMax;
            public int misses;
        }

        /// <summary>
        /// Measures every pixel. <paramref name="distance"/> returns the radial distance along a unit
        /// direction, or a value &lt;= 0 / non-finite for a miss.
        /// </summary>
        public static Result Measure(Func<Vector3, float> distance, int width = Width, int height = Height)
        {
            var r = new Result { width = width, height = height, depth = new float[width * height],
                                 zMin = float.PositiveInfinity, zMax = 0f };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float d = distance(PixelDirection(x, y, width, height));
                if (!(d > 0f) || float.IsInfinity(d)) { r.misses++; continue; }
                r.depth[y * width + x] = d;
                if (d < r.zMin) r.zMin = d;
                if (d > r.zMax) r.zMax = d;
            }
            if (r.zMax <= 0f) { r.zMin = 0f; r.zMax = 0f; }
            return r;
        }

        /// <summary>World Labs' log encoding to one byte. A miss (d &lt;= 0) is 0, like theirs.</summary>
        public static byte Encode(float d, float zMin, float zMax)
        {
            if (!(d > 0f)) return 0;
            float safeMin = Mathf.Max(zMin, 0.001f);
            float safeMax = Mathf.Max(zMax, safeMin + 0.001f);
            float denom = Mathf.Max(Mathf.Log(safeMax) - Mathf.Log(safeMin), 0.00001f);
            float n = Mathf.Clamp01((Mathf.Log(Mathf.Max(d, 0.001f)) - Mathf.Log(safeMin)) / denom);
            return (byte)Mathf.RoundToInt((1f - n) * 255f);
        }

        /// <summary>Inverse of <see cref="Encode"/>, for checking a written image against the scene.</summary>
        public static float Decode(byte b, float zMin, float zMax)
        {
            float n = 1f - b / 255f;
            return Mathf.Exp(Mathf.Log(zMin) + n * (Mathf.Log(zMax) - Mathf.Log(zMin)));
        }

        /// <summary>One grey byte per pixel, row 0 at the top.</summary>
        public static byte[] EncodeGray(Result r)
        {
            var bytes = new byte[r.depth.Length];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Encode(r.depth[i], r.zMin, r.zMax);
            return bytes;
        }
    }
}
