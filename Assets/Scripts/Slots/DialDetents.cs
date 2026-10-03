using System;

namespace MuseXR.Slots
{
    /// <summary>
    /// Her Monet time ring as a hand-turned dial (chapter D, frames 1-2): "a grabbable dial with three
    /// detents", Mist left, Afternoon top, Dusk right; "each detent gives a haptic tick and a water
    /// sound". The hand's wrist twist turns it; it stops at the end detents; crossing into a detent's
    /// zone selects it. A detent only changes once the dial is a few degrees past the midpoint, so a
    /// hand trembling on a boundary does not tick back and forth. On release it settles on the detent.
    /// Pure: degrees in, detent out.
    /// </summary>
    public sealed class DialDetents
    {
        public const int Count = 3;
        /// <summary>Angle between detents. Matches TimeRingDriver.detentDegrees, so its ring mesh agrees.</summary>
        public const float Spacing = 60f;
        /// <summary>The dial can be pushed this far past the end detents, then stops.</summary>
        public const float Overtravel = 12f;
        /// <summary>Degrees past the midpoint before a detent changes.</summary>
        public const float Hysteresis = 6f;

        float _grabAngle, _lastTwist, _twisted;

        public DialDetents(int start = 1)
        {
            Detent = Clamp(start, 0, Count - 1);
            Angle = AngleOf(Detent);
        }

        /// <summary>0 = Mist (left, -60°), 1 = Afternoon (top, 0°), 2 = Dusk (right, +60°).</summary>
        public int Detent { get; private set; }

        /// <summary>The dial's current turn in degrees, 0 = top. Positive is clockwise as the visitor sees it.</summary>
        public float Angle { get; private set; }

        public bool Held { get; private set; }

        public static float AngleOf(int detent) => (detent - (Count - 1) * 0.5f) * Spacing;

        public void Grab(float wristTwistDegrees)
        {
            Held = true;
            _grabAngle = Angle;
            _lastTwist = wristTwistDegrees;
            _twisted = 0f;
        }

        /// <summary>
        /// Feed the wrist's twist (degrees, any reference; only the change since Grab counts, accumulated per call).
        /// Returns the new detent if this call crossed into one, otherwise -1.
        /// </summary>
        public int Turn(float wristTwistDegrees)
        {
            if (!Held) return -1;
            var lo = AngleOf(0) - Overtravel;
            var hi = AngleOf(Count - 1) + Overtravel;
            // Accumulated frame to frame, so a long twist never wraps at 180.
            _twisted += DeltaAngle(_lastTwist, wristTwistDegrees);
            _lastTwist = wristTwistDegrees;
            Angle = Math.Max(lo, Math.Min(hi, _grabAngle + _twisted));
            _twisted = Angle - _grabAngle;   // at the stop, turning back responds at once

            var next = Detent;
            while (next < Count - 1 && Angle > (AngleOf(next) + AngleOf(next + 1)) * 0.5f + Hysteresis) next++;
            while (next > 0 && Angle < (AngleOf(next) + AngleOf(next - 1)) * 0.5f - Hysteresis) next--;
            if (next == Detent) return -1;
            Detent = next;
            return next;
        }

        /// <summary>Let go: the dial settles on its detent.</summary>
        public void Release()
        {
            Held = false;
            Angle = AngleOf(Detent);
        }

        /// <summary>Shortest signed difference b - a in degrees, -180..180, so a twist across ±180 does not jump.</summary>
        public static float DeltaAngle(float a, float b)
        {
            var d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
