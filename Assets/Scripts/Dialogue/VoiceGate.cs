using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// One master's voice at a time (Saul, 5 Oct, headset: "sometimes the audio of the masters plays twice like an
    /// echo"). Every master voice starts through <see cref="Play"/>, which stops whichever master voice is still
    /// sounding first - whatever played it: the shared voice, a figure's own source at the Gate, the salon speaker.
    /// </summary>
    public static class VoiceGate
    {
        static AudioSource _current, _shared;

        /// <summary>Play <paramref name="clip"/> on <paramref name="source"/>, after silencing any other master voice.</summary>
        public static void Play(AudioSource source, AudioClip clip)
        {
            if (source == null || clip == null) return;
            if (_current != null && _current != source && _current.isPlaying) _current.Stop();
            source.clip = clip;
            source.Play();
            _current = source;
        }

        /// <summary>Play a voice with no source of its own (it used to be a fire-and-forget one-shot nobody could stop).</summary>
        public static void Play(AudioClip clip, float volume = 0.9f)
        {
            if (clip == null) return;
            if (_shared == null)
            {
                var go = new GameObject("Voice Gate") { hideFlags = HideFlags.DontSave };
                Object.DontDestroyOnLoad(go);
                _shared = go.AddComponent<AudioSource>();
                _shared.playOnAwake = false; _shared.spatialBlend = 0f;
            }
            _shared.volume = volume;
            Play(_shared, clip);
        }

        /// <summary>Silence whatever master voice is sounding.</summary>
        public static void StopAll()
        {
            if (_current != null) _current.Stop();
        }

        /// <summary>A master voice is sounding now.</summary>
        public static bool Speaking => _current != null && _current.isPlaying;
    }
}
