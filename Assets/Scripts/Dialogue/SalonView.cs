using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using MusePico.Generation;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Puts each master's reading above that master's head, and drives the turn from a button.
    ///
    /// Deliberately not one shared subtitle. Three parallel readings printed into one box read as
    /// one long answer, which throws away the thing the lens work buys: the visitor should be able
    /// to see that three different voices looked at the same artwork and went three different ways.
    ///
    /// Controls: <b>Space / trigger</b> to speak, <b>Esc / B</b> to cancel. The utterance ends
    /// itself when you stop talking.
    /// </summary>
    public class SalonView : MonoBehaviour
    {
        [Header("Services")]
        public MuseumDialogue dialogue;

        [Header("Display")]
        public TMP_Text statusLabel;

        [Tooltip("One label per seated master, in the same order the roster seats them.")]
        public List<TMP_Text> masterLabels = new List<TMP_Text>();

        [Tooltip("Optional: typed questions, for testing without a microphone.")]
        public string typedQuestion = "What am I missing in this painting?";

        [Header("Unattended demo")]
        [Tooltip("Ask the typed question by itself shortly after start. " +
                 "On the emulator this is the only reliable way to see a full turn: neither the " +
                 "keyboard nor a gamepad is guaranteed to reach Unity there.")]
        public bool askOnStart;

        [Tooltip("Seconds to wait before the unattended question, so key resolution has finished.")]
        public float askOnStartDelay = 6f;

        readonly Dictionary<string, TMP_Text> _byMaster = new Dictionary<string, TMP_Text>();
        float _startedAt;
        bool _autoAsked;

        void Start()
        {
            _startedAt = Time.realtimeSinceStartup;
            _xr = new XrButtons();
            if (dialogue == null) return;
            dialogue.StatusChanged += OnStatus;
            dialogue.PerspectiveReady += OnPerspective;
        }

        XrButtons _xr;

        void OnDestroy()
        {
            _xr?.Dispose();
            if (dialogue == null) return;
            dialogue.StatusChanged -= OnStatus;
            dialogue.PerspectiveReady -= OnPerspective;
        }

        void Update()
        {
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) StartTurn();
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) AskTyped();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) dialogue?.Cancel();

            // XR devices, not Gamepad: a PICO/Quest controller is never a Unity Gamepad.
            if (_xr != null)
            {
                if (_xr.TriggerPressed) StartTurn();
                if (_xr.CancelPressed) dialogue?.Cancel();
            }

            if (askOnStart && !_autoAsked && Time.realtimeSinceStartup - _startedAt >= askOnStartDelay)
            {
                _autoAsked = true;
                AskTyped();
            }

            DrawLevelWhileRecording();
        }

        /// <summary>
        /// A live level meter, shown only while listening.
        ///
        /// It exists to answer one question that nothing else can: <b>is the microphone hearing
        /// anything?</b> On the PICO emulator this is not academic — the emulator zeroes out audio
        /// input unless host audio is explicitly enabled (<c>adb emu avd hostmicon</c>, or launching
        /// with <c>-allow-host-audio</c>), so the default state is a microphone that opens,
        /// records, and returns pure silence. From code that is indistinguishable from a broken
        /// integration. A bar that stays flat while you talk names it immediately.
        /// </summary>
        void DrawLevelWhileRecording()
        {
            var voice = dialogue != null ? dialogue.voice : null;
            if (statusLabel == null || voice == null || !voice.IsRecording) return;

            var filled = Mathf.Clamp(Mathf.RoundToInt(voice.Level * 16f), 0, 16);
            statusLabel.text =
                "Listening… speak, it ends itself\n" +
                "<size=80%>" + new string('█', filled) + new string('░', 16 - filled) +
                "  " + voice.Level.ToString("0.000") + "</size>\n" +
                "<size=70%><color=#7E8C89>" + DescribeInput() + "</color></size>";
        }

        /// <summary>
        /// What the visitor can actually do right now, and with what.
        ///
        /// Worth printing on the panel rather than assuming: on the emulator it is genuinely
        /// unknown whether a host keyboard reaches Unity, and whether the microphone passes real
        /// audio through or just opens and hears silence. Both look like "nothing happens".
        /// </summary>
        public string DescribeInput()
        {
            var parts = new List<string>();
            parts.Add(Keyboard.current != null ? "keyboard yes" : "keyboard no");
            // XR controllers, not gamepads. Reporting Gamepad here was doubly misleading: it is
            // always zero on a headset, and it is no longer what this scene reads.
            int xr = 0;
            foreach (var d in InputSystem.devices)
                if (d is UnityEngine.InputSystem.XR.XRController) xr++;
            parts.Add(xr > 0 ? xr + " XR controller" + (xr == 1 ? "" : "s") : "no XR controllers");

            var voice = dialogue != null ? dialogue.voice : null;
            if (voice == null) parts.Add("no VoiceCapture");
            else if (!voice.HasPermission()) parts.Add("mic not granted");
            else if (!voice.HasMicrophone) parts.Add("no mic device");
            else parts.Add("mic: " + voice.DeviceNameOrFirst());

            return string.Join(" · ", parts);
        }

        /// <summary>Speak a question out loud. The utterance ends itself.</summary>
        public void StartTurn()
        {
            ClearReadings();
            dialogue?.Listen();
        }

        /// <summary>Ask the typed question instead — for a desk, or a headset with no usable mic.</summary>
        public async void AskTyped()
        {
            if (dialogue == null) return;
            ClearReadings();
            await dialogue.AskAsync(typedQuestion);
        }

        void ClearReadings()
        {
            _byMaster.Clear();
            foreach (var label in masterLabels)
                if (label != null) label.text = "";
        }

        void OnStatus(string status)
        {
            if (statusLabel == null) return;
            statusLabel.text = status + "\n<size=70%><color=#7E8C89>" + DescribeInput() + "</color></size>";
        }

        void OnPerspective(Perspective perspective)
        {
            var label = LabelFor(perspective.speakerId);
            if (label == null) return;

            // The name is already on the plinth, so the label carries the reading and the effect
            // the master chose — which is the part that differs between them.
            label.text = "<size=120%>" + perspective.text + "</size>\n<color=#8FA3A0>" +
                         perspective.effect + "</color>";
        }

        /// <summary>
        /// Binds a speakerId to a label the first time it is seen, in arrival order.
        ///
        /// Matched by seat rather than by name so the scene does not have to hard-code which
        /// master stands where — swapping the invited masters in the Inspector then just works.
        /// </summary>
        TMP_Text LabelFor(string speakerId)
        {
            if (string.IsNullOrEmpty(speakerId)) return null;
            if (_byMaster.TryGetValue(speakerId, out var existing)) return existing;

            if (dialogue != null)
            {
                for (var i = 0; i < dialogue.Masters.Count && i < masterLabels.Count; i++)
                {
                    if (dialogue.Masters[i].id != speakerId) continue;
                    _byMaster[speakerId] = masterLabels[i];
                    return masterLabels[i];
                }
            }

            var next = _byMaster.Count;
            if (next >= masterLabels.Count) return null;
            _byMaster[speakerId] = masterLabels[next];
            return masterLabels[next];
        }
    }
}
