using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Keyboard and mouse movement when there is no headset — the Editor without Link, or a
    /// desktop player. WASD / arrows move, Shift is faster, hold the RIGHT mouse button to look.
    ///
    /// Right, not left: the left button is the Editor's click-a-plate (<c>JourneyPanel</c>).
    /// None of the journey's own keys (R, Space, Enter, Backspace, 1-0, F9) are used here.
    ///
    /// Switches itself off whenever an XR device is active, so a headset or Link session is
    /// untouched. When the rig's CharacterController is on (a walking stage) movement goes through
    /// it, so walls and floors hold; otherwise the rig slides horizontally at its current height.
    /// Yaw turns the rig about the camera, pitch tilts the camera's parent — never the camera
    /// itself, whose local pose belongs to the TrackedPoseDriver.
    /// </summary>
    public sealed class DesktopMove : MonoBehaviour
    {
        [Tooltip("The XR rig root. Defaults to this transform.")]
        public Transform rig;

        [Tooltip("The camera. Defaults to Camera.main.")]
        public Transform head;

        public float walkSpeed = 2f;
        public float fastSpeed = 5f;
        [Tooltip("Degrees per pixel of mouse movement while the right button is held.")]
        public float lookSensitivity = 0.15f;

        float _pitch;

        /// <summary>
        /// True while a text box has the keyboard (the masters' ask form): WASD then types a
        /// question instead of walking. Looking with the mouse still works.
        /// </summary>
        public static bool Suspended { get; set; }

        void Awake()
        {
            if (rig == null) rig = transform;
        }

        [Tooltip("Vertical field of view without a headset. Unity's 60 left the masters, who stand " +
                 "either side of the panel, and their speech bubbles half off the screen.")]
        public float desktopFieldOfView = 85f;

        void Update()
        {
            if (!DesktopMotion.Active(UnityEngine.XR.XRSettings.isDeviceActive)) return;
            if (head == null) { var cam = Camera.main; if (cam == null) return; head = cam.transform; }
            var eye = head.GetComponent<Camera>();
            if (eye != null && !Mathf.Approximately(eye.fieldOfView, desktopFieldOfView)) eye.fieldOfView = desktopFieldOfView;

            Look(Mouse.current);
            if (!Suspended) Move(Keyboard.current);
        }

        void Look(Mouse mouse)
        {
            if (mouse == null || !mouse.rightButton.isPressed) return;
            var delta = mouse.delta.ReadValue() * lookSensitivity;

            rig.RotateAround(head.position, Vector3.up, delta.x);

            var pitchPivot = head.parent;
            if (pitchPivot == null || pitchPivot == rig) return;
            var d = DesktopMotion.ClampPitchDelta(_pitch, -delta.y);
            if (Mathf.Approximately(d, 0f)) return;
            pitchPivot.RotateAround(head.position, rig.right, d);
            _pitch += d;
        }

        void Move(Keyboard keyboard)
        {
            if (keyboard == null) return;
            var input = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
            if (input == Vector2.zero) return;

            var speed = keyboard.shiftKey.isPressed ? fastSpeed : walkSpeed;
            var step = DesktopMotion.Planar(input, head.eulerAngles.y, speed, Time.deltaTime);

            var body = rig.GetComponent<CharacterController>();
            if (body != null && body.enabled) body.Move(step);
            else rig.position += step;
        }
    }
}
