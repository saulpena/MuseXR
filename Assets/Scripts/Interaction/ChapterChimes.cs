using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her confirm sound per chapter (MUSE-VR-design 4.3: "bronze bell, stone chime, wood, water; it
    /// replays when that piece appears in the personal world"): the moment a chapter's choice reaches the
    /// journey record its sound plays once. Watching the record rather than each chapter's code keeps one
    /// place for the four sounds, and Your world plays the same clips back (YourWorldEnding).
    /// </summary>
    public sealed class ChapterChimes : MonoBehaviour
    {
        static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        /// <summary>The chapter's sound: palace a bronze bell, grotto a stone chime, vangogh wood, monet water.</summary>
        public static AudioClip Clip(string chapter)
        {
            if (Clips.TryGetValue(chapter, out var c) && c != null) return c;
            c = chapter switch
            {
                "palace" => YourWorldEnding.Chime(392f, 4.0f, 2.4f, 0.55f),
                "grotto" => YourWorldEnding.Chime(1046f, 2.5f, 1.2f, 0.4f),
                "vangogh" => YourWorldEnding.Chime(196f, 7f, 0.5f, 0.6f),
                _ => YourWorldEnding.Chime(660f, 1.6f, 3f, 0.35f),
            };
            Clips[chapter] = c;
            return c;
        }

        readonly HashSet<string> _played = new HashSet<string>();
        AudioSource _audio;

        void Start()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.volume = 0.55f;
            // Whatever was already kept before this scene (or this component) started does not ring again.
            var r = JourneyMemory.Record;
            if (r.Palace != null) _played.Add("palace");
            if (r.Grotto != null) _played.Add("grotto");
            if (r.VanGogh != null) _played.Add("vangogh");
            if (r.Monet != null) _played.Add("monet");
        }

        void Update()
        {
            var r = JourneyMemory.Record;
            Ring("palace", r.Palace != null);
            Ring("grotto", r.Grotto != null);
            Ring("vangogh", r.VanGogh != null);
            Ring("monet", r.Monet != null);
        }

        void Ring(string chapter, bool kept)
        {
            if (!kept || !_played.Add(chapter)) return;
            _audio.PlayOneShot(Clip(chapter));
            Debug.Log("[Chime] " + chapter + " kept");
        }
    }
}
