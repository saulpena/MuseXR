using System;

namespace MuseXR.Slots
{
    /// <summary>
    /// How a companion walking with the visitor moves round them, as a bearing (world yaw, degrees) and a
    /// radius about the visitor's feet. Saul, 4 Oct 2026: companions follow at the sides and the edge of
    /// the view and are never in the way. Measured before this (CompanionClearanceProbe, the Gate): 13
    /// faults - in front of the eye for 2-3.5 s after turns, snaps, glances, teleports and a step back,
    /// and three inside the visitor after a teleport and a turn. So, as rules rather than steering:
    /// <list type="bullet">
    /// <item>a companion's place is never inside the cone round the head's gaze (<see cref="GazeHalfAngle"/>);</item>
    /// <item>one the gaze sweeps onto slips out of the cone at once, to the side it is already on;</item>
    /// <item>it goes round the visitor, never across the front: between sides it passes behind;</item>
    /// <item>it never comes nearer than <see cref="MinRadius"/>.</item>
    /// </list>
    /// Pure: no scene.
    /// </summary>
    public static class CrowdOrbit
    {
        /// <summary>No companion's centre within this of the gaze (with a body's width it clears the 25 degree
        /// in-the-way cone of <see cref="CompanionClearance"/> from 1.4 m out).</summary>
        public const float GazeHalfAngle = 25f;   // the walking path only (Saul, 4 Oct: at 42 about the head they were never in view)
        /// <summary>Nearer than this a companion fills the view.</summary>
        public const float MinRadius = 1.4f;
        /// <summary>How fast a companion slips out of the gaze: a quick step aside, degrees per second.</summary>
        public const float EscapeDegreesPerSecond = 240f;

        /// <summary>Signed shortest turn from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
        public static float Delta(float from, float to)
        {
            var d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        public static bool InGaze(float yaw, float gaze) => Math.Abs(Delta(gaze, yaw)) < GazeHalfAngle;

        /// <summary>
        /// <paramref name="place"/> moved out of the gaze cone if it lies in it, to the side it lies on (or,
        /// dead ahead, the side of <paramref name="current"/>).
        /// </summary>
        public static float AvoidGaze(float place, float gaze, float current)
        {
            var rel = Delta(gaze, place);
            if (Math.Abs(rel) >= GazeHalfAngle) return place;
            var side = Math.Abs(rel) > 1f ? Math.Sign(rel) : SideOf(current, gaze);
            return gaze + side * (GazeHalfAngle + 3f);
        }

        static int SideOf(float yaw, float gaze)
        {
            var r = Delta(gaze, yaw);
            return r >= 0f ? 1 : -1;
        }

        /// <summary>
        /// The next bearing from <paramref name="current"/> toward <paramref name="target"/> (already outside the
        /// cone), moving at most <paramref name="maxDegrees"/>, or <paramref name="escapeDegrees"/> while inside
        /// the gaze cone. Never through the cone: between sides the way round is behind the visitor.
        /// </summary>
        public static float Step(float current, float target, float gaze, float maxDegrees, float escapeDegrees)
        {
            var rc = Delta(gaze, current);
            if (Math.Abs(rc) < GazeHalfAngle)
            {
                var side = SideOf(current, gaze);
                var edge = side * (GazeHalfAngle + 3f);
                return gaze + Toward(rc, edge, escapeDegrees);
            }
            var rt = Delta(gaze, target);
            // Same side of the gaze: straight round (the short way between them never crosses the front).
            // Opposite sides: unwrap the target past 180 on this side, so the way is round the back.
            if (Math.Sign(rt) != Math.Sign(rc)) rt += rc > 0f ? 360f : -360f;
            return gaze + Toward(rc, rt, maxDegrees);
        }

        static float Toward(float from, float to, float max)
        {
            var d = to - from;
            return Math.Abs(d) <= max ? to : from + Math.Sign(d) * max;
        }

        /// <summary>The next radius toward <paramref name="target"/>, never inside <see cref="MinRadius"/>.</summary>
        public static float StepRadius(float current, float target, float maxMetres) =>
            Math.Max(MinRadius, Toward(current, Math.Max(MinRadius, target), maxMetres));
    }
}
