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

        bool _opened, _arrived;

        void Start() { if (nextFrame != null) nextFrame.gameObject.SetActive(false); }

        void Update()
        {
            if (_opened || gate == null || !gate.IsOpen || gate.NextWorld == null) return;
            _opened = true;
            gate.Arrived += () => StartCoroutine(Arrive());
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
            pivot.SetParent(nextFrame, true);
            Debug.Log("[Journey] " + gate.name + " open: " + nextFrame.name + " stands behind it");
        }

        IEnumerator Arrive()
        {
            if (_arrived) yield break;
            _arrived = true;
            if (nextFrame == null)
            {
                if (gate.Door != null) Destroy(gate.Door.gameObject);
                if (previousFrame != null) Destroy(previousFrame.gameObject);
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
            Physics.SyncTransforms();
            if (cc != null) cc.enabled = true;
            yield return null;   // A's floors and colliders are gone before B's are made
            foreach (Transform c in nextFrame)
                if (c.GetComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>() == null) c.gameObject.SetActive(true);
            yield return null;
            yield return new WaitForFixedUpdate();
            if (gravity != null) gravity.enabled = true;
            Debug.Log("[Journey] through to " + nextFrame.name);
        }
    }
}
