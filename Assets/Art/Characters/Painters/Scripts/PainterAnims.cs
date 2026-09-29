using System;

namespace Painters
{
    /// <summary>The Painter.controller parameters one character is driven by.</summary>
    public struct PainterAnimState
    {
        public float Speed;
        public bool Talking, Listening;
        public int TalkStyle, WalkStyle;
    }

    /// <summary>
    /// What each control does to <see cref="PainterAnimState"/>, and the names of the talk and walk styles in
    /// the order Painter.controller numbers them. Engine-free so it can be tested without Play mode.
    /// </summary>
    public static class PainterAnims
    {
        public static readonly string[] TalkNames =
            { "Talk", "Chat", "Passionate", "Hand on hip", "Left hand raised", "Angry", "Hands open", "Right hand open" };

        public static readonly string[] WalkNames = { "Walk", "Casual", "Thoughtful", "Formal", "Generated walk" };

        /// <summary>Speed that makes the controller walk (it walks above 0.1).</summary>
        public const float WalkSpeed = 1f;

        public static PainterAnimState Idle(PainterAnimState s)
        {
            s.Speed = 0f; s.Talking = false; s.Listening = false;
            return s;
        }

        public static PainterAnimState Talk(PainterAnimState s, int style)
        {
            if (style < 0 || style >= TalkNames.Length) throw new ArgumentOutOfRangeException(nameof(style));
            s.Speed = 0f; s.Talking = true; s.Listening = false; s.TalkStyle = style;
            return s;
        }

        public static PainterAnimState Walk(PainterAnimState s, int style)
        {
            if (style < 0 || style >= WalkNames.Length) throw new ArgumentOutOfRangeException(nameof(style));
            s.Speed = WalkSpeed; s.Talking = false; s.Listening = false; s.WalkStyle = style;
            return s;
        }

        public static PainterAnimState Listen(PainterAnimState s)
        {
            s.Speed = 0f; s.Talking = false; s.Listening = true;
            return s;
        }

        /// <summary>A talk style other than <paramref name="current"/>, so a shuffle always changes something.</summary>
        public static int OtherTalk(int current, Random rng)
        {
            int pick = rng.Next(TalkNames.Length - 1);
            return pick >= current ? pick + 1 : pick;
        }

        public static string Describe(PainterAnimState s)
        {
            if (s.Speed > 0.1f) return "walking: " + WalkNames[s.WalkStyle];
            if (s.Talking) return "talking: " + TalkNames[s.TalkStyle];
            return s.Listening ? "listening" : "idle";
        }
    }
}
