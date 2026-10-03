namespace MuseXR.Interaction
{
    /// <summary>Anything a grip can take hold of: a carried piece (<see cref="Holdable"/>) or a dial (<see cref="TimeRingDial"/>).</summary>
    public interface IGrippable
    {
        /// <summary>Asked when the grip closes on it. False refuses (a kept choice, another hand already on it).</summary>
        bool TryGrab(GripHand hand, bool byRay);

        /// <summary>Every frame while held.</summary>
        void Hold(GripHand hand);

        /// <summary>The grip opened.</summary>
        void Release(GripHand hand);
    }
}
