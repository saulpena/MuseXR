using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Worlds
{
    public enum WorldSet
    {
        /// <summary>Skylar's eight Marble worlds, full resolution: 1.9M–4.3M splats each,
        /// ~1.37 GB converted. World Labs' own docs say 2M+ crashes standalone VR, so this set
        /// is expected to struggle — it exists to measure how badly.</summary>
        Skylar,

        /// <summary>World Labs' five free example worlds at the 500k tier: ~116 MB converted,
        /// 12x lighter. This is the set that might actually hold framerate.</summary>
        Samples,
    }

    /// <summary>
    /// The worlds available to load, in two sets so the heavy and light options can be built
    /// and measured separately.
    /// </summary>
    public static class WorldCatalog
    {
        /// <summary>
        /// Skylar's worlds, with profiles ported from muse-infinity/config/worlds.js. Those
        /// numbers were tuned over 27 commits of playtesting — spawns re-derived after visitors
        /// started "in the sky", entries turned 90 and 180 degrees to face the right thing — so
        /// they are transcribed with the reasoning kept.
        ///
        /// Two of the ten worlds in that file are absent: bright-gallery-hall (its .spz is not
        /// in the release) and fantasy-realm (mesh-rendered, no .spz exists).
        /// </summary>
        public static readonly IReadOnlyList<WorldDefinition> Skylar = new List<WorldDefinition>
        {
            new WorldDefinition {
                key = "yellow-polka-dot-infinity-room", displayName = "Infinity Dot Room",
                worldScale = 2.0f, spawn = new Vector2(0.04f, 0.25f), groundY = 0.0f,
                yawDegrees = -90f, cameraFar = 200f, hasMeasuredSpawn = true,
            },
            new WorldDefinition {
                key = "van-gogh-inspired-gallery-interior", displayName = "Van Gogh Gallery",
                worldScale = 1.7f, spawn = new Vector2(1.79f, 0.30f), groundY = 0.0f,
                yawDegrees = 0f, cameraFar = 200f, hasMeasuredSpawn = true,
            },
            new WorldDefinition {
                // Ported spawn put the visitor immediately behind a column filling the view;
                // backed off along -Z and turned 25 degrees to clear it.
                key = "elegant-floral-palace-interior", displayName = "Floral Palace",
                worldScale = 1.7f, spawn = new Vector2(1.16f, -2.2f), groundY = 0.2f,
                yawDegrees = 25f, cameraFar = 200f, hasMeasuredSpawn = true,
            },
            new WorldDefinition {
                key = "mexican-courtyard-bedroom-fantasy", displayName = "Mexican Courtyard",
                worldScale = 1.7f, spawn = new Vector2(2.21f, -1.23f), groundY = 0.1f,
                yawDegrees = 0f, cameraFar = 400f, hasMeasuredSpawn = true,
            },
            new WorldDefinition {
                key = "grand-conservatory-with-lush-gardens", displayName = "Glass Conservatory",
                worldScale = 1.7f, spawn = new Vector2(-1.6f, -4.8f), groundY = 0.9f,
                yawDegrees = 0f, cameraFar = 400f, hasMeasuredSpawn = true,
            },
            new WorldDefinition {
                // "fix: water-garden entry view turned 180 to face the lily water"
                key = "enchanted-water-garden-sanctuary", displayName = "Water Garden",
                worldScale = 1.7f, spawn = new Vector2(0.8f, -19f), groundY = 1.1f,
                yawDegrees = 180f, cameraFar = 400f, hasMeasuredSpawn = true,
            },
            new WorldDefinition {
                // "fix: coastal-villa spawn re-derived from collider (visitor started in the sky)"
                key = "dreamlike-coastal-villa-gardens", displayName = "Coastal Villa",
                worldScale = 1.7f, spawn = new Vector2(-1.6f, -2.8f), groundY = 5.6f,
                yawDegrees = 0f, cameraFar = 400f, hasMeasuredSpawn = true,
            },
            new WorldDefinition {
                // "fix: sunlit-palace spawn advanced ~20m to the courtyard before the facade"
                key = "sunlit-palace-gardens", displayName = "Sunlit Gardens",
                worldScale = 1.7f, spawn = new Vector2(-9.5f, 15.5f), groundY = 3.0f,
                yawDegrees = 153.55f, cameraFar = 400f, hasMeasuredSpawn = true,
            },
        };

        /// <summary>
        /// World Labs' free example worlds at 500k. These carry no measured spawn — the CDN
        /// samples come without the semantics_metadata the API returns — so the cycler places
        /// the player from the asset's own bounds instead. Generating through the API would
        /// supply metric_scale_factor and ground_plane_offset and remove the guesswork.
        /// </summary>
        public static readonly IReadOnlyList<WorldDefinition> Samples = new List<WorldDefinition>
        {
            Sample("rustic_kitchen_with_natural_light_500k",   "Rustic Kitchen"),
            Sample("elegant_library_with_fireplace_500k",      "Library with Fireplace"),
            Sample("modern_house_with_lush_landscaping_500k",  "Modern House"),
            Sample("narrow_european_cobblestone_lane_500k",    "Cobblestone Lane"),
            Sample("warm_traditional_kitchen_interior_500k",   "Traditional Kitchen"),
        };

        static WorldDefinition Sample(string key, string name) => new WorldDefinition
        {
            key = key, displayName = name,
            worldScale = 1f, spawn = Vector2.zero, groundY = 0f, yawDegrees = 0f,
            cameraFar = 300f, hasMeasuredSpawn = false,
        };

        public static IReadOnlyList<WorldDefinition> Get(WorldSet set) =>
            set == WorldSet.Samples ? Samples : Skylar;

        public static int IndexOf(IReadOnlyList<WorldDefinition> list, WorldDefinition w)
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], w)) return i;
            return -1;
        }
    }
}
