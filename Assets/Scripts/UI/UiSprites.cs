using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.UI
{
    /// <summary>
    /// Her shapes, drawn once at runtime and cached: rounded rectangles (fill and 1px outline) as
    /// 9-slice sprites, a soft shadow, a disc, a solid ring and a dashed ring. White, so an Image's
    /// colour tints them. Generated rather than imported so the kit carries no texture assets.
    /// </summary>
    public static class UiSprites
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>A white rounded rectangle, 9-sliced at its corner radius (in px).</summary>
        public static Sprite Rounded(float radiusPx) => Rect("fill", radiusPx, 0f);

        /// <summary>The rounded rectangle's edge only, <paramref name="widthPx"/> wide.</summary>
        public static Sprite Outline(float radiusPx, float widthPx = 1f) => Rect("edge" + widthPx, radiusPx, widthPx);

        static Sprite Rect(string kind, float radiusPx, float edgePx)
        {
            int r = Mathf.Max(2, Mathf.RoundToInt(radiusPx));
            var key = kind + r;
            if (Cache.TryGetValue(key, out var s)) return s;
            const int ss = 4;                       // supersampled: 4x4 per pixel for clean edges
            int size = r * 2 + 4;
            var tex = NewTex(size, size, key);
            var px = new Color32[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cover = 0f;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    float fx = x + (sx + 0.5f) / ss, fy = y + (sy + 0.5f) / ss;
                    float d = RoundedDistance(fx - c, fy - c, c - 1f, c - 1f, r);
                    bool inside = d <= 0f;
                    if (edgePx > 0f) inside &= d > -edgePx;
                    if (inside) cover += 1f;
                }
                byte a = (byte)Mathf.RoundToInt(255f * cover / (ss * ss));
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px); tex.Apply();
            int border = r + 2;
            s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                              SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            Cache[key] = s;
            return s;
        }

        /// <summary>A soft drop shadow for a rounded panel: a rounded rect blurred by <paramref name="blurPx"/>.</summary>
        public static Sprite Shadow(float radiusPx, float blurPx)
        {
            int r = Mathf.RoundToInt(radiusPx), b = Mathf.RoundToInt(blurPx);
            var key = "shadow" + r + "_" + b;
            if (Cache.TryGetValue(key, out var s)) return s;
            int size = (r + b) * 2 + 4;
            var tex = NewTex(size, size, key);
            var px = new Color32[size * size];
            float c = size / 2f, half = r + 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDistance(x + 0.5f - c, y + 0.5f - c, half, half, r);
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d + b * 0.15f) / b));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
            }
            tex.SetPixels32(px); tex.Apply();
            int border = r + b + 2;
            s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                              SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            Cache[key] = s;
            return s;
        }

        /// <summary>The faked frost: white light top-left, warm at the right, inside a rounded rect.
        /// Stretched (not sliced) across the panel, so the corner is approximate - it is faint.</summary>
        public static Sprite Sheen(float radiusPx)
        {
            var key = "sheen" + Mathf.RoundToInt(radiusPx);
            if (Cache.TryGetValue(key, out var s)) return s;
            const int w = 128, h = 96;
            var tex = NewTex(w, h, key);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;              // v = 0 bottom
                float light = Mathf.Clamp01(1.1f - (u * 0.6f + (1f - v) * 0.9f)) * 0.55f;
                float warm = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.55f) / 0.45f)) * 0.22f;
                float edge = Mathf.Clamp01(RoundedDistance((u - 0.5f) * w, (v - 0.5f) * h, w / 2f - 1f, h / 2f - 1f, 10f) * -0.6f);
                var c = Color.Lerp(new Color(1f, 1f, 1f, light), new Color(0.96f, 0.88f, 0.78f, warm), warm / (light + warm + 1e-4f));
                c.a = Mathf.Max(light, warm) * edge;
                px[y * w + x] = c;
            }
            tex.SetPixels32(px); tex.Apply();
            s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }

        /// <summary>Her time-ring icons ("the ring always shows text plus icon: mist, sun, sunset").</summary>
        public static Sprite Icon(string kind)
        {
            var key = "icon-" + kind;
            if (Cache.TryGetValue(key, out var s)) return s;
            const int n = 96, ss = 3;
            var tex = NewTex(n, n, key);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float cover = 0f;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    float u = (x + (sx + 0.5f) / ss) / n * 2f - 1f, v = (y + (sy + 0.5f) / ss) / n * 2f - 1f;
                    if (IconCovers(kind, u, v)) cover += 1f;
                }
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * cover / (ss * ss)));
            }
            tex.SetPixels32(px); tex.Apply();
            s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }

        static bool IconCovers(string kind, float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            switch (kind)
            {
                case "sun":    // a disc with eight rays
                {
                    if (r < 0.36f) return true;
                    float a = Mathf.Atan2(v, u) / (Mathf.PI / 4f);
                    return r > 0.52f && r < 0.86f && Mathf.Abs(a - Mathf.Round(a)) < 0.13f;
                }
                case "sunset": // half a sun on a horizon, three rays
                {
                    if (v < -0.18f && v > -0.32f && Mathf.Abs(u) < 0.9f) return true;
                    if (v < -0.18f) return false;
                    float dy = v + 0.18f, rr = Mathf.Sqrt(u * u + dy * dy);
                    if (rr < 0.42f) return true;
                    float a = Mathf.Atan2(dy, u) / (Mathf.PI / 4f);
                    return rr > 0.56f && rr < 0.84f && Mathf.Abs(a - Mathf.Round(a)) < 0.12f && dy > 0.05f;
                }
                default:       // mist: three soft wavy bands
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        float cy = i * 0.42f + 0.06f * Mathf.Sin(u * 5f + i);
                        if (Mathf.Abs(v - cy) < 0.09f && Mathf.Abs(u) < 0.85f - 0.15f * Mathf.Abs(i)) return true;
                    }
                    return false;
                }
            }
        }

        /// <summary>A filled disc.</summary>
        public static Sprite Disc() => Ring("disc", 0f, 0f, 0f);

        /// <summary>A ring of <paramref name="widthFraction"/> of the radius; dashed if dashes &gt; 0.</summary>
        public static Sprite Ring(float widthFraction, int dashes = 0) => Ring("ring", widthFraction, dashes, 0.5f);

        static Sprite Ring(string kind, float width, float dashes, float duty)
        {
            var key = kind + width + "_" + dashes;
            if (Cache.TryGetValue(key, out var s)) return s;
            const int size = 128, ss = 3;
            var tex = NewTex(size, size, key);
            var px = new Color32[size * size];
            float c = size / 2f, R = c - 1.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cover = 0f;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    float fx = x + (sx + 0.5f) / ss - c, fy = y + (sy + 0.5f) / ss - c;
                    float d = Mathf.Sqrt(fx * fx + fy * fy);
                    bool inside = d <= R && (width <= 0f || d >= R * (1f - width));
                    if (inside && dashes > 0f)
                    {
                        float ang = (Mathf.Atan2(fy, fx) / (2f * Mathf.PI) + 1f) % 1f;
                        inside = (ang * dashes) % 1f < duty;
                    }
                    if (inside) cover += 1f;
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * cover / (ss * ss)));
            }
            tex.SetPixels32(px); tex.Apply();
            s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }

        static Texture2D NewTex(int w, int h, string name) =>
            new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "ui-" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };

        /// <summary>Signed distance from a point to a rounded box of half-size (hx, hy) and corner radius r.</summary>
        static float RoundedDistance(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
    }
}
