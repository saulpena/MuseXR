using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A world's pieces come up one by one (Saul, 5 Oct: "objects fade in smoothly instead of just popping in ... start
    /// appearing once the world becomes available, not when I cross the door ... 1 by 1 ... consistent across all
    /// worlds"). Put on a chapter's frame when its gate opens; every frame it finds the frame's pieces - the children of
    /// its groups (a painting, a statue, a plinth, a gate), including those its builders make a frame later - hides each
    /// before it is ever drawn, then fades them in nearest first, <see cref="StepSeconds"/> apart. The masters' marks
    /// keep their own fades. It goes quiet once nothing new has turned up for <see cref="QuietSeconds"/>, so what the
    /// visitor makes later (a replica, a chip) is never caught by it.
    /// </summary>
    public sealed class WorldReveal : MonoBehaviour
    {
        public const float StepSeconds = 0.25f, FadeSeconds = 0.9f, QuietSeconds = 4f;

        readonly HashSet<GameObject> _known = new HashSet<GameObject>();
        readonly List<GameObject> _queue = new List<GameObject>();
        float _next, _quiet;

        /// <summary>Start (or resume) revealing <paramref name="frame"/>'s pieces.</summary>
        public static WorldReveal Begin(Transform frame)
        {
            if (frame == null) return null;
            var w = frame.GetComponent<WorldReveal>();
            if (w == null) w = frame.gameObject.AddComponent<WorldReveal>();   // not ??: Unity's fake null
            w._quiet = 0f;
            w.enabled = true;
            return w;
        }

        /// <summary>
        /// Everything still waiting comes up now, together (a fade still): for a world switched on all at once in a test
        /// or a jump, where a slow sequence would only be in the way.
        /// </summary>
        public void Flush()
        {
            foreach (var go in _queue) if (go != null) Appear.In(go, FadeSeconds);
            _queue.Clear();
        }

        // LateUpdate: a piece built in Start or Update this frame is hidden before this frame is drawn.
        void LateUpdate()
        {
            foreach (Transform group in transform)
            {
                if (!group.gameObject.activeSelf || IsWorld(group)) continue;
                if (group.childCount == 0) Consider(group.gameObject);
                else foreach (Transform piece in group) Consider(piece.gameObject);
            }
            _quiet += Time.deltaTime;
            if (_queue.Count > 0 && Time.time >= _next)
            {
                var eye = Camera.main != null ? Camera.main.transform.position : transform.position;
                var best = -1; var bestD = float.MaxValue;
                for (var i = 0; i < _queue.Count; i++)
                {
                    if (_queue[i] == null) continue;
                    var d = (_queue[i].transform.position - eye).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) { var go = _queue[best]; _queue.RemoveAt(best); Appear.In(go, FadeSeconds); }
                else _queue.Clear();
                _next = Time.time + StepSeconds;
                _quiet = 0f;
            }
            if (_queue.Count == 0 && _quiet > QuietSeconds) enabled = false;
        }

        void Consider(GameObject go)
        {
            if (!go.activeSelf || _known.Contains(go)) return;
            if (go.name.StartsWith("Mark ") || IsWorld(go.transform)) { _known.Add(go); return; }   // the masters and the world: not pieces
            if (go.GetComponentInChildren<Renderer>(true) == null) return;   // not built yet (or no visuals): looked at again
            _known.Add(go);
            go.SetActive(false);
            _queue.Add(go);
            _quiet = 0f;
        }

        static bool IsWorld(Transform t) => t.GetComponentInChildren<GaussianSplatting.Runtime.GaussianSplatRenderer>(true) != null;
    }
}
