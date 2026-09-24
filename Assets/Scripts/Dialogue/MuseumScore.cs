using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Which piece plays when, and how loudly, ported from muse-infinity's
    /// <c>lib/backgroundMusic.js</c>.
    ///
    /// <b>Her casting is thematic, not decorative</b>, and the comment at the top of her file says
    /// why: Mussorgsky's <i>Promenade</i> IS music about walking through an art exhibition, so it
    /// scores the opening and the finale; Debussy scores the impressionist gallery walk; Satie
    /// scores the salon. All three are public-domain performances from Wikimedia Commons.
    ///
    /// <b>Acts share a track on purpose.</b> Stages 00-03 are one act, so stepping from the
    /// threshold to the question to the companions never restarts the music — which is the
    /// difference between a score and a jingle.
    ///
    /// Pure: the mapping and the fade arithmetic. The component owns the AudioSources.
    /// </summary>
    public static class MuseumScore
    {
        public const string Promenade = "promenade";
        public const string ClairDeLune = "clair-de-lune";
        public const string Gymnopedie = "gymnopedie";

        /// <summary>Background level. Hers: the room stays in front of the music.</summary>
        public const float FullVolume = 0.32f;

        /// <summary>
        /// While a master is speaking. Lowered, never stopped — a score that cuts out every time
        /// somebody talks reads as a bug, and cutting back in reads as worse.
        /// </summary>
        public const float DuckedVolume = 0.07f;

        /// <summary>Her exponential approach: 0.22 of the remaining gap every 60 ms tick.</summary>
        public const float FadeRatePerTick = 0.22f;

        public const float FadeTickSeconds = 0.06f;

        /// <summary>
        /// The piece for a stage. Unmapped stages take <see cref="Promenade"/>, as hers do — her
        /// map has no entry for the question or the curation either, and both fall through to it.
        /// </summary>
        public static string TrackFor(Stage stage)
        {
            switch (stage)
            {
                case Stage.WorldExploration:
                    return ClairDeLune;                 // Debussy walks the impressionist gallery

                case Stage.Summoning:
                case Stage.Roundtable:
                case Stage.Decision:
                    return Gymnopedie;                  // Satie holds the salon

                default:
                    return Promenade;                   // Mussorgsky opens and closes the exhibition
            }
        }

        /// <summary>
        /// How loud one track should be right now.
        ///
        /// Everything that is not the active piece targets silence, which is what makes the
        /// crossfade between acts a crossfade rather than a cut.
        /// </summary>
        public static float TargetVolume(bool enabled, bool isActiveTrack, bool ducked)
        {
            if (!enabled || !isActiveTrack) return 0f;
            return ducked ? DuckedVolume : FullVolume;
        }

        /// <summary>
        /// One step of the fade, frame-rate independent.
        ///
        /// Hers runs on a fixed 60 ms interval, so a flat 0.22 per tick is exact there. A Unity
        /// frame is not fixed, and applying a per-tick rate per-frame would make the fade run at
        /// whatever speed the headset happens to be managing — faster on a good frame, slower on a
        /// bad one, and audibly different between the Editor and a device. Converting through the
        /// exponent keeps her half-second duck a half-second duck at any frame rate.
        /// </summary>
        public static float Approach(float current, float target, float deltaTime)
        {
            if (deltaTime <= 0f) return current;

            var factor = 1f - Mathf.Pow(1f - FadeRatePerTick, deltaTime / FadeTickSeconds);
            var next = current + (target - current) * Mathf.Clamp01(factor);

            // Land exactly rather than approaching forever; a source left at 0.0001 never pauses.
            return Mathf.Abs(target - next) <= 0.005f ? target : next;
        }
    }
}
