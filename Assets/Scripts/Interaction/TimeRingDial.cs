using System;
using MuseXR.Slots;
using MuseXR.Worlds;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her Monet time ring, as the hand turns it (chapter D, frames 1-2): "Grip the ring and turn".
    /// The grip takes the ring; the wrist's roll turns it; it clicks into three detents (Mist left,
    /// Afternoon top, Dusk right), and each detent gives "a haptic tick and a water sound" and starts
    /// the 4 s ease through <see cref="TimeRingDriver.Choose"/>. Let go and it settles on its detent.
    ///
    /// The ring is a child named "Ring" that turns about the dial's own Z axis. The dial's +Z points
    /// away from the visitor (the flat-thing rule), so a clockwise turn as they see it is a negative
    /// roll about +Z. The driver's own <c>ringMesh</c> is left unset: two things turning one mesh fight.
    /// </summary>
    public sealed class TimeRingDial : MonoBehaviour, IGrippable
    {
        public DialDetents Detents { get; } = new DialDetents();
        public Transform Ring { get; private set; }
        public TimeRingDriver Driver { get; set; }
        public GripHand HeldBy { get; private set; }

        /// <summary>Raised on every detent the dial clicks into.</summary>
        public event Action<TimeOfDay> Clicked;

        public static TimeRingDial Make(GameObject root, Transform ring, TimeRingDriver driver)
        {
            var d = root.AddComponent<TimeRingDial>();
            d.Ring = ring;
            d.Driver = driver;
            if (driver != null) d.SetDetent((int)driver.Ring.Chosen);
            return d;
        }

        void SetDetent(int detent)
        {
            // DialDetents starts at Afternoon; align it with whatever the driver started on.
            // A turn is relative to where the dial stands: turning BY the target angle left it stuck once it was off
            // the top (from Mist, "to Afternoon" turned by 0 degrees and stayed on Mist - the desktop keys, 5 Oct).
            var target = DialDetents.AngleOf(detent);
            Detents.Grab(0f); Detents.Turn(target - Detents.Angle); Detents.Release();
        }

        /// <summary>The wrist's roll about the dial's axis, degrees, positive clockwise as the visitor sees it.</summary>
        public float Twist(Transform aim)
        {
            var axis = transform.forward;   // away from the visitor
            var up = Vector3.ProjectOnPlane(aim.up, axis);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(-aim.forward, axis);   // pointing straight down the axis
            // SignedAngle about the away axis is negative for up -> right, which the visitor sees as clockwise.
            return -Vector3.SignedAngle(transform.up, up, axis);
        }

        public bool TryGrab(GripHand hand, bool byRay)
        {
            if (HeldBy != null) return false;
            HeldBy = hand;
            Detents.Grab(Twist(hand.Source.Aim));
            return true;
        }

        public void Hold(GripHand hand)
        {
            if (hand != HeldBy) return;
            var crossed = Detents.Turn(Twist(hand.Source.Aim));
            if (crossed >= 0) Click(crossed, hand.Source);
        }

        public void Release(GripHand hand)
        {
            if (hand != HeldBy) return;
            HeldBy = null;
            Detents.Release();
        }

        void Click(int detent, IHandSource hand)
        {
            var time = (TimeOfDay)detent;   // Mist 0, Afternoon 1, Dusk 2: the same order
            hand?.Buzz(SlotRules.LightAmplitude * 1.5f, SlotRules.LightSeconds);
            ChimePlayer.Play(ChimePlayer.TickClip(), Ring != null ? Ring.position : transform.position, 0.7f);
            Driver?.Choose(time);
            Clicked?.Invoke(time);
        }

        /// <summary>Turn it from code: the Editor keys and the test harness.</summary>
        public void Step(int direction)
        {
            var next = Mathf.Clamp(Detents.Detent + Math.Sign(direction), 0, DialDetents.Count - 1);
            if (next == Detents.Detent) return;
            SetDetent(next);
            Click(next, null);
        }

        void Update()
        {
            if (Ring == null) return;
            var goal = Quaternion.Euler(0f, 0f, -Detents.Angle);
            // Follows the hand exactly while held; settles softly onto the detent once let go.
            Ring.localRotation = HeldBy != null ? goal
                : Quaternion.Slerp(Ring.localRotation, goal, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }
    }
}
