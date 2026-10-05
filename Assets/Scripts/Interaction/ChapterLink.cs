using System.Collections;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// One link of the journey in a single scene: chapter A's exit gate opens onto chapter B. B's whole
    /// chapter (its world, layout and interactions, built at the origin as in its own scene) waits
    /// inactive under <see cref="nextFrame"/>. When the gate opens, B's frame takes the pose of the world
    /// the gate shows, so B's layout stands in the world seen through it; B's own copy of the splat
    /// sleeps and the gate's world is the one drawn. On arrival B's frame and the visitor go back to the
    /// origin together in one frame (everything of B assumes the origin), A's frame is destroyed, and
    /// B's interactions wake - gravity held until B's floor exists. The same move JourneyCuration makes
    /// for the Palace.
    /// </summary>
    public sealed class ChapterLink : MonoBehaviour
    {
        [Tooltip("Chapter A's exit gate; its next world is B's.")]
        public MuseXR.Worlds.MoonGate gate;
        [Tooltip("Everything of chapter A: destroyed once the visitor is through.")]
        public Transform previousFrame;
        [Tooltip("Everything of chapter B, inactive until the gate opens.")]
        public Transform nextFrame;

        bool _opened, _arrived, _crossing;
        float _scan;
        readonly System.Collections.Generic.List<Renderer> _held = new System.Collections.Generic.List<Renderer>();

        void Start()
        {
            if (nextFrame != null) nextFrame.gameObject.SetActive(false);
            CompanionGroup.HideMasterMarks(nextFrame);
            CompanionGroup.HideMasterMarks(previousFrame);
        }

        void Update()
        {
            if (_opened && !_arrived && !_crossing && nextFrame != null && (_scan -= Time.deltaTime) <= 0f) { _scan = 0.1f; HoldBack(); }
            if (_opened || gate == null || !gate.IsOpen || gate.NextWorld == null) return;
            _opened = true;
            gate.Arrived += () => StartCoroutine(Arrive());
            gate.Crossed += CompanionGroup.FadeOutAllForCrossing;   // they fade in at their places on arrival
            // Through the plane, nothing of B is behind A's walls any more: everything held back shows, and the check
            // stops. Left running, it hid every piece for the 0.3 s of the crossing (the eye on the far side of the
            // plane reads as "not seen through"), then arrival switched them back on at once (measured, 5 Oct).
            gate.Crossed += () => { _crossing = true; foreach (var held in _held) if (held != null) held.enabled = true; _held.Clear(); };
            if (nextFrame == null) return;   // the last link: the next world is all there is
            var pivot = gate.NextWorld.transform.parent;
            nextFrame.SetPositionAndRotation(pivot.position, pivot.rotation);
            // B's layout stands in the world seen through the gate; its interactions wait for the visitor.
            foreach (Transform c in nextFrame)
            {
                bool world = c.GetComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>() != null;
                // Its layout only: not its exit, whose floor must be made at the origin.
                c.gameObject.SetActive(!world && c.name.StartsWith("Chapter ") && c.GetComponent<ChapterExit>() == null);
            }
            nextFrame.gameObject.SetActive(true);
            // Its pieces come up one by one from now, not at the crossing (Saul, 5 Oct); each, as it is switched on, is
            // held back at once if it cannot be seen through the opening (it showed for a frame first, measured 5 Oct).
            WorldReveal.Begin(nextFrame).Revealed += _ => { if (!_crossing && !_arrived) HoldBack(); };
            pivot.SetParent(nextFrame, true);
            Debug.Log("[Journey] " + gate.name + " open: " + nextFrame.name + " stands behind it");
        }

        /// <summary>
        /// B's meshes are not clipped to the gate's opening as its splats are, and A's splat walls do not hide
        /// them. So until the crossing a mesh of B shows only while the visitor can see it THROUGH the opening:
        /// wholly beyond the gate, and on a line from the eye that passes inside the aperture. Measured on the
        /// full walk, 4 Oct: Van Gogh's 21 m Starry Night ceiling hung out of the Grotto's arch over the terrace,
        /// and Monet's time-ring labels and round-table sign showed through Van Gogh's wall, 22 m off.
        /// Re-checked as the visitor moves and while B's chapter builds itself.
        /// </summary>
        void HoldBack()
        {
            var plane = gate.transform;
            var eye = Camera.main != null ? Camera.main.transform.position : plane.position - plane.forward;
            var e0 = plane.InverseTransformPoint(eye);
            var half = gate.Door != null ? gate.Door.apertureSize * 0.5f : new Vector2(1f, 1.5f);
            _cameIntoView.Clear();
            foreach (var r in nextFrame.GetComponentsInChildren<Renderer>(true))
            {
                if (!_held.Contains(r)) { if (!r.enabled) continue; _held.Add(r); }   // ours to show and hide from now on
                var show = SeenThrough(plane, r.bounds, e0, half);
                if (show && !r.enabled && r.gameObject.activeInHierarchy) _cameIntoView.Add(PieceOf(r.transform));
                r.enabled = show;
            }
            // Into view through the opening: faded up, never switched on whole (Saul, 5 Oct: things "flash into existence").
            foreach (var piece in _cameIntoView) if (piece != null) Appear.Replay(piece.gameObject, 0.4f);
        }

        readonly System.Collections.Generic.HashSet<Transform> _cameIntoView = new System.Collections.Generic.HashSet<Transform>();

        /// <summary>The piece a renderer belongs to: the child of one of the frame's groups (as WorldReveal counts them).</summary>
        Transform PieceOf(Transform t)
        {
            while (t.parent != null && t.parent.parent != null && t.parent.parent != nextFrame) t = t.parent;
            return t.parent != null && t.parent.parent == nextFrame ? t : t.parent == nextFrame ? t : t;
        }

        /// <summary>Wholly beyond the gate plane, and its centre seen from the eye through the aperture.</summary>
        static bool SeenThrough(Transform plane, Bounds b, Vector3 eyeLocal, Vector2 half)
        {
            var c = b.center; var e = b.extents;
            for (var i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                if (plane.InverseTransformPoint(corner).z < 0.05f) return false;   // reaches back to the visitor's side (-Z)
            }
            var cl = plane.InverseTransformPoint(c);
            if (eyeLocal.z >= 0f || cl.z <= 0f) return false;
            var t = -eyeLocal.z / (cl.z - eyeLocal.z);                              // where eye -> centre meets the plane
            var hit = Vector3.Lerp(eyeLocal, cl, t);
            return Mathf.Abs(hit.x) <= half.x && hit.y >= 0f && hit.y <= half.y * 2f;
        }

        static readonly System.Reflection.FieldInfo Registered =
            typeof(GaussianSplatting.Runtime.GaussianSplatRenderer).GetField("m_Registered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// A world the visitor has just walked into that never registered with the splat render system draws
        /// nothing: the Van Gogh room stood empty after the Grotto's arch until its renderer was switched off and
        /// on (full walk, 5 Oct 2026). A renderer enabled before its resources are ready skips registering and
        /// nothing retries it. Only those are cycled: re-enabling one that drew would re-upload its splats.
        /// </summary>
        static void Reregister(Transform frame)
        {
            if (Registered == null) return;
            foreach (var r in frame.GetComponentsInChildren<GaussianSplatting.Runtime.GaussianSplatRenderer>())
            {
                if (!r.isActiveAndEnabled || (bool)Registered.GetValue(r)) continue;
                r.enabled = false; r.enabled = true;
                Debug.Log("[Journey] " + r.name + " had not registered to draw: re-enabled on arrival (now " + ((bool)Registered.GetValue(r) ? "registered" : "STILL NOT registered") + ")");
            }
        }

        /// <summary>The chapter's paintings, props and heroes come up over this once the visitor is through.</summary>
        public const float ArriveFade = 0.8f;

        IEnumerator Arrive()
        {
            if (_arrived) yield break;
            _arrived = true;
            foreach (var held in _held) if (held != null) held.enabled = true;
            _held.Clear();
            if (nextFrame == null)
            {
                if (gate.Door != null) Destroy(gate.Door.gameObject);
                // Unset on the last link (Your world -> the Gate), Your world's props stood on in the
                // conservatory: the frame the gate stands in is the chapter just left.
                var last = previousFrame != null ? previousFrame : gate.transform.root;
                if (last != null && last != transform.root)
                {
                    // Its walk floor is the only floor the conservatory has now (the Gate's went at the
                    // first crossing): taken with the frame, the visitor fell (y -86 in the next second).
                    foreach (var floor in last.GetComponentsInChildren<Transform>())
                        if (floor.name == "Walk Floor") floor.SetParent(null, true);
                    Destroy(last.gameObject);
                }
                Debug.Log("[Journey] through " + gate.name + ": the journey's last world");
                yield break;
            }
            var rig = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var cc = rig != null ? rig.GetComponent<CharacterController>() : null;
            var gravity = rig != null ? rig.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>() : null;
            if (gravity != null) gravity.enabled = false;
            if (cc != null) cc.enabled = false;
            var r = Quaternion.Inverse(nextFrame.rotation);
            var f = nextFrame.position;
            if (rig != null) rig.transform.SetPositionAndRotation(r * (rig.transform.position - f), r * rig.transform.rotation);
            if (gate.Door != null) Destroy(gate.Door.gameObject);
            if (previousFrame != null) Destroy(previousFrame.gameObject);   // the gate goes with it
            nextFrame.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Reregister(nextFrame);
            Physics.SyncTransforms();
            if (cc != null) cc.enabled = true;
            yield return null;   // A's floors and colliders are gone before B's are made
            foreach (Transform c in nextFrame)
                if (c.GetComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>() == null && !c.gameObject.activeSelf) c.gameObject.SetActive(true);
            WorldReveal.Begin(nextFrame);   // the rest of its pieces join the same one-by-one fade (Saul, 5 Oct)
            yield return null;
            yield return new WaitForFixedUpdate();
            if (gravity != null) gravity.enabled = true;
            Debug.Log("[Journey] through to " + nextFrame.name);
        }
    }
}
