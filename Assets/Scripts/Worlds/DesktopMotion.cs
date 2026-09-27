using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// The maths behind <see cref="DesktopMove"/>, engine-free so it is EditMode-testable.
    /// </summary>
    public static class DesktopMotion
    {
        public const float PitchLimit = 80f;

        /// <summary>Only without a headset: in XR (device or Link) the head owns the view.</summary>
        public static bool Active(bool xrDeviceActive) => !xrDeviceActive;

        /// <summary>
        /// A horizontal step for WASD input facing <paramref name="yawDegrees"/>. Diagonals are
        /// normalised, so W+D is not 41% faster than W.
        /// </summary>
        public static Vector3 Planar(Vector2 input, float yawDegrees, float speed, float dt)
        {
            if (input.sqrMagnitude > 1f) input.Normalize();
            var yaw = Quaternion.Euler(0f, yawDegrees, 0f);
            var step = yaw * new Vector3(input.x, 0f, input.y);
            return step * (speed * dt);
        }

        /// <summary>The part of <paramref name="delta"/> that keeps pitch inside ±<see cref="PitchLimit"/>.</summary>
        public static float ClampPitchDelta(float currentPitch, float delta)
        {
            var next = Mathf.Clamp(currentPitch + delta, -PitchLimit, PitchLimit);
            return next - currentPitch;
        }
    }
}
