using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The visitor starts facing the opening panel however the app was launched (Saul, 6 Oct: "before I would always
    /// start facing the panel but now I am always like 70 degrees off"). The panel is built facing the rig's start
    /// direction, but the head's yaw inside the rig is wherever the headset's tracking forward happened to be - launched
    /// over adb while turned away, the visitor began turned away. Once the head is tracked, the rig is turned about the
    /// head so the view looks the way the rig started facing. Again after every scene load (Start again).
    /// </summary>
    public sealed class StartFacing : MonoBehaviour
    {
        /// <summary>How long after a scene loads to wait for a tracked head before turning.</summary>
        public const float WaitSeconds = 0.8f, GiveUpSeconds = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject(nameof(StartFacing));
            DontDestroyOnLoad(go);
            var me = go.AddComponent<StartFacing>();
            me.StartCoroutine(me.Align());
            SceneManager.sceneLoaded += (_, __) => { if (me != null) me.StartCoroutine(me.Align()); };
        }

        IEnumerator Align()
        {
            yield return null;
            var rig = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (rig == null || rig.Camera == null) yield break;
            var target = rig.transform.forward; target.y = 0f;   // the way the scene was built for the visitor to face
            if (target.sqrMagnitude < 1e-4f) yield break;
            yield return new WaitForSeconds(WaitSeconds);
            // A head still at its untracked identity pose is not facing anywhere yet: wait for real tracking.
            var cam = rig.Camera.transform;
            for (float t = 0f; t < GiveUpSeconds && cam.localRotation == Quaternion.identity; t += Time.unscaledDeltaTime) yield return null;
            var head = cam.forward; head.y = 0f;
            if (head.sqrMagnitude < 1e-4f) yield break;
            var off = Vector3.SignedAngle(head, target, Vector3.up);
            rig.RotateAroundCameraUsingOriginUp(off);
            Debug.Log("[StartFacing] turned the rig " + off.ToString("0") + " degrees so the visitor faces the start direction");
        }
    }
}
