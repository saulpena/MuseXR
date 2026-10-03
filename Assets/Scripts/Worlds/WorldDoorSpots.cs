using System.Collections.Generic;

namespace MuseXR.Worlds
{
    /// <summary>Where a world's door to the next chapter stands, relative to its spawn.</summary>
    public sealed class DoorSpot
    {
        /// <summary>Degrees clockwise from the direction the visitor faces on arrival.</summary>
        public float bearing;
        /// <summary>Metres from the spawn, along the floor.</summary>
        public float distance;
        /// <summary>Stand the door on the floor the world's collider reports; false, on the visitor's floor.</summary>
        public bool onCollider = true;
        /// <summary>What the spot is, so a later tuning pass knows what it was aiming at.</summary>
        public string reason;
    }

    /// <summary>
    /// The journey's door spots, one per chapter world, keyed by the world's base key (no 500k
    /// suffix). Chosen by looking at each capture from its spawn, not computed: a door belongs on a
    /// path, before an arch, in a portico, and only an eye can find those in a Gaussian splat. The
    /// shared worlds carry the spots tuned and reviewed for WorldDoors.unity on 1 Oct 2026.
    ///
    /// A world with no entry gets no door: the last chapter, and any world nobody has placed yet.
    /// Engine-free, so the table is testable.
    /// </summary>
    public static class WorldDoorSpots
    {
        static DoorSpot S(float bearing, float distance, bool onCollider, string reason) =>
            new DoorSpot { bearing = bearing, distance = distance, onCollider = onCollider, reason = reason };

        static readonly Dictionary<string, DoorSpot> Spots = new Dictionary<string, DoorSpot>
        {
            // The journey's own chapter worlds, placed by looking from the landing (1 Oct 2026).
            ["empty-chinese-imperial-temple-hall"] = S(0f, 7f, true,
                "on the central aisle, framing the golden Buddha at the far end of the hall"),
            ["celestial-peach-blossom-paradise"] = S(198f, 6.5f, false,
                "at the foot of the red palace's steps, behind the arrival view; no collider for this world"),
            ["enchanted-palace-garden"] = S(0f, 8f, false,
                "on the central path between the pools, under the glass arcade; no collider for this world"),
            ["enchanted-water-garden-sanctuary"] = S(175f, 5f, true,
                "on the pale stone path that winds along the pond, behind the arrival view; the rose arch ahead stands in the water"),
            ["dreamlike-coastal-villa-gardens"] = S(0f, 11f, true,
                "in the villa's arched portico at the top of the steps, past the pool and the rose beds"),
            ["van-gogh-inspired-gallery-interior"] = S(0f, 7f, true,
                "down the gallery, in the middle of the floor; at 10 m it was too small to find"),
            ["mexican-courtyard-bedroom-fantasy"] = S(0f, 7f, true,
                "on the brick path between the pools, before the cactus bed"),
            ["grand-conservatory-with-lush-gardens"] = S(0f, 7f, true,
                "on the path, framing the conservatory's central arch beyond it"),
            ["elegant-floral-palace-interior"] = S(30f, 5.2f, true,
                "in the rotunda, by the gold-panelled wall"),
        };

        /// <summary>The door spot for a world key (with or without the 500k suffix), or null.</summary>
        public static DoorSpot For(string worldKey)
        {
            if (string.IsNullOrEmpty(worldKey)) return null;
            var key = worldKey.EndsWith(WorldCatalog.SmallSuffix)
                ? worldKey.Substring(0, worldKey.Length - WorldCatalog.SmallSuffix.Length) : worldKey;
            return Spots.TryGetValue(key, out var spot) ? spot : null;
        }

        /// <summary>Every world that has a door spot, by base key.</summary>
        public static IEnumerable<string> Keys => Spots.Keys;

        /// <summary>
        /// For tuning from the Editor: replace a world's spot until the next domain reload. The
        /// table above is the record; copy a spot that works back into it.
        /// </summary>
        public static void SetForSession(string baseKey, DoorSpot spot) => Spots[baseKey] = spot;
    }
}
