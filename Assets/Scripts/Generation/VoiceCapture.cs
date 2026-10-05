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
        // What has been heard since StartRecording, as captured (interleaved if the device is stereo).
        readonly System.Collections.Generic.List<float> _heard = new System.Collections.Generic.List<float>();
        string[] _devices;

        /// <summary>
        /// The microphone is opened ONCE and left running in a looping buffer; a push-to-talk only marks
        /// where the utterance starts and ends in it. Saul, 4 Oct 2026: a hitch when the microphone starts
        /// and when it ends, which his other apps never have. That was <c>Microphone.Start</c> and
        /// <c>Microphone.End</c> on every turn: each opens or closes the audio device on the main thread.
        /// The buffer only has to outlast one frame's read, so a few seconds is plenty.
        /// </summary>
        public const int BufferSeconds = 4;

        /// <summary>Microphone.devices is a platform call (JNI on Android): asked once, not every turn.</summary>
        string[] Devices => _devices != null && _devices.Length > 0 ? _devices : (_devices = Microphone.devices);

        public bool HasMicrophone => Devices != null && Devices.Length > 0;

        /// <summary>
        /// The device in use, or the first one available before recording has started. On Android
        /// the name is often empty even when a device exists, so an empty string is reported as
        /// "default" rather than as nothing — "no name" and "no microphone" are different answers.
        /// </summary>
        public string DeviceNameOrFirst()
        {
            if (!string.IsNullOrEmpty(DeviceName)) return DeviceName;
            if (!HasMicrophone) return "none";
            var first = Devices[0];
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

        /// <summary>True once the microphone is open and running.</summary>
        public bool IsOpen => _clip != null && Microphone.IsRecording(DeviceName);

        /// <summary>
        /// Open the microphone into its looping buffer if it is not already. Called at Start, so the one
        /// device open is paid while the scene loads rather than when the visitor first speaks.
        /// </summary>
        public bool Open()
        {
            if (IsOpen) return true;
            if (!HasPermission() || !HasMicrophone) return false;
            DeviceName = Devices[0];
            _clip = Microphone.Start(DeviceName, true, BufferSeconds, captureSampleRate);
            _lastSamplePosition = 0;
            return _clip != null;
        }

        void Start() => Open();

        public bool StartRecording()
        {
            if (IsRecording) return true;
            if (!HasPermission()) { RequestPermission(); return false; }
            if (!Open()) return false;

            _startedAt = Time.realtimeSinceStartup;
            _lastSamplePosition = Microphone.GetPosition(DeviceName);   // from now: what came before is not this utterance
            _heard.Clear();
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
        /// Ends the utterance and returns it as 16 kHz mono WAV bytes, trimmed of the silence at
        /// each end. Returns null when nothing audible was captured — which is the honest answer
        /// when a microphone exists but hears nothing, and stops a silent clip being sent and
        /// billed. The microphone stays open.
        /// </summary>
        public byte[] StopRecording()
        {
            if (!IsRecording) return null;
            if (_clip != null) ReadNew();
            IsRecording = false;
            if (_clip == null || _heard.Count == 0) return null;

            var mono = WavEncoder.Downmix(_heard.ToArray(), _clip.channels);
            _heard.Clear();
            mono = WavEncoder.Resample(mono, _clip.frequency, WavEncoder.SpeechSampleRate);
            Level = WavEncoder.PeakLevel(mono);

            var trimmed = WavEncoder.TrimSilence(mono);
            if (trimmed.Length < WavEncoder.SpeechSampleRate / 4) return null;   // under 0.25 s of speech

            return WavEncoder.Encode(trimmed, 1, WavEncoder.SpeechSampleRate);
        }

        /// <summary>
        /// Hold-to-talk: the button was released, so end the utterance now with what was said,
        /// exactly as if silence had ended it. Does nothing when not recording (silence may already
        /// have ended it while the button was still held).
        /// </summary>
        public void FinishUtterance()
        {
            if (!IsRecording) return;
            var wav = StopRecording();
            UtteranceEnded?.Invoke(wav, SilenceDetector.StopReason.Released);
        }

        /// <summary>Drop the utterance. The microphone stays open.</summary>
        public void Abort()
        {
            if (!IsRecording) return;
            IsRecording = false;
            _heard.Clear();
        }

        public float RecordingSeconds => IsRecording ? Time.realtimeSinceStartup - _startedAt : 0f;

        /// <summary>
        /// The samples captured since the last read, added to the utterance and returned for the level
        /// and the silence detector. Only what is new, not the whole buffer: this runs every frame, and
        /// it is what makes the level a reading of RIGHT NOW. The buffer loops, so a read may wrap.
        /// </summary>
        float[] ReadNew()
        {
            var position = Microphone.GetPosition(DeviceName);
            var n = position - _lastSamplePosition;
            if (n < 0) n += _clip.samples;   // the loop wrapped since the last read
            if (n <= 0) return null;
            var samples = new float[n * _clip.channels];
            _clip.GetData(samples, _lastSamplePosition);   // reads round the end of a looping clip
            _lastSamplePosition = position;
            _heard.AddRange(samples);
            return samples;
        }

        void Update()
        {
            if (!IsRecording || _clip == null) return;

            var samples = ReadNew();
            if (samples == null) return;

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

        /// <summary>Closed only when the app is put away or the component goes, never between turns.</summary>
        void Close()
        {
            Abort();
            if (_clip != null) Microphone.End(DeviceName);
            _clip = null;
        }

        void OnApplicationPause(bool paused) { if (paused) Close(); }

        void OnDisable() => Close();
    }

    /// <summary>Speech in, text out. A seam, so the harness runs with no STT provider at all.</summary>
    public interface ISpeechToText
    {
        bool IsConfigured { get; }
        string Describe();
        Task<string> TranscribeAsync(byte[] wav);
    }
}
