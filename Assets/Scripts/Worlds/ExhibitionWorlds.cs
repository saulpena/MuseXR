using System.Collections.Generic;

namespace MuseXR.Worlds
{
    /// <summary>One exhibition scene of muse-infinity, as its navigator lists it.</summary>
    public sealed class ExhibitionScene
    {
        /// <summary>The 500k world key (WorldCatalog.Small / Original).</summary>
        public string worldKey;
        /// <summary>Her chapter line, verbatim: "02 / QUESTION".</summary>
        public string chapter;
        /// <summary>Her scene title, verbatim: "The Court of Light".</summary>
        public string title;
        /// <summary>File name of her scene thumbnail (muse-infinity/assets/scenes), without extension.</summary>
        public string thumbnail;

        // ---- where this world's door to the NEXT world stands --------------------------------
        // Chosen by looking at the capture from the spawn, not computed: a door belongs on a path,
        // under an arch, against a wall, and only an eye can find those in a Gaussian splat.

        /// <summary>Degrees clockwise from the direction the visitor faces on arrival.</summary>
        public float doorBearing;
        /// <summary>Metres from the spawn, on the floor.</summary>
        public float doorDistance;
        /// <summary>True: stand the door on the floor the world's collider reports there. False: on
        /// the world's groundY, for captures whose collider is missing the floor (the coastal
        /// terrace) or is in another frame (the dream world).</summary>
        public bool doorOnColliderFloor = true;
        /// <summary>Why the door stands there, so a later tuning pass knows what it was aiming at.</summary>
        public string doorReason;
    }

    /// <summary>
    /// The exhibition order of muse-infinity's <c>config/exhibitionScenes.js</c>: eight scenes walked
    /// with the navigator's arrows and dots, then the final dream world. Chapters and titles are hers,
    /// copied rather than paraphrased, so a door reads like the scene it opens.
    ///
    /// Two of her scenes are absent because no 500k export of their world exists:
    ///   06 / TRANSFORMATION  The Petal Transition Hall       (sunlit-palace-gardens)
    ///   08 / INFINITY        The Infinite Repetition Chamber (yellow-polka-dot-infinity-room)
    /// Their numbers are kept on the others, so the gaps are visible rather than renumbered away.
    ///
    /// Engine-free: no UnityEngine type, so the order is testable without a scene.
    /// </summary>
    public static class ExhibitionWorlds
    {
        static ExhibitionScene S(string key, string chapter, string title, string thumb,
                                 float bearing, float distance, bool onCollider, string reason) =>
            new ExhibitionScene
            {
                worldKey = key + WorldCatalog.SmallSuffix, chapter = chapter, title = title, thumbnail = thumb,
                doorBearing = bearing, doorDistance = distance, doorOnColliderFloor = onCollider, doorReason = reason,
            };

        public static readonly IReadOnlyList<ExhibitionScene> Scenes = new List<ExhibitionScene>
        {
            S("grand-conservatory-with-lush-gardens", "01 / ARRIVAL",    "The Threshold Conservatory",     "01-entrance-conservatory",
              0f, 7f, true, "on the path, framing the conservatory's central arch beyond it"),
            S("elegant-floral-palace-interior",       "02 / QUESTION",   "The Court of Light",             "02-court-of-light",
              30f, 5.2f, true, "against the gold-panelled wall of the rotunda, 5.9 m off"),
            S("enchanted-water-garden-sanctuary",     "03 / PERCEPTION", "The Garden of Water and Light",  "03-monet-water-and-light",
              175f, 5f, true, "on the pale stone path that winds along the pond, behind the arrival view; the rose arch ahead stands in the water"),
            S("dreamlike-coastal-villa-gardens",      "04 / INVENTION",  "The Sunset Frame Gallery",       "04-sunset-frame-gallery",
              0f, 11f, true, "in the villa's arched portico at the top of the steps, past the pool and the rose beds"),
            S("van-gogh-inspired-gallery-interior",   "05 / INTENSITY",  "The Studio of the Burning Sky",  "05-van-gogh-burning-sky",
              0f, 7f, true, "down the gallery, in the middle of the floor; at 10 m it was too small to find"),
            S("mexican-courtyard-bedroom-fantasy",    "07 / IDENTITY",   "The Courtyard of Living Memory", "07-frida-living-memory",
              0f, 7f, true, "on the brick path between the pools, before the cactus bed; at 12.5 m it was too small to find"),
            S("fantasy-realm-of-shimmering-spheres",  "09 / ANSWER",     "Your Dream World",               "09-final-dream-world",
              0f, 6f, false, "down the bubble tunnel; its collider is in another frame"),
        };

        /// <summary>The scene a world's door leads to: the next in her order, the last back to the first.</summary>
        public static int Next(int index) => Scenes.Count == 0 ? -1 : (index + 1) % Scenes.Count;

        /// <summary>Index of the scene showing <paramref name="worldKey"/>, or -1.</summary>
        public static int IndexOf(string worldKey)
        {
            for (int i = 0; i < Scenes.Count; i++) if (Scenes[i].worldKey == worldKey) return i;
            return -1;
        }
    }
}
