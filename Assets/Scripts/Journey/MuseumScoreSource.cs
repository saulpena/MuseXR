using UnityEngine;
using MusePico.Dialogue;

namespace MusePico.Journey
{
    /// <summary>
    /// Plays the background score and ducks it under the masters.
    ///
    /// Three looping sources, one per piece, all running all the time — crossfading between two
    /// live sources is what makes an act change a dissolve rather than a cut, and restarting a
    /// track from zero every time the visitor steps back a stage would be worse than no music.
    /// The decisions are <see cref="MuseumScore"/>'s; this only owns the AudioSources.
    ///
    /// <b>On by default, unlike hers.</b> Her score starts silent because a browser will not let
    /// audio play before a user gesture — a platform constraint, not a design choice. A headset has
    /// no such rule, and a museum that opens in silence is not what she built.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MuseumScoreSource : MonoBehaviour
    {
        [Tooltip("Mussorgsky's Promenade — the opening act and the finale.")]
        public AudioClip promenade;

        [Tooltip("Debussy's Clair de Lune — the gallery walk.")]
        public AudioClip clairDeLune;

        [Tooltip("Satie's Gymnopedie — the salon.")]
        public AudioClip gymnopedie;

        [Tooltip("Turn the whole score off without unwiring it.")]
        public bool enableMusic = true;

        [Tooltip("While this is playing, the score ducks. Usually the salon's voice source.")]
        public AudioSource duckUnder;

        AudioSource _promenade, _clair, _gymnopedie;
        string _active = MuseumScore.Promenade;

        void Awake()
        {
            _promenade = Make("Promenade", promenade);
            _clair = Make("Clair de Lune", clairDeLune);
            _gymnopedie = Make("Gymnopedie", gymnopedie);
        }

        AudioSource Make(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            // Flat, not positional: the score is not coming from a point in the room, and a
            // spatialised loop would swing around the visitor's head as they turned.
            source.spatialBlend = 0f;
            if (clip != null) source.Play();
            return source;
        }

        /// <summary>Follow the journey. Acts share a piece, so most stage changes do nothing.</summary>
        public void SetStage(Stage stage) => _active = MuseumScore.TrackFor(stage);

        void Update()
        {
            // The honest "is someone speaking" signal is the voice source itself, which is what her
            // narrator.onSpeaking hook reports.
            var ducked = duckUnder != null && duckUnder.isPlaying;
            var dt = Time.unscaledDeltaTime;

            Apply(_promenade, MuseumScore.Promenade, ducked, dt);
            Apply(_clair, MuseumScore.ClairDeLune, ducked, dt);
            Apply(_gymnopedie, MuseumScore.Gymnopedie, ducked, dt);
        }

        void Apply(AudioSource source, string track, bool ducked, float deltaTime)
        {
            if (source == null || source.clip == null) return;

            var target = MuseumScore.TargetVolume(enableMusic, track == _active, ducked);
            source.volume = MuseumScore.Approach(source.volume, target, deltaTime);

            // Pause a silent loop so it is not decoding audio nobody can hear, and resume it the
            // moment it is wanted again — from wherever it was, which is why an act returned to
            // does not start over.
            if (source.volume <= 0f && source.isPlaying) source.Pause();
            else if (source.volume > 0f && !source.isPlaying) source.UnPause();
        }
    }
}
