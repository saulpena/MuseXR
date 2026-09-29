using GaussianSplatting.Runtime;
using UnityEngine;

namespace MuseXR.Worlds
{
    public enum PortalPhase
    {
        /// <summary>No door yet. The next world is loaded but not drawn.</summary>
        Waiting,
        /// <summary>The door is materialising, shut.</summary>
        Appearing,
        Opening,
        Open,
        /// <summary>The visitor has walked through; the door shuts behind them.</summary>
        Closing,
        /// <summary>Shut and gone. The previous world can be unloaded.</summary>
        Done,
    }

    public enum PortalEvent { None, StartedAppearing, StartedOpening, Crossed, Closed }

    /// <summary>
    /// The one-way door between two splat worlds, as a state machine. The door is not there at
    /// first: it materialises when the visitor comes near and looks toward where it will be (or is
    /// asked to), then opens while the next world seeps out around it, lets the visitor through
    /// once it is open far enough, and shuts behind them for good. Pure — positions and a gaze
    /// flag in, phase and amounts out — so it is EditMode-testable; <see cref="SplatPortalDoor"/>
    /// is the scene shell around it.
    ///
    /// Positions are in DOOR space (<see cref="PortalGeometry"/>): the visitor starts at z &lt; 0.
    /// </summary>
    public sealed class PortalSequence
    {
        public float AppearSeconds = 2.5f;
        public float OpenSeconds = 3.5f;
        public float CloseSeconds = 2.5f;
        /// <summary>How long the next world takes to seep fully out round the door once it opens.</summary>
        public float SeepSeconds = 6f;
        /// <summary>The door appears when the eye is this close to its centre, in front of it, looking at it.</summary>
        public float TriggerDistance = 7f;
        /// <summary>How long the visitor must keep looking before the door starts to appear.</summary>
        public float LookSeconds = 0.4f;
        /// <summary>How open (0..1) the door must be before walking into it counts as going through.</summary>
        public float PassableOpening = 0.6f;

        public PortalPhase Phase { get; private set; } = PortalPhase.Waiting;
        /// <summary>0 absent, 1 fully materialised. Linear in time; ease it for display.</summary>
        public float Appear { get; private set; }
        /// <summary>0 shut, 1 fully open. Linear in time; ease it for display.</summary>
        public float Opening { get; private set; }
        /// <summary>0 nothing, 1 the next world's full seep round the door.</summary>
        public float Seep { get; private set; }
        /// <summary>0 all the current world's music, 1 all the next world's. Never goes back.</summary>
        public float MusicBlend { get; private set; }

        bool _openRequested;
        float _lookedFor;

        public void RequestOpen() => _openRequested = true;

        /// <summary>
        /// Advance by <paramref name="dt"/> seconds as the eye moves from prev to now.
        /// <paramref name="looking"/>: the visitor is facing the door.
        /// </summary>
        public PortalEvent Step(float dt, Vector3 prevEyeDoor, Vector3 eyeDoor, Vector2 half, bool looking = true)
        {
            switch (Phase)
            {
                case PortalPhase.Waiting:
                    _lookedFor = IsNear(eyeDoor) && looking ? _lookedFor + dt : 0f;
                    if (_openRequested || (_lookedFor >= LookSeconds && IsNear(eyeDoor)))
                    {
                        Phase = PortalPhase.Appearing;
                        return PortalEvent.StartedAppearing;
                    }
                    return PortalEvent.None;

                case PortalPhase.Appearing:
                    Appear = Mathf.Min(1f, Appear + dt / Mathf.Max(1e-3f, AppearSeconds));
                    if (Appear >= 1f)
                    {
                        Phase = PortalPhase.Opening;
                        return PortalEvent.StartedOpening;
                    }
                    return PortalEvent.None;

                case PortalPhase.Opening:
                case PortalPhase.Open:
                    if (Opening >= PassableOpening && prevEyeDoor.z < 0f &&
                        PortalGeometry.SegmentCrossesAperture(prevEyeDoor, eyeDoor, half))
                    {
                        Phase = PortalPhase.Closing;
                        MusicBlend = 1f;
                        return PortalEvent.Crossed;
                    }
                    if (Phase == PortalPhase.Opening)
                    {
                        Opening = Mathf.Min(1f, Opening + dt / Mathf.Max(1e-3f, OpenSeconds));
                        if (Opening >= 1f) Phase = PortalPhase.Open;
                    }
                    Seep = Mathf.Min(1f, Seep + dt / Mathf.Max(1e-3f, SeepSeconds));
                    MusicBlend = Mathf.Max(MusicBlend, Opening);
                    return PortalEvent.None;

                case PortalPhase.Closing:
                    Opening = Mathf.Max(0f, Opening - dt / Mathf.Max(1e-3f, CloseSeconds));
                    Seep = Mathf.Min(Seep, Opening);
                    if (Opening <= 0f)
                    {
                        Phase = PortalPhase.Done;
                        return PortalEvent.Closed;
                    }
                    return PortalEvent.None;

                default:
                    return PortalEvent.None;
            }
        }

        bool IsNear(Vector3 eyeDoor) =>
            eyeDoor.z < 0f && new Vector2(eyeDoor.x, eyeDoor.z).magnitude <= TriggerDistance;

        /// <summary>
        /// True if a head looking along <paramref name="forwardDoor"/> (door space) is facing the
        /// door's centre within <paramref name="maxDegrees"/>, ignoring pitch.
        /// </summary>
        public static bool IsLookingAt(Vector3 eyeDoor, Vector3 forwardDoor, float maxDegrees)
        {
            var toDoor = new Vector2(-eyeDoor.x, -eyeDoor.z);
            var fwd = new Vector2(forwardDoor.x, forwardDoor.z);
            if (toDoor.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-6f) return false;
            return Vector2.Angle(toDoor, fwd) <= maxDegrees;
        }

        /// <summary>Ease-in-out, so things start and stop gently.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
