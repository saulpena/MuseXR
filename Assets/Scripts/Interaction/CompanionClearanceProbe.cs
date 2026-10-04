using System.Collections;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The companion-clearance harness (Saul, 4 Oct 2026: "that should never happen - peripheral view and
    /// sides at most; they follow you but do not get in your way"). Added to any object in Play, it moves
    /// the visitor through the moves a visitor makes with smooth locomotion - walking, smooth and snap turns,
    /// glances, a brisk walk, a walk ending in a turn round, a step back - and judges every companion of every group in the scene each
    /// frame with <see cref="ClearanceLog"/>. Each confirmed fault is captured, as is the end of every move,
    /// to <see cref="Folder"/>. It prints its own VERDICT; it is a harness to run, not to edit to pass.
    /// </summary>
    public sealed class CompanionClearanceProbe : MonoBehaviour
    {
        public const string Folder = "Assets/Screenshots/clearance";
        public string label = "scene";
        public bool Done { get; private set; }
        public string Report { get; private set; } = "running";
        public ClearanceLog Log { get; } = new ClearanceLog();

        Unity.XR.CoreUtils.XROrigin _rig;
        Transform _head;
        string _phase = "start";
        int _shots;
        readonly List<string> _captured = new List<string>();

        IEnumerator Start()
        {
            _rig = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            _head = Camera.main != null ? Camera.main.transform : null;
            if (_rig == null || _head == null) { Report = "no rig or camera"; Done = true; yield break; }
            System.IO.Directory.CreateDirectory(Folder);
            yield return Phase("hold", Hold(1.5f));
            yield return Phase("walk forward 3 m", Walk(Vector3.forward, 3f, 1.2f));
            yield return Phase("smooth turn left 90", Turn(-90f, 90f));
            yield return Phase("hold after turn", Hold(2f));
            yield return Phase("smooth turn right 180", Turn(180f, 120f));
            yield return Phase("hold after 180", Hold(2f));
            yield return Phase("walk forward 2 m", Walk(Vector3.forward, 2f, 1.2f));
            yield return Phase("snap turns 45 right x2", SnapTwice(45f));
            yield return Phase("hold after snaps", Hold(2f));
            yield return Phase("glance left 55 (head only)", Glance(-55f, 2f));
            yield return Phase("glance right 55 (head only)", Glance(55f, 2f));
            // Smooth locomotion only (Saul: "there is no teleport"): a brisk walk, then a walk that ends
            // in a turn round, which is what a teleport-and-turn stood in for.
            yield return Phase("brisk walk 3 m", Walk(Vector3.forward, 3f, 2.0f));
            yield return Phase("hold after brisk walk", Hold(2f));
            yield return Phase("walk 2.5 m then turn 180", WalkThenTurn(2.5f, 180f));
            yield return Phase("hold after walk+180", Hold(3f));
            yield return Phase("step back 1.5 m", Walk(Vector3.back, 1.5f, 0.6f));
            yield return Phase("hold at end", Hold(1.5f));
            Report = Log.Verdict() + "\n" + string.Join("\n", Log.Faults) + (_captured.Count > 0 ? "\nCaptures:\n" + string.Join("\n", _captured) : "");
            Debug.Log("[Clearance] " + label + "\n" + Report);
            Done = true;
        }

        IEnumerator Phase(string name, IEnumerator body)
        {
            _phase = name;
            yield return body;
            Capture("end " + name);
            // And what the visitor sees just after: a capture in the frame a move lands catches what the
            // move itself did; this one catches what everything did about it.
            for (float t = 0f; t < 0.3f; t += Time.deltaTime) yield return null;
            Capture("after " + name);
        }

        void LateUpdate()
        {
            if (Done || _head == null) return;
            var gaze = _head.forward; gaze.y = 0f;
            foreach (var g in FindObjectsByType<CompanionGroup>(FindObjectsSortMode.None))
                foreach (var kv in g.Figures)
                {
                    if (kv.Value == null || !kv.Value.gameObject.activeInHierarchy) continue;
                    if (!Visible(kv.Value)) continue;
                    var e = _head.position; var f = kv.Value.position;
                    var fault = Log.Add(kv.Key, _phase, e.x, e.z, gaze.x, gaze.z, f.x, f.z, Time.deltaTime, Time.time);
                    if (fault != null) Capture("FAULT " + fault.Kind + " " + kv.Key);
                }
        }

        static bool Visible(Transform figure)
        {
            foreach (var r in figure.GetComponentsInChildren<Renderer>()) if (r.enabled) return true;
            return false;
        }

        void Capture(string what)
        {
            if (_shots >= 60) return;
            var file = $"{Folder}/{label}-{_shots:00}-{Sanitise(what)}.png";
            ScreenCapture.CaptureScreenshot(file);
            _captured.Add(file + "  [" + _phase + "]");
            _shots++;
        }

        static string Sanitise(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (var c in s) b.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            return b.ToString().Trim('-');
        }

        // ---- moves -----------------------------------------------------------------------------

        static IEnumerator Hold(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        /// <summary>Walk along the head's flat facing (or against it), at <paramref name="speed"/> m/s.</summary>
        IEnumerator Walk(Vector3 localDir, float metres, float speed)
        {
            var fwd = _head.forward; fwd.y = 0f; fwd.Normalize();
            var dir = localDir.z >= 0f ? fwd : -fwd;
            for (float done = 0f; done < metres;)
            {
                var step = Mathf.Min(metres - done, speed * Time.deltaTime);
                _rig.transform.position += dir * step;
                done += step;
                yield return null;
            }
        }

        /// <summary>A stick turn: the whole rig round the head, at <paramref name="degreesPerSecond"/>.</summary>
        IEnumerator Turn(float degrees, float degreesPerSecond)
        {
            for (float done = 0f; Mathf.Abs(done) < Mathf.Abs(degrees);)
            {
                var step = Mathf.Sign(degrees) * Mathf.Min(Mathf.Abs(degrees) - Mathf.Abs(done), degreesPerSecond * Time.deltaTime);
                _rig.RotateAroundCameraUsingOriginUp(step);
                done += step;
                yield return null;
            }
        }

        IEnumerator SnapTwice(float degrees)
        {
            _rig.RotateAroundCameraUsingOriginUp(degrees);
            yield return Hold(0.4f);
            _rig.RotateAroundCameraUsingOriginUp(degrees);
        }

        /// <summary>The head alone turns (as in a headset), held, then back.</summary>
        IEnumerator Glance(float degrees, float hold)
        {
            var start = _head.localRotation;
            for (float t = 0f; t < 0.4f; t += Time.deltaTime) { _head.localRotation = start * Quaternion.Euler(0f, degrees * t / 0.4f, 0f); yield return null; }
            for (float t = 0f; t < hold; t += Time.deltaTime) { _head.localRotation = start * Quaternion.Euler(0f, degrees, 0f); yield return null; }
            _head.localRotation = start;
        }

        IEnumerator WalkThenTurn(float metres, float degrees)
        {
            yield return Walk(Vector3.forward, metres, 1.2f);
            yield return Turn(degrees, 120f);
        }

        IEnumerator Teleport(float metres, float thenTurn)
        {
            var fwd = _head.forward; fwd.y = 0f; fwd.Normalize();
            var body = _rig.GetComponent<CharacterController>();
            var had = body != null && body.enabled;
            if (had) body.enabled = false;
            _rig.MoveCameraToWorldLocation(_head.position + fwd * metres);
            Physics.SyncTransforms();
            if (had) body.enabled = true;
            if (!Mathf.Approximately(thenTurn, 0f)) { yield return null; _rig.RotateAroundCameraUsingOriginUp(thenTurn); }
            yield return null;
        }
    }
}
