using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MusePico.Generation
{
    [Serializable]
    public class GenerationPreset
    {
        public string label;
        public SubjectKind kind = SubjectKind.Artist;
        [TextArea(1, 3)] public string input;
        public VrBudgetTier tier = VrBudgetTier.Exhibit;
        public bool withTexture = true;
    }

    /// <summary>
    /// The bench for runtime generation: pick a pipeline, run it, and read what it cost.
    ///
    /// Deliberately no world-space Canvas. A canvas needs the XR UI raycasting stack wired up
    /// before a single button can be pressed, and every one of those parts is another thing that
    /// can be the reason nothing happens on the emulator. This drives off raw controller buttons
    /// and 3D text, so the only things between a press and a generation are the pipeline itself.
    ///
    /// Controls, on a headset and at a desk:
    ///
    ///   Trigger  / Space  — generate the selected preset
    ///   Thumbstick / Tab  — next preset
    ///   Grip     / V      — hold to talk, release to generate what you said
    ///   B / Escape        — cancel the generation in flight
    ///
    /// Everything it reports is measured on the spot: elapsed seconds, credits actually consumed,
    /// triangles, materials, texture memory, and the verdict against a headset budget. That is
    /// the answer to "is generated output performant enough for VR" — per generation, from this
    /// account, rather than from a blog post.
    /// </summary>
    public class RuntimeGenerationHarness : MonoBehaviour
    {
        [Header("Services")]
        public RuntimeGenerator generator;
        public VoiceCapture voice;

        [Header("Display")]
        public TMP_Text titleLabel;
        public TMP_Text statusLabel;
        public TMP_Text reportLabel;

        [Tooltip("Scaled along X to show progress, 0..1.")]
        public Transform progressBar;

        [Header("Placement")]
        [Tooltip("Where the generated model is parented. Previous results are cleared first.")]
        public Transform plinthAnchor;

        [Tooltip("Keep earlier results instead of replacing them. Watch the frame rate if you do.")]
        public bool keepPrevious;

        [Header("Speech")]
        [Tooltip("Leave empty to use canned transcripts — that still exercises the whole microphone path.")]
        public bool useOpenAiSpeech = true;

        [Header("Pipelines to try")]
        public List<GenerationPreset> presets = new List<GenerationPreset>();

        int _selected;
        GameObject _current;
        ISpeechToText _speech;
        string _lastTranscript = "";
        string _voiceNote = "";

        public GenerationPreset Selected =>
            presets != null && presets.Count > 0 ? presets[_selected % presets.Count] : null;

        void Awake()
        {
            _speech = useOpenAiSpeech
                ? (ISpeechToText)new OpenAiSpeechToText()
                : new CannedSpeechToText(
                    "a marble bust of Socrates",
                    "a bronze statue of Frida Kahlo",
                    "an ornate wooden easel");

            if (presets == null || presets.Count == 0) presets = DefaultPresets();
            _xr = new XrButtons();
        }

        void Start()
        {
            if (generator != null) generator.ProgressChanged += OnProgress;
            if (voice != null)
            {
                voice.RequestPermission();
                voice.UtteranceEnded += OnUtteranceEnded;
            }
            Redraw();
        }

        void OnDestroy()
        {
            if (generator != null) generator.ProgressChanged -= OnProgress;
            if (voice != null) voice.UtteranceEnded -= OnUtteranceEnded;
            _xr?.Dispose();
        }

        void Update()
        {
            ReadInput();

            if (voice != null && voice.IsRecording && progressBar != null)
            {
                // While recording the bar becomes a level meter. On the emulator this is the only
                // way to tell a microphone that hears nothing from one that is not there.
                SetBar(voice.Level);
            }

            Redraw();
        }

        void ReadInput()
        {
            var keyboard = Keyboard.current;

            var next = (keyboard != null && keyboard.tabKey.wasPressedThisFrame) || ThumbstickFlicked();
            if (next) { _selected = (_selected + 1) % Mathf.Max(1, presets.Count); }

            var go = (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) || TriggerPressed();
            if (go) GenerateSelected();

            var cancel = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || CancelPressed();
            if (cancel && generator != null) generator.Cancel();

            var talkDown = (keyboard != null && keyboard.vKey.wasPressedThisFrame) || GripPressed();
            var talkUp = (keyboard != null && keyboard.vKey.wasReleasedThisFrame) || GripReleased();

            if (talkDown) StartTalking();
            if (talkUp) StopTalkingAndGenerate();
        }

        // Controller input goes through XrButtons (XR devices). It used to read Gamepad.all,
        // which never reports a PICO or Quest controller — see XrButtons for the measurement.
        XrButtons _xr;

        bool TriggerPressed()   { return _xr != null && _xr.TriggerPressed; }
        bool CancelPressed()    { return _xr != null && _xr.CancelPressed; }
        bool GripPressed()      { return _xr != null && _xr.GripPressed; }
        bool GripReleased()     { return _xr != null && _xr.GripReleased; }
        bool ThumbstickFlicked(){ return _xr != null && _xr.StickFlickedRight(); }

        public async void GenerateSelected()
        {
            var preset = Selected;
            if (preset == null || generator == null || generator.IsBusy) return;

            generator.tier = preset.tier;
            generator.withTexture = preset.withTexture;
            await RunAsync(preset.kind, preset.input);
        }

        void StartTalking()
        {
            if (voice == null || generator == null || generator.IsBusy) return;

            _voiceNote = voice.StartRecording()
                ? (voice.autoStopOnSilence ? "Listening… just speak, it ends itself" : "Listening… release to send")
                : "Microphone unavailable. " + voice.Describe();
        }

        void StopTalkingAndGenerate()
        {
            if (voice == null || !voice.IsRecording) return;

            // With voice activity detection the utterance ends itself, so letting go of the
            // button mid-sentence must not cut it off. Releasing early is only a manual override
            // when detection is turned off.
            if (voice.autoStopOnSilence) return;

            OnUtteranceEnded(voice.StopRecording(), SilenceDetector.StopReason.Silence);
        }

        void OnUtteranceEnded(byte[] wav, SilenceDetector.StopReason reason)
        {
            if (reason == SilenceDetector.StopReason.NeverHeardAnything)
            {
                // The one failure worth naming precisely: a microphone that exists and records
                // nothing is indistinguishable from a missing one unless you say so.
                _voiceNote = "Nothing heard at all in " + voice.maxSeconds + "s. The microphone is " +
                             "open (" + voice.Describe() + ") but no audio is reaching it — on the " +
                             "emulator that usually means host audio is not being passed through.";
                return;
            }

            if (wav == null)
            {
                _voiceNote = "Too short to send. Peak level reached " + voice.Level.ToString("0.000") + ".";
                return;
            }

            TranscribeAndGenerate(wav, reason);
        }

        async void TranscribeAndGenerate(byte[] wav, SilenceDetector.StopReason reason)
        {
            _voiceNote = "Transcribing " + (wav.Length / 1024) + " KB" +
                         (reason == SilenceDetector.StopReason.MaxDuration ? " (hit the time limit)" : "") + "…";
            var text = await _speech.TranscribeAsync(wav);

            if (string.IsNullOrWhiteSpace(text))
            {
                _voiceNote = "No transcript. " + _speech.Describe();
                return;
            }

            _lastTranscript = text;
            _voiceNote = "Heard: “" + text + "”";

            if (GenerationSubject.LooksLikeAnEnvironment(text))
            {
                // Worth saying before spending: Tripo returns one object. A place comes back as a
                // shoebox diorama and bills the same as a good result. Whole environments are the
                // Gaussian-splat road's job.
                _voiceNote += "  — that sounds like a place. Tripo makes objects; generating it " +
                              "anyway as a single piece.";
            }

            var preset = Selected;
            if (preset != null)
            {
                generator.tier = preset.tier;
                generator.withTexture = preset.withTexture;
            }
            await RunAsync(SubjectKind.FreeForm, text);
        }

        async System.Threading.Tasks.Task RunAsync(SubjectKind kind, string input)
        {
            if (!keepPrevious && _current != null) Destroy(_current);

            var outcome = await generator.GenerateAsync(kind, input);

            if (outcome.Success)
            {
                _current = outcome.Root;
                if (plinthAnchor != null && _current != null)
                {
                    var local = _current.transform.localPosition;
                    _current.transform.SetParent(plinthAnchor, false);
                    _current.transform.localPosition = local;
                }
            }
        }

        void OnProgress(GenerationProgress progress)
        {
            if (progressBar != null && (voice == null || !voice.IsRecording)) SetBar(progress.Fraction);
        }

        void SetBar(float fraction)
        {
            var scale = progressBar.localScale;
            scale.x = Mathf.Clamp01(fraction);
            progressBar.localScale = scale;
        }

        void Redraw()
        {
            var preset = Selected;

            if (titleLabel != null)
            {
                titleLabel.text = preset == null
                    ? "No pipelines configured"
                    : "<size=130%>" + preset.label + "</size>\n" + preset.kind + " · " + preset.tier +
                      (preset.withTexture ? " · textured" : " · untextured");
            }

            if (statusLabel != null)
            {
                var progress = generator != null ? generator.Progress : default;
                var budget = generator != null ? generator.Budget : VrAssetBudget.For(VrBudgetTier.Exhibit);
                var seconds = budget.EstimatedSeconds;

                var lines = new List<string>
                {
                    progress.Headline(),
                    "~" + budget.EstimatedCredits(false) + " credits · ~" + seconds.fast + "-" + seconds.slow +
                    "s · " + budget.FaceLimit.ToString("N0") + " tri cap",
                };

                if (!string.IsNullOrEmpty(_voiceNote)) lines.Add(_voiceNote);
                if (voice != null && voice.IsRecording)
                    lines.Add("level " + voice.Level.ToString("0.00") + "  " + Meter(voice.Level));

                statusLabel.text = string.Join("\n", lines);
            }

            if (reportLabel != null)
            {
                var progress = generator != null ? generator.Progress : default;
                reportLabel.text = progress.Phase == GenerationPhase.Done && !string.IsNullOrEmpty(progress.Message)
                    ? progress.Message
                    : "Trigger/Space generate · Stick/Tab next · Grip/V talk · B/Esc cancel";
            }
        }

        static string Meter(float level)
        {
            var filled = Mathf.Clamp(Mathf.RoundToInt(level * 12f), 0, 12);
            return new string('#', filled) + new string('.', 12 - filled);
        }

        /// <summary>
        /// The pipelines worth comparing, and why each is in the list. Together they answer the
        /// three questions that decide whether runtime generation is viable: how slow is the good
        /// setting, how fast is the cheap one, and what does a wrong-shaped request produce.
        /// </summary>
        public static List<GenerationPreset> DefaultPresets() => new List<GenerationPreset>
        {
            new GenerationPreset
            {
                label = "Artist — textured",
                kind = SubjectKind.Artist, input = "Claude Monet",
                tier = VrBudgetTier.Exhibit, withTexture = true,
            },
            new GenerationPreset
            {
                label = "Artist — untextured (fast)",
                kind = SubjectKind.Artist, input = "Vincent van Gogh",
                tier = VrBudgetTier.Exhibit, withTexture = false,
            },
            new GenerationPreset
            {
                label = "Prop — cheapest tier",
                kind = SubjectKind.Prop, input = "an ornate wooden easel",
                tier = VrBudgetTier.Background, withTexture = true,
            },
            new GenerationPreset
            {
                label = "Artwork — hero budget",
                kind = SubjectKind.Artwork, input = "a bronze dancer",
                tier = VrBudgetTier.Hero, withTexture = true,
            },
            new GenerationPreset
            {
                label = "Architecture — one element",
                kind = SubjectKind.EnvironmentPiece, input = "a carved stone archway",
                tier = VrBudgetTier.Hero, withTexture = true,
            },
        };
    }
}
