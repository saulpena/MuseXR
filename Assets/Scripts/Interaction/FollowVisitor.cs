using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A question panel that follows the visitor like the masters' card (Saul, 5 Oct): held at the eye height
    /// it opened at (looking up or down never moves it), turning with the visitor once they turn past
    /// <see cref="TurnDegrees"/>, still for small head movements (<see cref="HeadSlack"/>), so it never stays behind
    /// at the object that asked it. Panels open at the same time stack upwards rather than overlap. +Z points away
    /// from the visitor, the way flat things read in this project.
    /// </summary>
    public sealed class FollowVisitor : MonoBehaviour
    {
        public const float TurnDegrees = 20f, TurnDegreesPerSecond = 110f, HeadSlack = 0.3f, StackStep = 0.34f;

        public float ahead = 1.1f, drop = 0.22f;

        static readonly List<FollowVisitor> Open = new List<FollowVisitor>();
        float _eyeY, _yaw;
        Vector3 _xz;
        bool _placed;

        public static FollowVisitor Attach(GameObject go, float ahead = 1.1f, float drop = 0.22f)
        {
            var f = go.GetComponent<FollowVisitor>();
            if (f == null) f = go.AddComponent<FollowVisitor>();   // not ??: Unity's fake null
            f.ahead = ahead; f.drop = drop;
            f.Place(true);
            return f;
        }

        void OnEnable() { if (!Open.Contains(this)) Open.Add(this); _placed = false; }
        void OnDisable() => Open.Remove(this);
        void LateUpdate() => Place(false);

        public void Place(bool snap)
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return;
            var hf = cam.forward; hf.y = 0f;
            var headYaw = hf.sqrMagnitude > 1e-6f ? Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg : _yaw;
            var flat = new Vector3(cam.position.x, 0f, cam.position.z);
            if (snap || !_placed) { _yaw = headYaw; _xz = flat; _eyeY = cam.position.y; _placed = true; }
            else if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > TurnDegrees)
                _yaw = Mathf.MoveTowardsAngle(_yaw, headYaw, TurnDegreesPerSecond * Time.deltaTime);
            var off = flat - _xz;
            if (off.magnitude > HeadSlack) _xz = flat - off.normalized * HeadSlack;
            // Height too, with the same slack: up the Grotto's slope it stayed at the eye height it was made at.
            var dy = cam.position.y - _eyeY;
            if (Mathf.Abs(dy) > HeadSlack) _eyeY = cam.position.y - Mathf.Sign(dy) * HeadSlack;
            var stack = Mathf.Max(0, Open.IndexOf(this)) * StackStep;
            var fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            var eye = new Vector3(_xz.x, _eyeY, _xz.z);
            var at = eye + fwd * ahead - Vector3.up * (drop - stack);
            transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - eye, Vector3.up));
        }
    }
    /// <summary>
    /// Stands where it is and turns about the vertical to face the visitor (+Z away from them, the way flat things
    /// read here), smoothly: the Palace's reason chips over the court (Saul, 5 Oct).
    /// </summary>
    public sealed class TurnToVisitor : MonoBehaviour
    {
        public const float DegreesPerSecond = 180f;

        public static TurnToVisitor Attach(GameObject go)
        {
            var t = go.GetComponent<TurnToVisitor>();
            if (t == null) t = go.AddComponent<TurnToVisitor>();
            t.Face(true);
            return t;
        }

        void LateUpdate() => Face(false);

        void Face(bool snap)
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return;
            var away = transform.position - cam.position; away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) return;
            var want = Quaternion.LookRotation(away.normalized, Vector3.up);
            transform.rotation = snap ? want : Quaternion.RotateTowards(transform.rotation, want, DegreesPerSecond * Time.deltaTime);
        }
    }
}
