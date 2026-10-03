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
        /// Which side of a two-way painting the visitor is on, with a dead band: they count as inside
        /// only once the eye is <paramref name="band"/> metres past the canvas, and as back out only
        /// once it is that far in front of it. Leaning in to look is not walking in.
        /// </summary>
        /// <param name="eyeDepth">The eye's depth in canvas space: negative in front of the picture,
        /// positive inside it.</param>
        public static bool InsideAfter(bool wasInside, float eyeDepth, float band) =>
            wasInside ? eyeDepth > -band : eyeDepth > band;

        /// <summary>
        /// A controller tip is touching the centre of the canvas: within <paramref name="depth"/> of
        /// the paint, on the picture side or just through it, and within <paramref name="radius"/> of
        /// the centre (a quarter of the smaller half-size on a canvas grown past door size).
        /// </summary>
        /// <param name="tipInCanvas">The tip in canvas space, metres (unscaled): -z is the picture side.</param>
        public static bool TouchesCentre(Vector3 tipInCanvas, Vector2 half, float radius, float depth)
        {
            if (tipInCanvas.z < -depth || tipInCanvas.z > depth * 0.5f) return false;
            float r = Mathf.Max(radius, 0.25f * Mathf.Min(half.x, half.y));
            return new Vector2(tipInCanvas.x, tipInCanvas.y).magnitude <= r;
        }

        /// <summary>
        /// A two-way painting opens while the visitor is near it on either side and roughly in line
        /// with it: within <paramref name="reach"/> of its plane and no more than
        /// <paramref name="sideways"/> metres past either edge.
        /// </summary>
        public static bool NearTwoWay(Vector3 eyeInCanvas, Vector2 half, float reach, float sideways) =>
            Mathf.Abs(eyeInCanvas.z) <= reach && Mathf.Abs(eyeInCanvas.x) <= half.x + sideways;

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
