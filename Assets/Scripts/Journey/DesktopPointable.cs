using System;
using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>
    /// What a mouse click does to this object when there is no headset.
    ///
    /// Everything pointable in the journey — panel plates, room dots, portraits, artworks and the
    /// masters themselves — carries one of these beside its XRSimpleInteractable, and both run the
    /// same callback. The mouse path used to guess from sibling order ("the action plate is always
    /// the last child"), which made every navigator arrow and dot fire FORM MY ANSWER instead.
    /// </summary>
    public sealed class DesktopPointable : MonoBehaviour
    {
        public Action Picked;

        public void Pick() => Picked?.Invoke();
    }
}
