using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Gives each controller in the rig a grip. It finds the rig's Near-Far interactors (their
    /// transforms are the controllers' aim poses) and puts a <see cref="GripHand"/> beside each. With
    /// no headset running it adds a mouse hand instead, so the test scene works at a desk:
    ///
    ///   hold G     grip (takes what the mouse points at)
    ///   Q / E      stick left / right (15° steps)
    ///   Z / C      roll the wrist (turns the time ring)
    ///   scroll     carry nearer or further
    ///   Enter / Backspace   A / B
    /// </summary>
    public sealed class HandsBootstrap : MonoBehaviour
    {
        public readonly List<GripHand> Hands = new List<GripHand>();
        public DesktopHand Desktop { get; private set; }

        void Start()
        {
            // Inactive included: the rig's modality manager keeps a controller switched off until it is
            // tracked, which on a headset can be after Start. A GripHand on it simply waits.
            foreach (var nf in FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (nf.handedness == InteractorHandedness.None) continue;
                var hand = nf.handedness == InteractorHandedness.Left ? Hand.Left : Hand.Right;
                if (Hands.Exists(h => h.Source.Hand == hand)) continue;
                Hands.Add(GripHand.Attach(nf.gameObject, new XrHandSource(hand, nf.transform)));
            }
            if (!UnityEngine.XR.XRSettings.isDeviceActive && Camera.main != null)
            {
                Desktop = DesktopHand.Make(Camera.main);
                Hands.Add(Desktop.Grip);
            }
            Debug.Log("[Interaction] grips: " + Hands.Count + (Desktop != null ? " (incl. mouse hand)" : ""));
        }
    }

    /// <summary>The mouse as a right hand, for the Editor without a headset.</summary>
    public sealed class DesktopHand : MonoBehaviour
    {
        public ScriptedHandSource Source { get; private set; }
        public GripHand Grip { get; private set; }

        /// <summary>When true, code drives the hand (the test harness) and the mouse is ignored.</summary>
        public bool Scripted { get; set; }

        Camera _cam;
        float _depth = 0.45f, _roll;

        public static DesktopHand Make(Camera cam)
        {
            var go = new GameObject("Desktop Hand");
            var d = go.AddComponent<DesktopHand>();
            d._cam = cam;
            d.Source = new ScriptedHandSource(Hand.Right, go.transform);
            d.Grip = GripHand.Attach(go, d.Source);
            return d;
        }

        void Update()
        {
            if (Scripted || _cam == null) return;
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null || kb == null) return;
            _depth = Mathf.Clamp(_depth + mouse.scroll.ReadValue().y * 0.0005f, 0.2f, 3f);
            if (kb.zKey.isPressed) _roll -= 90f * Time.deltaTime;
            if (kb.cKey.isPressed) _roll += 90f * Time.deltaTime;
            var ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            var rot = Quaternion.LookRotation(ray.direction, _cam.transform.up) * Quaternion.Euler(0f, 0f, -_roll);
            // Before a grab the aim sits at the camera, so the ray starts where the eye is.
            var held = Grip.Held != null;
            transform.SetPositionAndRotation(held ? ray.origin + ray.direction * _depth : ray.origin, rot);
            Source.Grip = kb.gKey.isPressed;
            Source.Stick = new Vector2(kb.eKey.isPressed ? 1f : kb.qKey.isPressed ? -1f : 0f, 0f);
        }
    }
}
