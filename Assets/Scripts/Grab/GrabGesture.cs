namespace MusePico.Grab
{
    public enum GrabRelease
    {
        /// <summary>A quick press with a still hand: the visitor was pointing, not moving anything.</summary>
        Tap,
        /// <summary>Held, carried, turned or taken in two hands: the visitor moved the painting.</summary>
        Moved,
    }

    /// <summary>
    /// Tells a click from a grab, when both are the same trigger.
    ///
    /// XRI's Select is bound to the TRIGGER in this project (the grip is push-to-talk), and a hung
    /// painting already used the trigger to open its reading. So one press has to mean both: a
    /// tap opens the work, a hold picks it up. The hand is measured, not the painting, because a
    /// far grab puts the painting on the end of the ray — a 1 degree wrist twitch at 5 m moves it
    /// 9 cm, which would make every tap at a distant wall read as a grab.
    /// </summary>
    public static class GrabGesture
    {
        /// <summary>Longer than this and the press was a hold. A deliberate click is ~0.1-0.2 s.</summary>
        public const float TapSeconds = 0.35f;
        /// <summary>The controller may drift this far during a tap (metres).</summary>
        public const float TapTravel = 0.03f;
        /// <summary>The controller may turn this much during a tap (degrees).</summary>
        public const float TapTurn = 6f;

        public static GrabRelease Classify(float heldSeconds, float handTravel, float handTurnDegrees, int mostHands)
        {
            if (mostHands > 1) return GrabRelease.Moved;          // two hands is always a scale
            if (heldSeconds < 0f || heldSeconds > TapSeconds) return GrabRelease.Moved;
            if (handTravel > TapTravel) return GrabRelease.Moved;
            if (handTurnDegrees > TapTurn) return GrabRelease.Moved;
            return GrabRelease.Tap;
        }
    }
}
