using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Capture mode: freezes the view at a fixed, repeatable pose so two builds can be compared
    /// pixel for pixel from headset screenshots (adb exec-out screencap -p).
    ///
    /// Without it the app re-centres on every launch and the head keeps whatever tilt the resting
    /// headset has, so screenshots of two builds never line up. Capture mode disables the camera's
    /// TrackedPoseDriver and places the camera at the rig's origin (the current stage's spawn),
    /// <see cref="EyeHeight"/> metres up, level, facing the origin's forward. It follows the
    /// origin every frame, so moving to another stage gives that stage's fixed view.
    ///
    /// The view no longer follows the head, so this is for desk captures, not for wearing.
    /// Turn on from the desk:  adb shell am start -n com.musexr.impossiblemuseum/com.unity3d.player.UnityPlayerActivity --ez musexr.capturePose true
    /// In the Editor: F10 toggles it. Every change is logged as [CapturePose].
    /// </summary>
    public sealed class CapturePose : MonoBehaviour
    {
        public const float EyeHeight = 1.6f;
        public const string IntentExtra = LaunchOptions.CapturePoseExtra;

        InputAction _toggle;
        XROrigin _origin;
        TrackedPoseDriver _driver;
        bool _on;
        float _extraYaw;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject(nameof(CapturePose));
            DontDestroyOnLoad(go);
            var c = go.AddComponent<CapturePose>();
            c._extraYaw = LaunchOptions.CaptureYaw;
            if (LaunchedWithCaptureFlag()) c.Set(true, "launch flag");
        }

        /// <summary>The fixed camera pose for a rig origin: pure, so it can be tested.</summary>
        public static Pose For(Vector3 originPosition, Quaternion originRotation, float extraYaw = 0f)
        {
            float yaw = originRotation.eulerAngles.y + extraYaw;
            return new Pose(originPosition + Vector3.up * EyeHeight, Quaternion.Euler(0f, yaw, 0f));
        }

        void OnEnable()
        {
            _toggle = new InputAction("capture-pose-toggle", InputActionType.Button);
            _toggle.AddBinding("<Keyboard>/f10");
            _toggle.Enable();
        }

        void OnDisable() { _toggle?.Disable(); _toggle?.Dispose(); _toggle = null; }

        void Update()
        {
            if (_toggle != null && _toggle.WasPressedThisFrame()) Set(!_on, "F10");
        }

        void LateUpdate()
        {
            if (!_on || !Resolve()) return;
            var p = For(_origin.transform.position, _origin.transform.rotation, _extraYaw);
            _origin.Camera.transform.SetPositionAndRotation(p.position, p.rotation);
        }

        void Set(bool on, string why)
        {
            _on = on;
            if (Resolve()) _driver.enabled = !on;
            Debug.Log($"[CapturePose] {(on ? "ON" : "off")} ({why}): eye {EyeHeight} m above the stage origin, " +
                      $"level, facing the origin's forward + {_extraYaw:0} deg; head tracking " + (on ? "paused" : "restored"));
        }

        bool Resolve()
        {
            if (_origin == null) _origin = FindFirstObjectByType<XROrigin>();
            if (_origin == null || _origin.Camera == null) return false;
            if (_driver == null) _driver = _origin.Camera.GetComponent<TrackedPoseDriver>();
            if (_driver != null && _on && _driver.enabled) _driver.enabled = false;
            return true;
        }

        static bool LaunchedWithCaptureFlag() => LaunchOptions.CapturePose;
    }
}
