using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace MuseXR.Interaction
{
    public enum Hand { Left, Right }

    /// <summary>
    /// What a hand gives the interactions: where it points, whether its grip is held, its stick,
    /// and a way to buzz it. The headset reads controllers; the Editor and the test harness drive
    /// the same interface with a mouse or with code, so grab, rotate and place run without a headset.
    /// </summary>
    public interface IHandSource
    {
        Hand Hand { get; }
        /// <summary>The controller's aim pose: position is the hand, forward is the ray.</summary>
        Transform Aim { get; }
        bool Grip { get; }
        /// <summary>Her "Trigger draws and selects".</summary>
        bool Trigger { get; }
        Vector2 Stick { get; }
        void Buzz(float amplitude, float seconds);
    }

    /// <summary>
    /// A real controller. Her buttons (chatplan §3.1): "Grip grabs. Stick rotates a held object in
    /// 15° steps." Grip is free in this rig, because XRI's Select is rebound to the trigger, so this
    /// reads the grip directly rather than through XRI and leaves the trigger to pointing and drawing.
    /// </summary>
    public sealed class XrHandSource : IHandSource
    {
        readonly InputAction _grip, _trigger, _stick;
        readonly XRNode _node;

        public XrHandSource(Hand hand, Transform aim)
        {
            Hand = hand;
            Aim = aim;
            var side = hand == Hand.Left ? "{LeftHand}" : "{RightHand}";
            _grip = new InputAction("grip-" + hand, InputActionType.Value, expectedControlType: "Axis");
            _grip.AddBinding("<XRController>" + side + "/{Grip}");
            _trigger = new InputAction("trigger-" + hand, InputActionType.Value, expectedControlType: "Axis");
            _trigger.AddBinding("<XRController>" + side + "/{Trigger}");
            _trigger.Enable();
            _stick = new InputAction("stick-" + hand, InputActionType.Value, expectedControlType: "Vector2");
            _stick.AddBinding("<XRController>" + side + "/{Primary2DAxis}");
            _grip.Enable(); _stick.Enable();
            _node = hand == Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
        }

        public Hand Hand { get; }
        public Transform Aim { get; }

        /// <summary>Held past half travel. An analogue threshold, so a resting finger does not grab.</summary>
        public bool Grip => _grip.ReadValue<float>() > 0.55f;

        public bool Trigger => _trigger.ReadValue<float>() > 0.55f;

        public Vector2 Stick => _stick.ReadValue<Vector2>();

        public void Buzz(float amplitude, float seconds)
        {
            var device = InputDevices.GetDeviceAtXRNode(_node);
            if (device.isValid) device.SendHapticImpulse(0u, amplitude, seconds);
        }

        public void Dispose()
        {
            _grip.Dispose(); _trigger.Dispose(); _stick.Dispose();
        }
    }

    /// <summary>A hand driven by code: the test harness, and the Editor's mouse hand.</summary>
    public sealed class ScriptedHandSource : IHandSource
    {
        public ScriptedHandSource(Hand hand, Transform aim) { Hand = hand; Aim = aim; }
        public Hand Hand { get; }
        public Transform Aim { get; }
        public bool Grip { get; set; }
        public bool Trigger { get; set; }
        public Vector2 Stick { get; set; }
        /// <summary>The last buzz asked for, so a check can see the haptic fired.</summary>
        public float LastBuzzAmplitude { get; private set; }
        public int Buzzes { get; private set; }
        public void Buzz(float amplitude, float seconds) { LastBuzzAmplitude = amplitude; Buzzes++; }
    }
}
