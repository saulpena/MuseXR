using System.Collections;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Test tooling: walks the visitor as smooth locomotion does - through the rig's CharacterController at
    /// walking pace, turning smoothly toward where it goes - so a scripted run of the journey crosses gates and
    /// walks up to pieces the way a visitor with a thumbstick would, never jumping. Added in Play by a test.
    /// </summary>
    public sealed class JourneyWalker : MonoBehaviour
    {
        public const float WalkSpeed = 1.2f, TurnSpeed = 90f;
        public bool Busy { get; private set; }
        public string Last { get; private set; } = "";

        Unity.XR.CoreUtils.XROrigin _rig;
        CharacterController _body;

        Transform Head => Camera.main != null ? Camera.main.transform : null;

        void Awake()
        {
            _rig = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            _body = _rig != null ? _rig.GetComponent<CharacterController>() : null;
        }

        /// <summary>Turn to face <paramref name="target"/>, then walk until the eye is within <paramref name="stopAt"/> m of it (flat).</summary>
        public void WalkTo(Vector3 target, float stopAt = 0.3f, float speed = WalkSpeed)
        {
            StopAllCoroutines();
            StartCoroutine(Run(target, stopAt, speed));
        }

        /// <summary>Turn on the spot to face <paramref name="target"/>.</summary>
        public void Face(Vector3 target)
        {
            StopAllCoroutines();
            StartCoroutine(TurnTo(target, true));
        }

        IEnumerator Run(Vector3 target, float stopAt, float speed)
        {
            Busy = true; Last = "";
            yield return TurnTo(target, false);
            var stuck = 0f;
            for (var guard = 0; guard < 4000; guard++)
            {
                var head = Head; if (head == null) break;
                var to = target - head.position; to.y = 0f;
                if (to.magnitude <= stopAt) break;
                // keep facing the way we walk (a visitor steers with the stick and looks ahead)
                var yaw = Vector3.SignedAngle(Flat(head.forward), to, Vector3.up);
                _rig.RotateAroundCameraUsingOriginUp(Mathf.Clamp(yaw, -TurnSpeed * Time.deltaTime, TurnSpeed * Time.deltaTime));
                var step = to.normalized * Mathf.Min(to.magnitude, speed * Time.deltaTime);
                var before = _rig.transform.position;
                if (_body != null && _body.enabled) _body.Move(step + Vector3.down * 0.02f);
                else _rig.transform.position += step;
                stuck = (_rig.transform.position - before).magnitude < step.magnitude * 0.2f ? stuck + Time.deltaTime : 0f;
                if (stuck > 1.5f) { Last = "blocked at " + head.position.ToString("F2") + " walking to " + target.ToString("F2"); Debug.Log("[Walker] " + Last); break; }
                yield return null;
            }
            if (Last.StartsWith("blocked") == false) Last = "arrived near " + target.ToString("F2");
            Busy = false;
        }

        IEnumerator TurnTo(Vector3 target, bool alone)
        {
            if (alone) Busy = true;
            for (var guard = 0; guard < 2000; guard++)
            {
                var head = Head; if (head == null) break;
                var to = target - head.position; to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) break;
                var yaw = Vector3.SignedAngle(Flat(head.forward), to, Vector3.up);
                if (Mathf.Abs(yaw) < 1f) break;
                _rig.RotateAroundCameraUsingOriginUp(Mathf.Clamp(yaw, -TurnSpeed * Time.deltaTime, TurnSpeed * Time.deltaTime));
                yield return null;
            }
            if (alone) Busy = false;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
    }
}
