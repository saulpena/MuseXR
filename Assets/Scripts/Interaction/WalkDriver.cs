using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Walks the XR rig along a path at walking pace, the way smooth locomotion moves it: the rig's
    /// CharacterController moves a little every frame, with gravity and collisions, and the eye turns to
    /// the way it is going. For testing a journey without a headset: walking through a portal door is
    /// then a real crossing, frame by frame, not a jump.
    /// </summary>
    public sealed class WalkDriver : MonoBehaviour
    {
        public const float Speed = 1.4f, EyeHeight = 1.6f;

        readonly Queue<Vector3> _path = new Queue<Vector3>();
        CharacterController _body;
        Transform _eye;
        float _fall;

        public bool Walking => _path.Count > 0;

        /// <summary>Walk the rig through <paramref name="points"/> (floor positions), in order.</summary>
        public static WalkDriver Walk(params Vector3[] points)
        {
            var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) { Debug.LogError("[Walk] no XR Origin"); return null; }
            var w = origin.GetComponent<WalkDriver>();
            if (w == null) w = origin.gameObject.AddComponent<WalkDriver>();   // not ??: Unity's fake null defeats it
            w._body = origin.GetComponent<CharacterController>();
            w._eye = origin.Camera != null ? origin.Camera.transform : null;
            // No headset driving the eye: hold it at standing height over the body.
            if (w._eye != null)
                foreach (var b in w._eye.GetComponents<Behaviour>())
                    if (b.GetType().Name.Contains("TrackedPoseDriver")) b.enabled = false;
            w._path.Clear();
            foreach (var p in points) w._path.Enqueue(p);
            return w;
        }

        void Update()
        {
            if (_body == null || _path.Count == 0) return;
            var here = transform.position;
            var to = _path.Peek() - here; to.y = 0f;
            var step = Speed * Time.deltaTime;
            Vector3 move;
            if (to.magnitude <= step) { move = to; _path.Dequeue(); }
            else move = to.normalized * step;
            _fall = _body.isGrounded ? -0.5f : _fall - 9.81f * Time.deltaTime;
            _body.Move(move + Vector3.up * _fall * Time.deltaTime);
            if (_eye != null && to.sqrMagnitude > 1e-4f)
            {
                _eye.position = transform.position + Vector3.up * EyeHeight;
                _eye.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }
        }
    }
}
