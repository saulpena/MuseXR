using System;
using MusePico.Generation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MusePico.Characters
{
    /// <summary>
    /// Steps every rigged master through the seven clips so they can be watched without touching
    /// the Animator window.
    ///
    /// <b>Why this exists.</b> The clips are driven by three parameters on
    /// <c>VanGoghMaster.controller</c> - an int <c>Gesture</c> for the four new gestures, a float
    /// <c>Speed</c> for Walk and a bool <c>Talking</c> for Talk. Seeing them otherwise means
    /// entering Play Mode, selecting a character, opening the Animator window and typing values,
    /// once per character. That is a poor way to judge whether an animation reads.
    ///
    /// Drives every animator in step by default, so the five stand as a row doing the same thing
    /// and the differences between the bodies are the only variable.
    ///
    /// Input is keyboard AND controller. Not <c>Gamepad</c>: a PICO or Quest controller arrives
    /// through OpenXR as an <c>XRController</c> and is never surfaced as a Gamepad, which is why
    /// <see cref="XrButtons"/> exists and why this reuses it rather than rolling its own bindings.
    /// </summary>
    public sealed class GestureCycler : MonoBehaviour
    {
        /// <summary>One entry per clip, expressed as the parameter values that reach it.</summary>
        public readonly struct Pose
        {
            public readonly string Name;
            public readonly int Gesture;
            public readonly float Speed;
            public readonly bool Talking;

            public Pose(string name, int gesture, float speed, bool talking)
            {
                Name = name; Gesture = gesture; Speed = speed; Talking = talking;
            }
        }

        // Ordered so the four new gestures sit together, with the three originals as the control
        // group either side of them.
        public static readonly Pose[] Poses =
        {
            new Pose("Idle",    0, 0f,   false),
            new Pose("Greet",   1, 0f,   false),
            new Pose("Listen",  2, 0f,   false),
            new Pose("Ponder",  3, 0f,   false),
            new Pose("Present", 4, 0f,   false),
            new Pose("Talk",    0, 0f,   true),
            new Pose("Walk",    0, 0.5f, false),
        };

        [Tooltip("Animators to drive. Leave empty and it finds every Animator in the scene at Start.")]
        public Animator[] animators;

        [Tooltip("Advance on a timer. Turn off to step by hand.")]
        public bool autoAdvance = true;

        [Tooltip("Seconds per clip when advancing automatically.")]
        public float secondsPerPose = 4f;

        [Tooltip("Optional. Shows which clip is playing and the controls.")]
        public TMP_Text label;

        int _index;
        float _elapsed;
        XrButtons _xr;

        public Pose Current => Poses[_index];

        void Start()
        {
            if (animators == null || animators.Length == 0)
                animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);

            try { _xr = new XrButtons(); }
            catch (Exception e) { Debug.LogWarning("[GestureCycler] no XR input: " + e.Message); }

            Apply();
        }

        void OnDestroy() => _xr?.Dispose();

        void Update()
        {
            bool next = false, prev = false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) next = true;
                if (kb.backspaceKey.wasPressedThisFrame) prev = true;
                if (kb.aKey.wasPressedThisFrame) autoAdvance = !autoAdvance;
                for (int i = 0; i < Poses.Length && i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) { Select(i); return; }
            }

            if (_xr != null && (_xr.TriggerPressed || _xr.StickFlickedRight())) next = true;
            if (_xr != null && _xr.CancelPressed) prev = true;

            if (next) { Step(+1); return; }
            if (prev) { Step(-1); return; }

            if (!autoAdvance) return;
            _elapsed += Time.deltaTime;
            if (_elapsed >= secondsPerPose) Step(+1);
        }

        public void Step(int delta)
        {
            _index = (_index + delta + Poses.Length) % Poses.Length;
            Apply();
        }

        public void Select(int index)
        {
            if (index < 0 || index >= Poses.Length) return;
            _index = index;
            Apply();
        }

        void Apply()
        {
            _elapsed = 0f;
            var pose = Poses[_index];
            foreach (var a in animators)
            {
                if (a == null || a.runtimeAnimatorController == null) continue;
                // Setting a parameter the controller does not declare throws in the Editor, and a
                // scene may hold animators on something other than a master.
                if (!Has(a, "Gesture")) continue;
                a.SetInteger("Gesture", pose.Gesture);
                if (Has(a, "Speed")) a.SetFloat("Speed", pose.Speed);
                if (Has(a, "Talking")) a.SetBool("Talking", pose.Talking);
            }

            if (label != null)
                label.text = $"<size=140%><b>{pose.Name}</b></size>\n" +
                             $"{_index + 1} of {Poses.Length}   ·   auto {(autoAdvance ? "on" : "off")}\n" +
                             "<size=80%>space / trigger = next   ·   1-7 = pick   ·   A = auto</size>";
        }

        static bool Has(Animator a, string name)
        {
            foreach (var p in a.parameters)
                if (p.name == name) return true;
            return false;
        }
    }
}
