using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Plays a chapter's sound where it happened. Clips are synthesised once (<see cref="ChimeSynth"/>)
    /// and cached; the same clip replays when the piece appears in Your world.
    /// </summary>
    public static class ChimePlayer
    {
        static readonly Dictionary<ChapterSound, AudioClip> Clips = new Dictionary<ChapterSound, AudioClip>();
        static AudioClip _tick;

        public static AudioClip Clip(ChapterSound sound)
        {
            if (Clips.TryGetValue(sound, out var clip) && clip != null) return clip;
            var data = ChimeSynth.Render(sound);
            clip = AudioClip.Create("chime-" + sound, data.Length, 1, ChimeSynth.SampleRate, false);
            clip.SetData(data, 0);
            Clips[sound] = clip;
            return clip;
        }

        public static AudioClip TickClip()
        {
            if (_tick != null) return _tick;
            var data = ChimeSynth.Tick();
            _tick = AudioClip.Create("dial-tick", data.Length, 1, ChimeSynth.SampleRate, false);
            _tick.SetData(data, 0);
            return _tick;
        }

        static AudioClip _refuse;

        public static AudioClip RefuseClip()
        {
            if (_refuse != null) return _refuse;
            var data = ChimeSynth.Refuse();
            _refuse = AudioClip.Create("refuse", data.Length, 1, ChimeSynth.SampleRate, false);
            _refuse.SetData(data, 0);
            return _refuse;
        }

        /// <summary>The last thing played, for checks: what, where, when.</summary>
        public static string LastPlayed { get; private set; } = string.Empty;
        public static float LastPlayedAt { get; private set; } = -1f;

        public static void Play(ChapterSound sound, Vector3 at, float volume = 0.8f) => Play(Clip(sound), at, volume);

        public static void Play(AudioClip clip, Vector3 at, float volume)
        {
            LastPlayed = clip.name;
            LastPlayedAt = Time.time;
            var go = new GameObject("Chime " + clip.name);
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.spatialBlend = 0.6f;     // placed, but never lost when the head turns
            src.minDistance = 1f;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }
    }
}
