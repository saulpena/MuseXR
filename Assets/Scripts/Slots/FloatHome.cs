using System;

namespace MuseXR.Slots
{
    /// <summary>
    /// Her rule: "released away from a slot, the object floats back to its plinth; nothing falls to
    /// the floor". This is the timing of that glide: how long it takes for a given distance, and at
    /// each moment how far along it is and how high it rides above the straight line, so the piece
    /// lifts over the plinth's edge instead of sliding through it.
    /// </summary>
    public static class FloatHome
    {
        public const float MinSeconds = 0.5f;
        public const float MaxSeconds = 1.4f;
        /// <summary>Seconds added per metre of travel.</summary>
        public const float SecondsPerMetre = 0.6f;
        /// <summary>Arc height as a share of the distance, capped.</summary>
        public const float LiftShare = 0.25f, MaxLift = 0.15f;
        /// <summary>The snap into a slot: short and decisive.</summary>
        public const float SnapSeconds = 0.12f;

        public static float Duration(float distance) =>
            Clamp(MinSeconds + Math.Max(0f, distance) * SecondsPerMetre, MinSeconds, MaxSeconds);

        /// <summary>Progress 0..1 along the line: smoothstep, so it leaves and lands softly.</summary>
        public static float Along(float elapsed, float duration)
        {
            var t = Clamp(duration > 0f ? elapsed / duration : 1f, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Metres above the straight line: 0 at both ends, highest midway.</summary>
        public static float Lift(float elapsed, float duration, float distance)
        {
            var t = Clamp(duration > 0f ? elapsed / duration : 1f, 0f, 1f);
            var peak = Math.Min(MaxLift, Math.Max(0f, distance) * LiftShare);
            return peak * 4f * t * (1f - t);
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
