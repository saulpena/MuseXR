using System;

namespace MuseXR.Slots
{
    /// <summary>
    /// Fires once when a condition has held for long enough, and re-arms only once it stops.
    /// "Ray dwell 0.4 s on the frame" is one of these. Pure.
    /// </summary>
    public sealed class DwellTimer
    {
        public DwellTimer(float seconds) { Seconds = seconds; }

        public float Seconds { get; }
        public float Held { get; private set; }
        bool _fired;

        /// <summary>Returns true on the one frame the dwell completes.</summary>
        public bool Update(bool on, float dt)
        {
            if (!on) { Held = 0f; _fired = false; return false; }
            Held += dt > 0f ? dt : 0f;
            if (_fired || Held < Seconds) return false;
            _fired = true;
            return true;
        }
    }

    /// <summary>
    /// Inside a radius, with a margin before it counts as left, so standing on the edge does not
    /// flicker. Fires once per approach. Her plinths "replay the chapter chime as you approach";
    /// her artwork card opens "within 1.2 m of the viewing mark". Pure.
    /// </summary>
    public sealed class ApproachTrigger
    {
        public const float DefaultMargin = 0.3f;

        public ApproachTrigger(float radius, float margin = DefaultMargin) { Radius = radius; Margin = margin; }

        public float Radius { get; }
        public float Margin { get; }
        public bool Inside { get; private set; }

        /// <summary>Returns true on the frame the visitor comes inside.</summary>
        public bool Update(float distance)
        {
            if (Inside)
            {
                if (distance > Radius + Margin) Inside = false;
                return false;
            }
            if (distance > Radius) return false;
            Inside = true;
            return true;
        }
    }

    /// <summary>
    /// One artwork's attention (her UI 4.1 and record.dwell):
    ///   the card is wanted after a 0.4 s ray dwell on the frame, or on coming within 1.2 m of the
    ///   viewing mark; it is wanted again only after the ray has left and the visitor has stepped away.
    ///   Gaze on it is timed; 4 s in total makes it "seen", and the total goes into record.dwell.
    /// Pure: the scene says each frame whether the ray is on the frame, how far the visitor stands
    /// from the viewing mark and whether their gaze is on the work.
    /// </summary>
    public sealed class ArtworkAttention
    {
        public const float RayDwellSeconds = 0.4f;
        public const float ViewingMarkRadius = 1.2f;
        public const float SeenSeconds = 4f;
        /// <summary>Gaze counts within this many degrees of the work's centre.</summary>
        public const float GazeConeDegrees = 20f;

        readonly DwellTimer _ray = new DwellTimer(RayDwellSeconds);
        readonly ApproachTrigger _near = new ApproachTrigger(ViewingMarkRadius);

        public ArtworkAttention(string artworkId) { ArtworkId = artworkId ?? string.Empty; }

        public string ArtworkId { get; }
        /// <summary>Total seconds the visitor's gaze has rested on it.</summary>
        public float GazeSeconds { get; private set; }
        public bool Seen { get; private set; }

        public event Action<string> CardWanted;
        /// <summary>Raised once, when the gaze total first reaches 4 s.</summary>
        public event Action<string, float> BecameSeen;

        public void Update(bool rayOnFrame, float distanceToViewingMark, bool gazeOnWork, float dt)
        {
            var byRay = _ray.Update(rayOnFrame, dt);
            var byStep = _near.Update(distanceToViewingMark);
            if (byRay || byStep) CardWanted?.Invoke(ArtworkId);

            if (!gazeOnWork) return;
            GazeSeconds += dt > 0f ? dt : 0f;
            if (Seen || GazeSeconds < SeenSeconds) return;
            Seen = true;
            BecameSeen?.Invoke(ArtworkId, GazeSeconds);
        }

        public static bool GazeOn(float angleFromGazeDegrees) => angleFromGazeDegrees <= GazeConeDegrees;
    }

    /// <summary>
    /// Her one-time height calibration (§3.5): "seated and standing play run the same flow". The
    /// visitor's eye is measured once and the view is lifted (or lowered) to a standing eye, so
    /// everything placed for standing reach — grabbables at 0.8-1.3 m, panels at 1.5 m — fits a
    /// seated visitor too. Pure.
    /// </summary>
    public static class HeightCalibration
    {
        public const float StandingEye = 1.6f;
        public const float MaxLift = 0.7f, MaxLower = 0.3f;
        /// <summary>A reading under this cannot be an eye (headset on a desk, tracking not started).</summary>
        public const float MinPlausibleEye = 0.6f;

        /// <summary>Metres to add to the camera's height. 0 for an implausible reading.</summary>
        public static float Offset(float measuredEye)
        {
            if (float.IsNaN(measuredEye) || measuredEye < MinPlausibleEye || measuredEye > 2.3f) return 0f;
            var d = StandingEye - measuredEye;
            return d > MaxLift ? MaxLift : d < -MaxLower ? -MaxLower : d;
        }
    }
}
