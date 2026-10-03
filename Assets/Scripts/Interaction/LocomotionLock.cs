using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace MuseXR.Interaction
{
    /// <summary>
    /// While a hand holds something its stick rotates the object, so that stick must stop
    /// teleporting and snap-turning (chatplan §2.4: stick rotation "must suppress teleport on that
    /// stick"). Both sticks carry teleport and snap turn in this rig, so the lock is per hand: it
    /// disables that hand's locomotion actions in the rig's XRI action asset and re-enables exactly
    /// the ones it disabled when the grip opens. Only actions that were enabled are touched, so a
    /// mode the rig itself switched off stays off.
    /// </summary>
    public static class LocomotionLock
    {
        /// <summary>Every locomotion action XRI's starter asset drives from a stick.</summary>
        public static readonly string[] Actions = { "Teleport Mode", "Teleport Mode Cancel", "Snap Turn", "Turn", "Move", "Grab Move" };

        static readonly Dictionary<Hand, List<InputAction>> Disabled = new Dictionary<Hand, List<InputAction>>();

        public static bool IsLocked(Hand hand) => Disabled.ContainsKey(hand);

        public static void Acquire(Hand hand)
        {
            if (Disabled.ContainsKey(hand)) return;
            var list = new List<InputAction>();
            foreach (var action in Find(hand))
            {
                if (!action.enabled) continue;
                action.Disable();
                list.Add(action);
            }
            Disabled[hand] = list;
        }

        public static void Release(Hand hand)
        {
            if (!Disabled.TryGetValue(hand, out var list)) return;
            Disabled.Remove(hand);
            foreach (var action in list) action.Enable();
        }

        static IEnumerable<InputAction> Find(Hand hand)
        {
            var mapName = hand == Hand.Left ? "XRI Left Locomotion" : "XRI Right Locomotion";
            foreach (var manager in Object.FindObjectsByType<InputActionManager>(FindObjectsSortMode.None))
            {
                if (manager.actionAssets == null) continue;
                foreach (var asset in manager.actionAssets)
                {
                    var map = asset != null ? asset.FindActionMap(mapName) : null;
                    if (map == null) continue;
                    foreach (var name in Actions)
                    {
                        var action = map.FindAction(name);
                        if (action != null) yield return action;
                    }
                }
            }
        }
    }
}
