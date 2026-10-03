using System;
using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>One point of her memory architecture: position (her units, metres), family, size, seed.</summary>
    public struct MemoryPoint
    {
        public float X, Y, Z;
        public string Family;
        public float Size;
        public float Seed;
    }

    /// <summary>
    /// muse-infinity's lib/memoryWorld.js, ported line for line: "the impossible manor", the point
    /// cloud her stages 05-09 play over. A floor of broken perspective traces, a facade with a
    /// central arched door and two arched windows under a gable, colonnades on both sides reaching
    /// toward the viewer, foreground branches, and drifting dust. Her y = -2.5 is the floor and +z
    /// runs away from the viewer; the facade stands at z 15.5.
    ///
    /// Pure and deterministic (her own hash), so the same cloud every time and testable.
    /// </summary>
    public static class MemoryWorld
    {
        const float Tau = (float)(Math.PI * 2);

        /// <summary>Her memoryPalette, as 0-255 RGB.</summary>
        public static readonly Dictionary<string, int[]> Palette = new Dictionary<string, int[]>
        {
            { "stone", new[] { 138, 128, 118 } },
            { "ivory", new[] { 226, 216, 196 } },
            { "gold", new[] { 203, 161, 102 } },
            { "cyan", new[] { 78, 145, 146 } },
            { "garden", new[] { 54, 105, 90 } },
            { "mist", new[] { 88, 107, 111 } },
            { "dust", new[] { 151, 140, 127 } },
        };

        static float Fract(double v) => (float)(v - Math.Floor(v));
        public static float Random(double index, double salt = 0) => Fract(Math.Sin(index * 91.733 + salt * 37.719) * 43758.5453);

        static void Point(List<MemoryPoint> p, float x, float y, float z, string family, float size, double seed) =>
            p.Add(new MemoryPoint { X = x, Y = y, Z = z, Family = family, Size = size, Seed = Random(seed, 8) * Tau });

        static void Line(List<MemoryPoint> p, float[] a, float[] b, int count, string family, float size, int salt)
        {
            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0f : i / (float)(count - 1);
                var scatter = (Random(i + salt, 2) - .5f) * .035f;
                Point(p, a[0] + (b[0] - a[0]) * t + scatter, a[1] + (b[1] - a[1]) * t + scatter,
                      a[2] + (b[2] - a[2]) * t + scatter, family, size, i + salt);
            }
        }

        static void Plane(List<MemoryPoint> p, float[] o, float[] u, float[] v, int columns, int rows, string family,
                          Func<float, float, bool> mask, int salt)
        {
            for (var iy = 0; iy < rows; iy++)
                for (var ix = 0; ix < columns; ix++)
                {
                    var nx = columns == 1 ? 0f : ix / (float)(columns - 1);
                    var ny = rows == 1 ? 0f : iy / (float)(rows - 1);
                    if (!mask(nx, ny)) continue;
                    var index = iy * columns + ix + salt;
                    var jitter = (Random(index, 4) - .5f) * .055f;
                    Point(p, o[0] + u[0] * nx + v[0] * ny + jitter, o[1] + u[1] * nx + v[1] * ny + jitter,
                          o[2] + u[2] * nx + v[2] * ny + jitter, family, .75f + Random(index, 5) * .7f, index);
                }
        }

        static void Column(List<MemoryPoint> p, float x, float z, float height, int salt)
        {
            for (var ring = 0; ring < 13; ring++)
            {
                var y = -2.35f + height * ring / 12f;
                var radius = ring < 2 || ring > 10 ? .24f : .15f;
                for (var i = 0; i < 15; i++)
                {
                    var angle = i / 15f * Tau;
                    Point(p, x + (float)Math.Cos(angle) * radius, y, z + (float)Math.Sin(angle) * radius, "ivory", 1.05f, salt + ring * 15 + i);
                }
            }
            Line(p, new[] { x - .36f, -2.35f, z }, new[] { x + .36f, -2.35f, z }, 18, "gold", 1.1f, salt + 300);
            Line(p, new[] { x - .34f, -2.35f + height, z }, new[] { x + .34f, -2.35f + height, z }, 18, "gold", 1.1f, salt + 350);
        }

        static void Arch(List<MemoryPoint> p, float cx, float baseY, float z, float radius, float height, string family, int salt)
        {
            Line(p, new[] { cx - radius, baseY, z }, new[] { cx - radius, baseY + height, z }, 42, family, 1.15f, salt);
            Line(p, new[] { cx + radius, baseY, z }, new[] { cx + radius, baseY + height, z }, 42, family, 1.15f, salt + 50);
            for (var layer = 0; layer < 4; layer++)
            {
                var r = radius + layer * .085f;
                for (var i = 0; i < 72; i++)
                {
                    var angle = Math.PI + i / 71.0 * Math.PI;
                    Point(p, cx + (float)Math.Cos(angle) * r, baseY + height + (float)Math.Sin(angle) * r,
                          z + (Random(i + salt, 3) - .5f) * .07f, family, 1.15f, salt + layer * 100 + i);
                }
            }
        }

        static void Branch(List<MemoryPoint> p, float x, float y, float z, int direction, int salt)
        {
            for (var i = 0; i < 7; i++)
            {
                var bend = (Random(i + salt, 2) - .5f) * 1.1f;
                var end = new[] { x + direction * (1.8f + i * .34f), y + .4f + i * .52f + bend, z + (Random(i + salt, 3) - .5f) * 2.4f };
                Line(p, new[] { x, y + i * .12f, z }, end, 18, "garden", .85f, salt + i * 30);
                for (var leaf = 0; leaf < 18; leaf++)
                {
                    var angle = Random(leaf + salt + i, 5) * Tau;
                    var radius = .08f + Random(leaf + salt + i, 6) * .7f;
                    Point(p, end[0] + (float)Math.Cos(angle) * radius, end[1] + (float)Math.Sin(angle) * radius * .55f,
                          end[2] + (Random(leaf + salt, 7) - .5f) * .8f, "garden", .7f + Random(leaf + salt, 8), salt + i * 50 + leaf);
                }
            }
        }

        /// <summary>Her createMemoryWorld().</summary>
        public static List<MemoryPoint> Create()
        {
            var p = new List<MemoryPoint>();

            // A floor made from broken perspective traces, not a solid grid.
            for (var zi = 0; ; zi++)
            {
                var z = 3f + zi * .72f;
                if (z > 25f) break;
                Line(p, new[] { -8.4f, -2.5f, z }, new[] { 8.4f, -2.5f, z }, 34, z % 2 < 1 ? "mist" : "stone", .65f, (int)Math.Round(z * 70));
            }
            for (var xi = 0; ; xi++)
            {
                var x = -8f + xi * 1.15f;
                if (x > 8f) break;
                Line(p, new[] { x, -2.5f, 3f }, new[] { x, -2.5f, 25f }, 40, "mist", .58f, (int)Math.Round((x + 9) * 90));
            }

            // The impossible manor: a facade, central arch and fractured roof.
            Plane(p, new[] { -5.4f, -2.35f, 15.5f }, new[] { 10.8f, 0f, 0f }, new[] { 0f, 5.7f, 0f }, 62, 31, "stone", (x, y) =>
            {
                var px = x * 10.8f - 5.4f;
                var py = y * 5.7f - 2.35f;
                var centralDoor = Math.Abs(px) < 1.35f && py < 1.45f;
                var window = (Math.Abs(px - 3.35f) < .72f || Math.Abs(px + 3.35f) < .72f) && py > -.4f && py < 1.5f;
                return !centralDoor && !window && Random(Math.Round((x + y) * 1000), 9) > .2f;
            }, 900);
            Arch(p, 0f, -2.35f, 15.32f, 1.35f, 1.25f, "gold", 1500);
            Arch(p, -3.35f, -.42f, 15.3f, .7f, .7f, "cyan", 1800);
            Arch(p, 3.35f, -.42f, 15.3f, .7f, .7f, "cyan", 2050);
            Line(p, new[] { -6.05f, 3.3f, 15.5f }, new[] { 0f, 5.25f, 15.5f }, 105, "ivory", 1.15f, 2200);
            Line(p, new[] { 0f, 5.25f, 15.5f }, new[] { 6.05f, 3.3f, 15.5f }, 105, "ivory", 1.15f, 2400);
            Line(p, new[] { -6.05f, 3.3f, 15.5f }, new[] { 6.05f, 3.3f, 15.5f }, 125, "gold", .9f, 2600);
            Line(p, new[] { -5.8f, -2.35f, 15.4f }, new[] { -5.8f, 3.3f, 15.4f }, 70, "ivory", 1f, 2800);
            Line(p, new[] { 5.8f, -2.35f, 15.4f }, new[] { 5.8f, 3.3f, 15.4f }, 70, "ivory", 1f, 2900);

            // Colonnades pull the facade toward the camera and create true depth.
            var ci = 0;
            for (var z = 5.8f; z < 14.8f; z += 2.1f, ci++)
            {
                Column(p, -4.65f, z, 4.2f, 3100 + ci * 500);
                Column(p, 4.65f, z, 4.2f, 5600 + ci * 500);
                Line(p, new[] { -4.65f, 1.85f, z }, new[] { -4.65f, 1.85f, z + 1.9f }, 35, "stone", .8f, 8000 + ci * 60);
                Line(p, new[] { 4.65f, 1.85f, z }, new[] { 4.65f, 1.85f, z + 1.9f }, 35, "stone", .8f, 8300 + ci * 60);
            }

            // Foreground vegetation frames the architecture and exaggerates parallax.
            Branch(p, -6.7f, -2.2f, 4.2f, 1, 9100);
            Branch(p, 6.8f, -2.25f, 4.7f, -1, 10100);
            Branch(p, -7.5f, -2.3f, 9.5f, 1, 11100);
            Branch(p, 7.7f, -2.3f, 10.5f, -1, 12100);

            // Sparse airborne matter fills the void without turning it into a star field.
            for (var i = 0; i < 980; i++)
                Point(p, (Random(i, 10) - .5f) * 19f, -2f + Random(i, 11) * 8f, 2f + Random(i, 12) * 26f,
                      Random(i, 13) > .76f ? "cyan" : "dust", .35f + Random(i, 14) * .85f, 13000 + i);
            return p;
        }

        /// <summary>
        /// Her finalParticleTarget, in metres in front of the viewer (x right, y up from the floor,
        /// z away): perception settles into a long wavering horizon band, emotion into an elliptical
        /// vortex, invention into a grid like a facade of windows (18 columns, 13 rows).
        /// </summary>
        public static void FinalTarget(string choice, int i, int count, float seedX, float seedY, float time,
                                       out float x, out float y, out float z)
        {
            if (choice == "perception")
            {
                x = (seedX - .5f) * 14f;
                y = 1.1f + seedY * 2.4f + (float)Math.Sin(seedX * 11 + time) * .35f;
                z = 8f;
                return;
            }
            if (choice == "emotion")
            {
                var angle = seedX * Math.PI * 5 + time * .25;
                var radius = .4f + seedY * 3.2f;
                x = (float)Math.Cos(angle) * radius;
                y = 2f + (float)Math.Sin(angle) * radius * .58f;
                z = 7f;
                return;
            }
            const int columns = 18;
            var row = (i / columns) % 13;
            var column = i % columns;
            x = -5f + column * (10f / (columns - 1)) + (float)Math.Sin(row + time) * .08f;
            y = 4.6f - row * (4.2f / 12f) + (float)Math.Cos(column + time) * .06f;
            z = 9f;
        }
    }
}
