using UnityEngine;

namespace MuseXR.Worlds
{
    public enum RevealPhase
    {
        /// <summary>Nothing yet; the next world is loaded but not drawn.</summary>
        Waiting,
        /// <summary>Night falls on the world being left.</summary>
        Dusk,
        /// <summary>The front rises; the worlds trade places across it.</summary>
        Rising,
        /// <summary>The new world's dawn light settles to day.</summary>
        Settling,
        Done,
    }

    public enum RevealEvent { None, StartedDusk, StartedRising, FrontPassed, Finished }

    /// <summary>
    /// A world-to-world transition with no door (Skylar: "the reflection of the stars gradually
    /// becomes water ... night becomes morning"). The visitor stands in the room and looks up; night
    /// falls on it, then the next world rises out of the floor on a ragged front while the old one
    /// dissolves above it, and the new world's dawn settles into day. Pure — flags in, amounts out —
    /// so it is EditMode-testable; <see cref="SplatRevealTransition"/> is the scene shell.
    /// </summary>
    public sealed class RevealSequence
    {
        public float DuskSeconds = 4f;
        public float RiseSeconds = 12f;
        public float SettleSeconds = 4f;
        /// <summary>How long the visitor must stand in place looking up before night falls.</summary>
        public float LookSeconds = 0.6f;
        /// <summary>Front height at the start and end of the rise, metres.</summary>
        public float FrontFrom = -0.5f;
        public float FrontTo = 35f;

        public RevealPhase Phase { get; private set; } = RevealPhase.Waiting;
        /// <summary>0 day, 1 full night on the world being left.</summary>
        public float Night { get; private set; }
        /// <summary>1 full dawn tint on the arriving world, 0 plain day.</summary>
        public float Dawn { get; private set; } = 1f;
        /// <summary>0..1 through the rise.</summary>
        public float Rise { get; private set; }
        /// <summary>0 the old world's music, 1 the new one's. Never goes back.</summary>
        public float MusicBlend { get; private set; }

        /// <summary>
        /// The front's height. Slow at the floor, where the eye is and where the water begins, and
        /// quicker as it climbs past the ceiling into the new world's sky.
        /// </summary>
        public float Front => Mathf.Lerp(FrontFrom, FrontTo, Rise * Rise);

        bool _requested;
        float _lookedFor;

        public void Request() => _requested = true;

        public RevealEvent Step(float dt, bool inPlace, bool lookingUp)
        {
            switch (Phase)
            {
                case RevealPhase.Waiting:
                    _lookedFor = inPlace && lookingUp ? _lookedFor + dt : 0f;
                    if (_requested || _lookedFor >= LookSeconds)
                    {
                        Phase = RevealPhase.Dusk;
                        return RevealEvent.StartedDusk;
                    }
                    return RevealEvent.None;

                case RevealPhase.Dusk:
                    Night = Mathf.Min(1f, Night + dt / Mathf.Max(1e-3f, DuskSeconds));
                    if (Night >= 1f)
                    {
                        Phase = RevealPhase.Rising;
                        return RevealEvent.StartedRising;
                    }
                    return RevealEvent.None;

                case RevealPhase.Rising:
                    Rise = Mathf.Min(1f, Rise + dt / Mathf.Max(1e-3f, RiseSeconds));
                    MusicBlend = Mathf.Max(MusicBlend, Rise);
                    if (Rise >= 1f)
                    {
                        Phase = RevealPhase.Settling;
                        return RevealEvent.FrontPassed;
                    }
                    return RevealEvent.None;

                case RevealPhase.Settling:
                    Dawn = Mathf.Max(0f, Dawn - dt / Mathf.Max(1e-3f, SettleSeconds));
                    if (Dawn <= 0f)
                    {
                        Phase = RevealPhase.Done;
                        return RevealEvent.Finished;
                    }
                    return RevealEvent.None;

                default:
                    return RevealEvent.None;
            }
        }
    }
}
