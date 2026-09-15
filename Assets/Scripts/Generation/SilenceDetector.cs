namespace MusePico.Generation
{
    /// <summary>
    /// Decides when someone has stopped talking.
    ///
    /// Ported from DuckyMayhem's <c>VoiceInputManager</c>, which is working code against a real
    /// account rather than a guess: mean absolute amplitude per frame, a silence timer that
    /// accumulates while it stays under threshold, and a hard ceiling on the whole utterance.
    /// The constants are its constants — 0.01 and 1.5 s — because they have been used and not
    /// complained about.
    ///
    /// Why it matters here more than in a game about ducks: this replaces hold-to-talk. In a
    /// headset, holding a grip button through a whole sentence is the sort of thing that seems
    /// fine until someone is also trying to point at a statue. Press once, speak, stop — and the
    /// utterance ends itself.
    ///
    /// <b>Mean</b> absolute amplitude, not peak. Peak is the right signal for a level meter, where
    /// you want it to jump. It is the wrong one for silence detection, because a single click or
    /// a knock on the desk pins it high and resets the timer.
    ///
    /// Pure arithmetic, no Unity types, so the decision is pinned by tests rather than found on a
    /// device.
    /// </summary>
    public struct SilenceDetector
    {
        /// <summary>Below this mean amplitude counts as silence.</summary>
        public float Threshold;

        /// <summary>Seconds of continuous silence before the utterance is considered finished.</summary>
        public float SilenceTimeout;

        /// <summary>Hard ceiling on one utterance, whatever the level does.</summary>
        public float MaxSeconds;

        float _silenceFor;
        float _elapsed;
        bool _heardAnything;

        public static SilenceDetector Default() => new SilenceDetector
        {
            Threshold = 0.01f,
            SilenceTimeout = 1.5f,
            MaxSeconds = 12f,
        };

        public float SilenceFor => _silenceFor;
        public float Elapsed => _elapsed;

        /// <summary>True once any audio above threshold has been heard at all.</summary>
        public bool HeardAnything => _heardAnything;

        public void Reset()
        {
            _silenceFor = 0f;
            _elapsed = 0f;
            _heardAnything = false;
        }

        /// <summary>Why the utterance ended, or <see cref="StopReason.Continue"/> to keep going.</summary>
        public enum StopReason
        {
            Continue,
            /// <summary>They finished speaking.</summary>
            Silence,
            /// <summary>The ceiling was hit — the audio is still usable.</summary>
            MaxDuration,
            /// <summary>Nothing above threshold for the whole leading window. Probably no input at all.</summary>
            NeverHeardAnything,
        }

        /// <summary>
        /// Advances by one frame. <paramref name="meanLevel"/> is the mean absolute amplitude of
        /// the samples captured since the previous call.
        /// </summary>
        public StopReason Update(float meanLevel, float deltaSeconds)
        {
            _elapsed += deltaSeconds;

            if (meanLevel >= Threshold)
            {
                _heardAnything = true;
                _silenceFor = 0f;
            }
            else
            {
                _silenceFor += deltaSeconds;
            }

            if (_elapsed >= MaxSeconds) return StopReason.MaxDuration;

            // Nothing heard, and long enough to be sure: on the emulator this is the difference
            // between "they are thinking" and "no audio is reaching us at all", and the caller
            // reports the two differently.
            if (!_heardAnything && _silenceFor >= SilenceTimeout * 2f) return StopReason.NeverHeardAnything;

            // Only ends on silence AFTER something was actually said, so the pause between
            // pressing the button and starting to speak does not end the utterance immediately.
            if (_heardAnything && _silenceFor >= SilenceTimeout) return StopReason.Silence;

            return StopReason.Continue;
        }
    }
}
