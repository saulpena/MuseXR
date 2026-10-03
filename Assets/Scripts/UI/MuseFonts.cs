using TMPro;
using UnityEngine;

namespace MuseXR.UI
{
    /// <summary>Her type: Cormorant Garamond for titles and quotes, Inter and JetBrains Mono for the rest.
    /// One asset at Resources/MuseFonts so any panel can find the faces without wiring.</summary>
    [CreateAssetMenu(menuName = "MuseXR/UI Fonts")]
    public sealed class MuseFonts : ScriptableObject
    {
        public TMP_FontAsset serif;         // Cormorant Garamond SemiBold (600)
        public TMP_FontAsset serifMedium;   // Cormorant Garamond Medium (500): quotes and answers
        public TMP_FontAsset serifItalic;   // Cormorant Garamond Medium Italic (500)
        public TMP_FontAsset sans;          // Inter Regular
        public TMP_FontAsset sansSemi;      // Inter SemiBold (600)
        public TMP_FontAsset sansBold;      // Inter Bold (700)
        public TMP_FontAsset mono;          // JetBrains Mono SemiBold

        static MuseFonts _loaded;
        public static MuseFonts Get() => _loaded != null ? _loaded : (_loaded = Resources.Load<MuseFonts>("MuseFonts"));
    }
}
