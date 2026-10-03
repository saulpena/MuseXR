using UnityEngine;

namespace MuseXR.UI
{
    /// <summary>
    /// Skylar's VR interface system (her plan, section 04), as tokens. Read from her page's CSS
    /// (:root, .gp, .btn, .av, .opt, .answer, .slot, .memento), not eyeballed.
    ///
    /// Sizes are in her CSS pixels; <see cref="PanelScale"/> turns them into metres for a panel's
    /// viewing distance. Fonts: her serif is Cormorant Garamond (600, and 500 italic). Her sans and
    /// mono are Apple system faces that cannot ship, so Inter and JetBrains Mono stand in.
    /// </summary>
    public static class MuseTheme
    {
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public static readonly Color Ink = Hex("#26221d");
        public static readonly Color Ink2 = Hex("#5d554b");
        public static readonly Color Ink3 = Hex("#8d8478");
        public static readonly Color Line = Hex("#e7dfd2");
        public static readonly Color Gold = Hex("#b8913f");
        public static readonly Color GoldSoft = Hex("#f3e9d2");
        public static readonly Color GoldInk = Hex("#8a6a24");
        public static readonly Color Rose = Hex("#c0587a");
        public static readonly Color RoseSoft = Hex("#f7e3ea");
        public static readonly Color Paper = Color.white;
        public static readonly Color MementoPaper = Hex("#fffdf8");
        public static readonly Color GlyphBack = Hex("#efe8db");

        /// <summary>Her .gp: rgba(255,253,249,.8) over a 22px blur. Unity has no backdrop blur, so a
        /// little more opacity keeps text readable over a busy splat.</summary>
        public static readonly Color Glass = new Color(1f, 253f / 255f, 249f / 255f, 0.8f);   // her --glass: rgba(255,253,249,.8)
        public static readonly Color GlassEdge = new Color(1f, 1f, 1f, 0.9f);
        public static readonly Color Shadow = new Color(40f / 255f, 30f / 255f, 15f / 255f, 0.22f);

        /// <summary>The purple AI tag (her "purple AI tag, distinct from real works").</summary>
        public static readonly Color AiTagBack = Hex("#ece6f7");
        public static readonly Color AiTagInk = Hex("#6b52a8");

        /// <summary>Each master's colour: her --monet, --vg, --soc; the rest pick up the roster.</summary>
        public static Color Master(string id)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "monet": return Hex("#5f9c92");
                case "van_gogh": case "vangogh": return Hex("#c4952f");
                case "socrates": return Hex("#7b7266");
                case "frida": return Hex("#b8566a");
                case "hilma": return Hex("#6f86b8");
                case "morisot": return Hex("#9a7fae");
                default: return Ink3;
            }
        }

        public static string Initial(string id)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "monet": return "M";
                case "van_gogh": case "vangogh": return "V";
                case "socrates": return "S";
                case "frida": return "F";
                case "hilma": return "H";
                case "morisot": return "B";
                default: return string.IsNullOrEmpty(id) ? "?" : id.Substring(0, 1).ToUpperInvariant();
            }
        }

        // Her px sizes.
        public const float PanelRadius = 22f, CardRadius = 18f, OptionRadius = 14f, GlyphRadius = 6f;
        public const float KickerPx = 10.5f, TitlePx = 24f, BodyPx = 14f, SmallPx = 11f, QuotePx = 20f;
        public const float ButtonPx = 13f, GlyphPx = 10.5f, AvatarPx = 34f;
    }
}
