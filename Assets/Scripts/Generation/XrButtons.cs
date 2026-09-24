using System;
using UnityEngine.InputSystem;

namespace MusePico.Generation
{
    /// <summary>
    /// The handful of controller buttons the harness scenes need, read as XR devices.
    ///
    /// <b>Why this exists.</b> These scenes previously read <c>Gamepad.all</c>. That is dead on
    /// every headset we target: PICO and Quest controllers arrive through OpenXR as
    /// <c>XRController</c> devices and are never surfaced as a Unity <c>Gamepad</c>, so the
    /// controller half of the input silently did nothing and only the keyboard bindings worked.
    /// Measured on the PICO emulator, and true of hardware for the same reason.
    ///
    /// Bound to the generic <c>&lt;XRController&gt;</c> layout rather than to a vendor profile, so
    /// one set of bindings covers PICO 4 and Quest — the per-vendor difference stays in the OpenXR
    /// interaction profiles that <c>XRBuild</c> switches, which is where it belongs.
    ///
    /// Deliberately NOT the XRI action asset: that one drives locomotion and interaction on the
    /// rig, and a harness button is not locomotion. Keeping them separate means rebinding movement
    /// cannot quietly break "press trigger to generate".
    /// </summary>
    public sealed class XrButtons : IDisposable
    {
        public readonly InputAction Trigger;   // act / confirm
        public readonly InputAction Grip;      // push-to-talk
        public readonly InputAction Cancel;    // secondary face button
        public readonly InputAction Next;      // thumbstick, for stepping through presets

        public XrButtons()
        {
            // Keyboard bindings sit alongside the controllers, never instead of them. Without a
            // headset attached, <XRController> resolves to nothing and the whole journey is
            // undriveable in the Editor — which is where it gets compared against her web build.
            Trigger = new InputAction("xr-trigger", InputActionType.Button);
            Trigger.AddBinding("<XRController>{RightHand}/triggerPressed");
            Trigger.AddBinding("<XRController>{LeftHand}/triggerPressed");
            Trigger.AddBinding("<Keyboard>/space");
            Trigger.AddBinding("<Keyboard>/enter");

            Grip = new InputAction("xr-grip", InputActionType.Button);
            Grip.AddBinding("<XRController>{LeftHand}/gripPressed");
            Grip.AddBinding("<XRController>{RightHand}/gripPressed");

            Cancel = new InputAction("xr-cancel", InputActionType.Button);
            Cancel.AddBinding("<XRController>{RightHand}/secondaryButton");
            Cancel.AddBinding("<XRController>{LeftHand}/secondaryButton");
            Cancel.AddBinding("<Keyboard>/escape");

            Next = new InputAction("xr-next", InputActionType.Value, expectedControlType: "Vector2");
            // NOT the thumbstick. XRI's snap-turn lives on the right stick, so binding a second
            // meaning to it meant every attempt to look round also changed the world. Her build
            // has no stick path to this at all - rooms are changed by clicking the navigator - so
            // the only bindings left are the Editor keyboard ones below.
            // The right arrow reads as a full-right stick, so StickFlickedRight latches the same way.
            Next.AddCompositeBinding("2DVector").With("Right", "<Keyboard>/rightArrow")
                                               .With("Left", "<Keyboard>/leftArrow");

            Trigger.Enable(); Grip.Enable(); Cancel.Enable(); Next.Enable();
        }

        public bool TriggerPressed => Trigger.WasPressedThisFrame();
        public bool CancelPressed => Cancel.WasPressedThisFrame();
        public bool GripPressed => Grip.WasPressedThisFrame();
        public bool GripReleased => Grip.WasReleasedThisFrame();

        /// <summary>True on the frame the stick crosses to the right, latched so a held stick
        /// steps once rather than scrolling through every preset in a third of a second.</summary>
        public bool StickFlickedRight()
        {
            float x = Next.ReadValue<UnityEngine.Vector2>().x;
            bool over = x > 0.7f;
            bool fired = over && !_stickLatched;
            _stickLatched = over;
            return fired;
        }

        /// <summary>
        /// The same latch in the other direction — one step per flick, not a scroll.
        ///
        /// A SEPARATE latch from the right one on purpose: sharing it would let a flick right
        /// swallow the next flick left, because the stick passes through centre between them and
        /// only one latch would have cleared.
        /// </summary>
        public bool StickFlickedLeft()
        {
            float x = Next.ReadValue<UnityEngine.Vector2>().x;
            bool over = x < -0.7f;
            bool fired = over && !_stickLatchedLeft;
            _stickLatchedLeft = over;
            return fired;
        }

        bool _stickLatched;
        bool _stickLatchedLeft;

        public void Dispose()
        {
            Trigger?.Dispose(); Grip?.Dispose(); Cancel?.Dispose(); Next?.Dispose();
        }
    }
}
