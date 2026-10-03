using System;

namespace MuseXR.Slots
{
    /// <summary>
    /// Her held-object rotation: "the stick turns it in 15° steps and the angle is shown" (Palace,
    /// frame 2). One flick is one step. The stick has to come back toward the centre before the
    /// next step counts, so holding it over does not spin the object, and a stick resting slightly
    /// off-centre does nothing. Pure.
    /// </summary>
    public sealed class StickStepper
    {
        public const float StepDegrees = 15f;
        /// <summary>Deflection that fires a step, and the one it must fall under to re-arm.</summary>
        public const float Press = 0.65f, Rearm = 0.35f;

        bool _armed = true;

        /// <summary>Total turn applied so far, degrees. Right is clockwise seen from above (+yaw in Unity).</summary>
        public float Degrees { get; private set; }

        /// <summary>Feed the stick's x. Returns -1, 0 or +1: the step taken this frame.</summary>
        public int Update(float x)
        {
            if (float.IsNaN(x)) return 0;
            var a = Math.Abs(x);
            if (!_armed)
            {
                if (a < Rearm) _armed = true;
                return 0;
            }
            if (a < Press) return 0;
            _armed = false;
            var step = x > 0f ? 1 : -1;
            Degrees += step * StepDegrees;
            return step;
        }

        public void Reset()
        {
            Degrees = 0f;
            _armed = true;
        }

        /// <summary>An angle as she shows it: whole degrees in 0..359.</summary>
        public static int Display(float degrees)
        {
            var d = (int)Math.Round(degrees) % 360;
            return d < 0 ? d + 360 : d;
        }
    }
}
