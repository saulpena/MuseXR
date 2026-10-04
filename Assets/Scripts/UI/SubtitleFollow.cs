using UnityEngine;

namespace MuseXR.UI
{
    /// <summary>
    /// Her 4.2 subtitle placement, as a seam with no scene in it: the panel sits 0.4 m upper-right of
    /// the speaker's head as the visitor sees it, and stays put in the world. Only when the panel
    /// itself is more than 35 degrees off the gaze does it lazily follow - eased, never snapped - to
    /// one spot chosen at that moment (home if home is in view, else the edge of the gaze cone on the
    /// speaker's side), and stops there.
    ///
    /// The target is locked when the move starts. Measured in Saul's headset test (2 Oct 2026): a
    /// target recomputed from the gaze every frame slides away while the visitor turns to read it,
    /// then slides back - "it moves so much it's hard to read; it makes me sick".
    /// </summary>
    public sealed class SubtitleFollow
    {
        public const float Right = 0.4f;          // her 0.4 m, to the viewer's right of the head
        public const float Up = 0.25f;            // and above it, so the line never covers the face
        public const float FollowDegrees = 35f;   // her threshold
        public const float HoldDegrees = 28f;     // where a followed panel settles: inside the cone, not on its edge
        public const float EaseSeconds = 0.45f;   // time constant of the ease; never a snap

        /// <summary>Extra height above <see cref="Up"/>: a tall panel lifts by half its height so its
        /// lower edge clears the head rather than its centre.</summary>
        public float ExtraUp;

        public Vector3 Current { get; private set; }
        /// <summary>True while the panel is easing to its locked target; false while it stands still.</summary>
        public bool Following { get; private set; }
        /// <summary>Within this of its target the move is over and the panel is still again.</summary>
        public const float ArriveMetres = 0.01f;
        bool _placed;
        Vector3 _target;

        /// <summary>Where the panel belongs with the visitor looking at the speaker.</summary>
        public static Vector3 Home(Vector3 speakerHead, Vector3 eye) => Home(speakerHead, eye, 0f);

        public static Vector3 Home(Vector3 speakerHead, Vector3 eye, float extraUp)
        {
            var toHead = speakerHead - eye; toHead.y = 0f;
            var right = toHead.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, toHead.normalized) : Vector3.right;
            return speakerHead + right * Right + Vector3.up * (Up + extraUp);
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
            Current = Home(speakerHead, eye, ExtraUp); Following = false; _placed = true;
        }

        /// <summary>
        /// Start a new line where the visitor can read it: at home if home is in view, otherwise
        /// straight at the edge of the gaze cone on the speaker's side - appearing there, not easing in
        /// from behind the visitor.
        /// </summary>
        public void Reset(Vector3 speakerHead, Vector3 eye, Vector3 gaze)
        {
            Reset(speakerHead, eye);
            if (OffGaze(eye, gaze, Current) > FollowDegrees) Current = ConeEdge(Current, eye, gaze);
        }

        public Vector3 Tick(Vector3 speakerHead, Vector3 eye, Vector3 gaze, float dt)
        {
            var home = Home(speakerHead, eye, ExtraUp);
            if (!_placed) { Current = home; _placed = true; }

            // A panel the visitor can see never moves. One that has left the cone picks a spot ONCE.
            if (!Following && OffGaze(eye, gaze, Current) > FollowDegrees)
            {
                Following = true;
                _target = OffGaze(eye, gaze, home) <= HoldDegrees ? home : ConeEdge(home, eye, gaze);
            }
            if (!Following) return Current;

            float k = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / EaseSeconds);
            Current = Vector3.Lerp(Current, _target, k);
            if (Vector3.Distance(Current, _target) < ArriveMetres) { Current = _target; Following = false; }
            return Current;
        }

        /// <summary>Away from the speaker, the panel's lower edge sits this far above eye height.</summary>
        public const float EdgeAboveEye = 0.05f;
        const float HeadClearance = 0.22f;   // SubtitleRig's: ExtraUp = halfHeight + this - Up

        /// <summary>
        /// Home swung round towards the gaze until it is HoldDegrees off it, at the same distance, with
        /// its lower edge just above eye height. At the speaker's head height a large panel ran off the
        /// top of the view; centred at eye height it sat behind whatever the visitor was handling
        /// (blind review, Palace, 3 Oct 2026: the reason chips hid its disclaimer and Next).
        /// </summary>
        Vector3 ConeEdge(Vector3 home, Vector3 eye, Vector3 gaze)
        {
            var g = gaze; g.y = 0f; g.Normalize();
            var d = home - eye; d.y = 0f; float dist = d.magnitude; d /= Mathf.Max(dist, 1e-4f);
            float side = Mathf.Sign(Vector3.Cross(g, d).y);
            var dir = Quaternion.AngleAxis(side * HoldDegrees, Vector3.up) * g;
            float half = Mathf.Max(0f, ExtraUp + Up - HeadClearance);
            return eye + dir * dist + Vector3.up * (half + EdgeAboveEye);
        }
    }
}
