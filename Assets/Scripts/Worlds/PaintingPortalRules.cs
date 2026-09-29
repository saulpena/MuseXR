using GaussianSplatting.Runtime;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>A step on the journey that has to finish before the next one starts.</summary>
    public interface ITransitionStep
    {
        /// <summary>The visitor has stepped through: they now stand in the next world.</summary>
        bool HasCrossed { get; }
        /// <summary>Finished: the previous world is gone.</summary>
        bool IsDone { get; }
    }

    /// <summary>
    /// When a carried painting may open, and where the world inside it stands. Pure, so it is
    /// EditMode-testable; <see cref="PaintingPortal"/> is the scene shell.
    ///
    /// The painting is a Unity Quad whose picture faces its own -Z (its +Z points into the wall it
    /// hung on), so the world inside it lies along its +Z.
    /// </summary>
    public static class PaintingPortalRules
    {
        /// <summary>Degrees between the canvas's up and the world's up: tilt and roll together.</summary>
        public static float TiltDegrees(Vector3 canvasUp) => Vector3.Angle(canvasUp, Vector3.up);

        /// <summary>
        /// The horizontal direction the world inside the painting lies in, or false when the canvas
        /// is lying so flat that it has no clear facing.
        /// </summary>
        public static bool Heading(Vector3 canvasForward, out Vector3 headingFlat)
        {
            headingFlat = new Vector3(canvasForward.x, 0f, canvasForward.z);
            if (headingFlat.sqrMagnitude < 0.25f * 0.25f) { headingFlat = Vector3.forward; return false; }
            headingFlat.Normalize();
            return true;
        }

        /// <summary>The viewer sees the picture side (the quad's -Z side).</summary>
        public static bool ViewerInFront(Vector3 eye, Vector3 canvasCentre, Vector3 canvasForward) =>
            Vector3.Dot(eye - canvasCentre, canvasForward) < 0f;

        /// <summary>
        /// The painting may tear open: large enough to walk into, held roughly upright, seen from
        /// the front and within reach. Anything else and it stays a painting.
        /// </summary>
        public static bool MayOpen(Vector3 eye, Vector3 canvasCentre, Vector3 canvasForward, Vector3 canvasUp,
            float canvasHeight, float minHeight, float maxTiltDegrees, float maxDistance)
        {
            if (canvasHeight < minHeight) return false;
            if (TiltDegrees(canvasUp) > maxTiltDegrees) return false;
            if (!Heading(canvasForward, out _)) return false;
            if (!ViewerInFront(eye, canvasCentre, canvasForward)) return false;
            var flat = eye - canvasCentre; flat.y = 0f;
            return flat.magnitude <= maxDistance;
        }

        /// <summary>
        /// Where the world inside the painting stands: on the floor (y = <paramref name="floorY"/>),
        /// <paramref name="behind"/> metres past the canvas along its heading, turned to that
        /// heading and never tilted — however the painting is held, the room stays upright.
        /// </summary>
        public static void FollowPose(Vector3 canvasCentre, Vector3 canvasForward, float behind, float floorY,
            out Vector3 position, out float yawDegrees)
        {
            Heading(canvasForward, out var h);
            position = new Vector3(canvasCentre.x, floorY, canvasCentre.z) + h * behind;
            yawDegrees = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
        }
    }

    public enum TearPhase
    {
        /// <summary>A painting; it tears open while it may, and heals when it may not.</summary>
        Waiting,
        /// <summary>The visitor stepped through; the tear closes behind them.</summary>
        Closing,
        Done,
    }

    public enum TearEvent { None, Crossed, Closed }

    /// <summary>The tear in a painting: opens while allowed, heals when not, one-way once crossed.</summary>
    public sealed class TearSequence
    {
        public float TearSeconds = 3f;
        public float HealSeconds = 1.5f;
        public float CloseSeconds = 2f;
        /// <summary>How far open (0..1) the tear must be before stepping into it counts.</summary>
        public float PassableTear = 0.7f;

        public TearPhase Phase { get; private set; } = TearPhase.Waiting;
        public float Tear { get; private set; }

        public TearEvent Step(float dt, bool mayOpen, Vector3 prevEyeDoor, Vector3 eyeDoor, Vector2 half)
        {
            switch (Phase)
            {
                case TearPhase.Waiting:
                    if (Tear >= PassableTear && prevEyeDoor.z < 0f &&
                        PortalGeometry.SegmentCrossesAperture(prevEyeDoor, eyeDoor, half))
                    {
                        Phase = TearPhase.Closing;
                        return TearEvent.Crossed;
                    }
                    Tear = mayOpen
                        ? Mathf.Min(1f, Tear + dt / Mathf.Max(1e-3f, TearSeconds))
                        : Mathf.Max(0f, Tear - dt / Mathf.Max(1e-3f, HealSeconds));
                    return TearEvent.None;

                case TearPhase.Closing:
                    Tear = Mathf.Max(0f, Tear - dt / Mathf.Max(1e-3f, CloseSeconds));
                    if (Tear <= 0f)
                    {
                        Phase = TearPhase.Done;
                        return TearEvent.Closed;
                    }
                    return TearEvent.None;

                default:
                    return TearEvent.None;
            }
        }
    }
}
