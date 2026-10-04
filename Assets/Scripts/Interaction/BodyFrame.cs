using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Where the visitor's body faces, as opposed to their head. There is no torso tracking, so the
    /// body follows the head lazily: a glance (under <see cref="DeadZoneDegrees"/>) leaves it where it
    /// is, a held turn brings it round at <see cref="TurnDegreesPerSecond"/>, and walking points it
    /// along the walk. The waist panel and the companions' crowd both hang off this, so neither moves
    /// when the visitor only looks around.
    /// </summary>
    public sealed class BodyFrame : MonoBehaviour
    {
        public const float DeadZoneDegrees = 40f, TurnDegreesPerSecond = 150f, WalkingSpeed = 0.35f;

        public Transform Head { get; private set; }
        /// <summary>The body's facing, flat.</summary>
        public Vector3 Forward { get; private set; } = Vector3.forward;
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        /// <summary>The floor under the visitor, flat.</summary>
        public Vector3 Feet { get; private set; }
        /// <summary>How fast the visitor is walking, flat, metres per second.</summary>
        public Vector3 Velocity { get; private set; }

        float _yaw;
        Vector3 _lastFeet;
        bool _started;
        int _frame = -1;

        static BodyFrame _instance;

        /// <summary>The body frame of the main camera's rig, made on first use.</summary>
        public static BodyFrame Get()
        {
            if (_instance != null) return _instance;
            var cam = Camera.main;
            if (cam == null) return null;
            var origin = cam.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            var host = origin != null ? origin.gameObject : cam.gameObject;
            _instance = host.GetComponent<BodyFrame>();
            if (_instance == null) _instance = host.AddComponent<BodyFrame>();   // not ??: Unity's fake null
            _instance.Head = cam.transform;
            _instance.Step(0f);
            return _instance;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Update() => Step(Time.deltaTime);

        /// <summary>Advance once per frame (safe to call more than once in a frame).</summary>
        public void Step(float dt)
        {
            if (_frame == Time.frameCount && _started) return;
            _frame = Time.frameCount;
            if (Head == null) { var cam = Camera.main; if (cam == null) return; Head = cam.transform; }
            var origin = Head.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            var floorY = origin != null ? origin.transform.position.y : Head.position.y - 1.6f;
            var feet = new Vector3(Head.position.x, floorY, Head.position.z);
            var headFwd = Head.forward; headFwd.y = 0f;
            var headYaw = headFwd.sqrMagnitude > 1e-6f ? Mathf.Atan2(headFwd.x, headFwd.z) * Mathf.Rad2Deg : _yaw;
            if (!_started) { _yaw = headYaw; _lastFeet = feet; _started = true; }

            var v = dt > 1e-5f ? (feet - _lastFeet) / dt : Vector3.zero;
            v.y = 0f;
            // A teleport or snap turn is not a walk: ignore velocities no person reaches.
            Velocity = v.magnitude < 4f ? Vector3.Lerp(Velocity, v, Mathf.Clamp01(dt * 6f)) : Vector3.zero;
            _lastFeet = feet;
            Feet = feet;

            float target = _yaw;
            var walkYaw = Mathf.Atan2(Velocity.x, Velocity.z) * Mathf.Rad2Deg;
            // Walking points the body along the walk - but not a step backwards or sideways, which a body
            // does without turning round (it swung the crowd in front of a visitor stepping back).
            if (Velocity.magnitude > WalkingSpeed && Mathf.Abs(Mathf.DeltaAngle(headYaw, walkYaw)) < 60f) target = walkYaw;
            else if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > DeadZoneDegrees) target = headYaw;
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, TurnDegreesPerSecond * dt);
            // A snap turn turns the body with it at once.
            if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > 100f) _yaw = headYaw;
            Forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        }

        /// <summary>The flat direction <paramref name="bearing"/> degrees from the body's facing (+ right).</summary>
        public Vector3 Bearing(float bearing) => Quaternion.Euler(0f, bearing, 0f) * Forward;
    }
}
