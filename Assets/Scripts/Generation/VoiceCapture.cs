using System;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace MusePico.Generation
{
    /// <summary>
    /// Push-to-talk capture, reduced to what a speech recogniser wants.
    ///
    /// Measured on PICO Emulator 6.1.0: the device declares <c>android.hardware.microphone</c> and
    /// audio_flinger reports live 48 kHz RECORD threads, so <c>Microphone</c> is expected to work
    /// there rather than being a hardware-only path. Whether the emulator passes the HOST's
    /// microphone through is a separate question, and the one <see cref="Level"/> exists to
    /// answer: recording silence and having no microphone at all look identical from code, and
    /// only a level meter separates them.
    ///
    /// Android needs RECORD_AUDIO granted at runtime, not just declared, and PICO shows its own
    /// consent dialog for it. Asking at <see cref="RequestPermission"/> rather than at the moment
    /// of recording means the dialog does not land in the middle of a push-to-talk.
    /// </summary>
    public class VoiceCapture : MonoBehaviour
    {
        [Tooltip("Longest utterance kept. Speech services charge by audio length.")]
        public int maxSeconds = 12;

        [Tooltip("Capture rate. Downsampled to 16 kHz before sending either way.")]
        public int captureSampleRate = 16000;

        [Header("Voice activity")]
        [Tooltip("End the utterance when they stop speaking, instead of requiring the button held down.")]
        public bool autoStopOnSilence = true;

        [Tooltip("Mean amplitude below this counts as silence. 0.01 is DuckyMayhem's proven value.")]
        public float silenceThreshold = 0.01f;

        [Tooltip("Seconds of silence that end an utterance, once something has been said.")]
        public float silenceTimeout = 1.5f;

        public bool IsRecording { get; private set; }

        /// <summary>Peak level of the last read, 0..1. Drives the meter — peak is what should jump.</summary>
        public float Level { get; private set; }

        public string DeviceName { get; private set; }

        /// <summary>
        /// Fires when the utterance ended by itself. The payload is the WAV, or null when nothing
        /// audible was captured — the caller reports those two very differently.
        /// </summary>
        public event Action<byte[], SilenceDetector.StopReason> UtteranceEnded;

        AudioClip _clip;
        float _startedAt;
        int _lastSamplePosition;
        SilenceDetector _silence;

        public bool HasMicrophone => Microphone.devices != null && Microphone.devices.Length > 0;

        /// <summary>
        /// The device in use, or the first one available before recording has started. On Android
        /// the name is often empty even when a device exists, so an empty string is reported as
        /// "default" rather than as nothing — "no name" and "no microphone" are different answers.
        /// </summary>
        public string DeviceNameOrFirst()
        {
            if (!string.IsNullOrEmpty(DeviceName)) return DeviceName;
            if (!HasMicrophone) return "none";
            var first = Microphone.devices[0];
            return string.IsNullOrEmpty(first) ? "default" : first;
        }

        /// <summary>What the platform reports, verbatim — the first thing to read when nothing works.</summary>
        public string Describe()
        {
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
                return "No microphone devices reported. On Android, check RECORD_AUDIO is granted.";

            var list = string.Join(", ", devices);
            return devices.Length + " device(s): " + list;
        }

        public bool HasPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }

        /// <summary>Prompts for RECORD_AUDIO if it is not already granted. Safe to call repeatedly.</summary>
        public void RequestPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                Permission.RequestUserPermission(Permission.Microphone);
#endif
        }

        public bool StartRecording()
        {
            if (IsRecording) return true;
            if (!HasPermission()) { RequestPermission(); return false; }
            if (!HasMicrophone) return false;

            DeviceName = Microphone.devices[0];
            _clip = Microphone.Start(DeviceName, false, Mathf.Max(1, maxSeconds), captureSampleRate);
            if (_clip == null) return false;

            _startedAt = Time.realtimeSinceStartup;
            _lastSamplePosition = 0;
            _silence = new SilenceDetector
            {
                Threshold = silenceThreshold,
                SilenceTimeout = silenceTimeout,
                MaxSeconds = maxSeconds,
            };
            IsRecording = true;
            return true;
        }

        /// <summary>
        /// Stops and returns the utterance as 16 kHz mono WAV bytes, trimmed of the silence at
        /// each end. Returns null when nothing audible was captured — which is the honest answer
        /// when a microphone exists but hears nothing, and stops a silent clip being sent and
        /// billed.
        /// </summary>
        public byte[] StopRecording()
        {
            if (!IsRecording) return null;

            var position = Microphone.GetPosition(DeviceName);
            Microphone.End(DeviceName);
            IsRecording = false;

            if (_clip == null || position <= 0) return null;

            var raw = new float[position * _clip.channels];
            _clip.GetData(raw, 0);

            var mono = WavEncoder.Downmix(raw, _clip.channels);
            mono = WavEncoder.Resample(mono, _clip.frequency, WavEncoder.SpeechSampleRate);
            Level = WavEncoder.PeakLevel(mono);

            var trimmed = WavEncoder.TrimSilence(mono);
            if (trimmed.Length < WavEncoder.SpeechSampleRate / 4) return null;   // under 0.25 s of speech

            return WavEncoder.Encode(trimmed, 1, WavEncoder.SpeechSampleRate);
        }

        public void Abort()
        {
            if (!IsRecording) return;
            Microphone.End(DeviceName);
            IsRecording = false;
        }

        public float RecordingSeconds => IsRecording ? Time.realtimeSinceStartup - _startedAt : 0f;

        void Update()
        {
            if (!IsRecording || _clip == null) return;

            // Only the samples captured since the last frame, not the whole buffer: this runs
            // every frame, and copying twelve seconds of audio per frame would cost more than the
            // generation it is waiting for. It is also what makes the level a reading of RIGHT
            // NOW rather than an average over the whole utterance, which would never fall back
            // below the silence threshold once someone had spoken.
            var position = Microphone.GetPosition(DeviceName);
            if (position < _lastSamplePosition) _lastSamplePosition = 0;   // the ring buffer wrapped
            var newSamples = position - _lastSamplePosition;
            if (newSamples <= 0) return;

            var samples = new float[newSamples * _clip.channels];
            _clip.GetData(samples, _lastSamplePosition);
            _lastSamplePosition = position;

            Level = WavEncoder.PeakLevel(samples);

            if (!autoStopOnSilence)
            {
                if (RecordingSeconds >= maxSeconds) Abort();
                return;
            }

            var reason = _silence.Update(WavEncoder.MeanLevel(samples), Time.deltaTime);
            if (reason == SilenceDetector.StopReason.Continue) return;

            var wav = StopRecording();
            UtteranceEnded?.Invoke(wav, reason);
        }

        void OnDisable() => Abort();
    }

    /// <summary>Speech in, text out. A seam, so the harness runs with no STT provider at all.</summary>
    public interface ISpeechToText
    {
        bool IsConfigured { get; }
        string Describe();
        Task<string> TranscribeAsync(byte[] wav);
    }
}
