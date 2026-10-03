using UnityEngine;

namespace MuseXR.UI
{
    /// <summary>
    /// Her 4.2 subtitle placement, as a seam with no scene in it: the panel sits 0.4 m upper-right of
    /// the speaker's head as the visitor sees it, and once the visitor's gaze has turned more than 35
    /// degrees away from that spot it lazily follows - eased every frame, never snapped - to the edge
    /// of the gaze cone on the speaker's side, at the same distance and height. Turn back and it eases
    /// home again.
    /// </summary>
    public sealed class SubtitleFollow
    {
        public const float Right = 0.4f;          // her 0.4 m, to the viewer's right of the head
        public const float Up = 0.25f;            // and above it, so the line never covers the face
        public const float FollowDegrees = 35f;   // her threshold
        public const float HoldDegrees = 28f;     // where a followed panel settles: inside the cone, not on its edge
        public const float EaseSeconds = 0.45f;   // time constant of the ease; never a snap

        public Vector3 Current { get; private set; }
        public bool Following { get; private set; }
        bool _placed;

        /// <summary>Where the panel belongs with the visitor looking at the speaker.</summary>
        public static Vector3 Home(Vector3 speakerHead, Vector3 eye)
        {
            var toHead = speakerHead - eye; toHead.y = 0f;
            var right = toHead.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, toHead.normalized) : Vector3.right;
            return speakerHead + right * Right + Vector3.up * Up;
        }

        /// <summary>Flat angle in degrees between the gaze and the direction to <paramref name="point"/>.</summary>
        public static float OffGaze(Vector3 eye, Vector3 gaze, Vector3 point)
        {
            var g = gaze; g.y = 0f; var d = point - eye; d.y = 0f;
            if (g.sqrMagnitude < 1e-6f || d.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(g, d);
        }

        /// <summary>Start a new line at its home, with no ease from wherever the last one was.</summary>
        public void Reset(Vector3 speakerHead, Vector3 eye)
        {
            Current = Home(speakerHead, eye); Following = false; _placed = true;
        }

        public Vector3 Tick(Vector3 speakerHead, Vector3 eye, Vector3 gaze, float dt)
        {
            var home = Home(speakerHead, eye);
            if (!_placed) { Current = home; _placed = true; }

            float off = OffGaze(eye, gaze, home);
            Following = off > FollowDegrees;
            var target = home;
            if (Following)
            {
                // Swing the home direction round towards the gaze until it is HoldDegrees off it, keeping
                // its distance and height: the panel waits at the edge of view on the speaker's side.
                var g = gaze; g.y = 0f; g.Normalize();
                var d = home - eye; float h = d.y; d.y = 0f; float dist = d.magnitude; d /= Mathf.Max(dist, 1e-4f);
                float side = Mathf.Sign(Vector3.Cross(g, d).y);
                var dir = Quaternion.AngleAxis(side * HoldDegrees, Vector3.up) * g;
                target = eye + dir * dist + Vector3.up * h;
            }
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / EaseSeconds);
            Current = Vector3.Lerp(Current, target, k);
            return Current;
        }
    }
}
