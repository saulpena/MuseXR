using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// The eight Marble worlds that actually ship, with their measured profiles.
    ///
    /// Ported verbatim from muse-infinity/config/worlds.js. Two of the ten worlds in that file
    /// are absent here, deliberately:
    ///   bright-gallery-hall  — its .spz and collider are not in the worlds-v1 release (404)
    ///   fantasy-realm-…      — mesh-rendered; it has no .spz at all
    ///
    /// Ordered as a walk: interiors first while the eye adjusts, then the big outdoor spaces.
    /// </summary>
    public static class WorldCatalog
    {
        public static readonly IReadOnlyList<WorldDefinition> All = new List<WorldDefinition>
        {
            new WorldDefinition {
                key = "yellow-polka-dot-infinity-room", displayName = "Infinity Dot Room",
                worldScale = 2.0f, spawn = new Vector2(0.04f, 0.25f), groundY = 0.0f,
                yawDegrees = -90f, cameraFar = 200f, enclosed = true,
            },
            new WorldDefinition {
                key = "van-gogh-inspired-gallery-interior", displayName = "Van Gogh Gallery",
                worldScale = 1.7f, spawn = new Vector2(1.79f, 0.30f), groundY = 0.0f,
                yawDegrees = 0f, cameraFar = 200f, enclosed = true,
            },
            new WorldDefinition {
                // Ported spawn (1.16, 0.78) put the visitor immediately behind a column, which
                // filled the view. Backed off along -Z and turned ~25 degrees to clear it.
                key = "elegant-floral-palace-interior", displayName = "Floral Palace",
                worldScale = 1.7f, spawn = new Vector2(1.16f, -2.2f), groundY = 0.2f,
                yawDegrees = 25f, cameraFar = 200f, enclosed = true,
            },
            new WorldDefinition {
                key = "mexican-courtyard-bedroom-fantasy", displayName = "Mexican Courtyard",
                worldScale = 1.7f, spawn = new Vector2(2.21f, -1.23f), groundY = 0.1f,
                yawDegrees = 0f, cameraFar = 400f, enclosed = false,
            },
            new WorldDefinition {
                key = "grand-conservatory-with-lush-gardens", displayName = "Glass Conservatory",
                worldScale = 1.7f, spawn = new Vector2(-1.6f, -4.8f), groundY = 0.9f,
                yawDegrees = 0f, cameraFar = 400f, enclosed = false,
            },
            new WorldDefinition {
                // "fix: water-garden entry view turned 180 to face the lily water"
                key = "enchanted-water-garden-sanctuary", displayName = "Water Garden",
                worldScale = 1.7f, spawn = new Vector2(0.8f, -19f), groundY = 1.1f,
                yawDegrees = 180f, cameraFar = 400f, enclosed = false,
            },
            new WorldDefinition {
                // "fix: coastal-villa spawn re-derived from collider (visitor started in the sky)"
                key = "dreamlike-coastal-villa-gardens", displayName = "Coastal Villa",
                worldScale = 1.7f, spawn = new Vector2(-1.6f, -2.8f), groundY = 5.6f,
                yawDegrees = 0f, cameraFar = 400f, enclosed = false,
            },
            new WorldDefinition {
                // "fix: sunlit-palace spawn advanced ~20m to the courtyard before the facade"
                key = "sunlit-palace-gardens", displayName = "Sunlit Gardens",
                worldScale = 1.7f, spawn = new Vector2(-9.5f, 15.5f), groundY = 3.0f,
                yawDegrees = 153.55f, cameraFar = 400f, enclosed = false,
            },
        };

        public static int IndexOf(WorldDefinition w)
        {
            for (int i = 0; i < All.Count; i++) if (ReferenceEquals(All[i], w)) return i;
            return -1;
        }

        public static WorldDefinition ByKey(string key)
        {
            foreach (var w in All) if (w.key == key) return w;
            return null;
        }
    }
}
