using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// A picture's true proportions - width over height of the photograph as it was scanned.
    ///
    /// The museum's pictures import scaled up to a power of two, because on Android Unity will not compress a
    /// non-power-of-two texture that has mipmaps: left at 1679x2048 each painting shipped as RGBA32, 17.5 MB apiece
    /// and 226 MB for the set, against ~2.5 MB as ASTC (measured 5 Oct 2026). Stretching changes nothing on screen -
    /// every quad samples UV 0..1 - but it does change <c>texture.width / texture.height</c>, which is where every
    /// frame, crop and card took its shape from. So ask here instead: the table (Resources/PictureAspects.txt,
    /// written by the editor from the source images and checked by PictureAspectTests) knows the real proportions,
    /// and anything not in it is answered from the texture as before.
    /// </summary>
    public static class PictureAspect
    {
        public const string ResourceName = "PictureAspects";
        static Dictionary<string, float> _table;

        /// <summary>Width over height of the picture, or <paramref name="fallback"/> with no texture.</summary>
        public static float Of(Texture texture, float fallback = 1f)
        {
            if (texture == null || texture.height <= 0) return fallback;
            return Table().TryGetValue(texture.name, out var a) ? a : texture.width / (float)texture.height;
        }

        /// <summary>Parses "name width height" lines. Public so the table's format is tested, not assumed.</summary>
        public static Dictionary<string, float> Parse(string text)
        {
            var d = new Dictionary<string, float>();
            if (string.IsNullOrEmpty(text)) return d;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split(' ');
                if (parts.Length != 3) continue;
                if (int.TryParse(parts[1], out var w) && int.TryParse(parts[2], out var h) && w > 0 && h > 0)
                    d[parts[0]] = w / (float)h;
            }
            return d;
        }

        static Dictionary<string, float> Table()
        {
            if (_table != null) return _table;
            var asset = Resources.Load<TextAsset>(ResourceName);
            _table = Parse(asset != null ? asset.text : null);
            if (asset != null) Resources.UnloadAsset(asset);
            return _table;
        }
    }
}
