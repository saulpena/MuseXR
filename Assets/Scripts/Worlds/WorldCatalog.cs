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

        /// <summary>Skylar's worlds re-exported from Marble at the 500k tier, plus two that the
        /// full-res set never had: ~60 MB of .spz against 512 MB, ~230 MB converted against
        /// 1.37 GB. Same captures, same coordinate frame (see <see cref="Small"/>), so the
        /// playtested spawns carry over. This is the set to actually ship.</summary>
        Small,
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

        /// <summary>
        /// Skylar's captures re-exported at 500k, plus two the full-res set did not have.
        ///
        /// MEASURED, not assumed: the 500k bounds sit strictly INSIDE the full-res bounds on all
        /// six faces of all six shared worlds — every min rose, every max fell. That is decimation
        /// dropping sparse outlier splats, not a change of frame (a re-export in a different
        /// convention would reflect or offset, not shrink uniformly). So the spawns below are the
        /// full-res ones unchanged, and they mean the same thing. The interiors shrink 20-25%
        /// because their outliers are faint floaters; the outdoor bounds are pinned by sky and
        /// terrain that survive decimation, so those move under 1.2%.
        ///
        /// The two without a measured spawn are placed from the asset's own bounds:
        ///  - enchanted-palace-garden appears nowhere in muse-infinity/config/worlds.js. New.
        ///  - fantasy-realm-of-shimmering-spheres IS in worlds.js — it is the DEFAULT_WORLD_KEY —
        ///    but was mesh-rendered with `splatUrl: null`, which is exactly why WorldCatalog.Skylar
        ///    omits it. Its profile there (bounds x +/-2.08, z -6.83..9.99) is in METRIC space,
        ///    while this .spz measures +/-24 m: about 11x apart, which is not its 1.5061 metric
        ///    scale either. The profile does not port. Bounds placement instead.
        /// </summary>
        public static readonly IReadOnlyList<WorldDefinition> Small = new List<WorldDefinition>
        {
            Small_("van-gogh-inspired-gallery-interior", "Van Gogh Gallery",
                   1.7f, new Vector2(1.79f, 0.30f), 0.0f, 0f, 200f,
                   new Vector4(-2.47f, 10.0f, -14.62f, 13.73f)),
            Small_("elegant-floral-palace-interior", "Floral Palace",
                   1.7f, new Vector2(1.16f, -2.2f), 0.2f, 25f, 200f,
                   new Vector4(-8.12f, 12.73f, -10.4f, 11.44f)),
            Small_("mexican-courtyard-bedroom-fantasy", "Mexican Courtyard",
                   1.7f, new Vector2(2.21f, -1.23f), 0.1f, 0f, 400f,
                   new Vector4(-21.02f, 23.04f, -22.3f, 17.39f)),
            Small_("grand-conservatory-with-lush-gardens", "Glass Conservatory",
                   1.7f, new Vector2(-1.6f, -4.8f), 0.9f, 0f, 400f,
                   new Vector4(-47.15f, 41.28f, -56.41f, 39.15f)),
            Small_("enchanted-water-garden-sanctuary", "Water Garden",
                   1.7f, new Vector2(0.8f, -19f), 1.1f, 180f, 400f,
                   new Vector4(-6.36f, 23.3f, -41.25f, 16.21f)),
            Small_("dreamlike-coastal-villa-gardens", "Coastal Villa",
                   1.7f, new Vector2(-1.6f, -2.8f), 5.6f, 0f, 400f,
                   new Vector4(-30.63f, 21.07f, -39.06f, 33.44f)),

            // Measured 22 Sep 2026 by screenshot, not by heuristic. Both were previously
            // SmallUnmeasured, which fell through to the bounds fallback in WorldCycler.Place --
            // and that fallback backs the camera off 45% of the LARGER horizontal extent, which
            // for these two put the eye completely outside the capture:
            //   palace garden  -> (0.3, -2.2, -44.3)  : 44 m behind it, below the origin
            //   shimmering     -> (-0.9, 2.1, -24.2)  : just past the far z bound
            // Standing outside a capture looking in is what got reported as "the splats look
            // upside down". The worlds were never wrong; the fallback was.
            //
            // These exports are web-UI Marble downloads, already baked to the final world frame
            // (y-up, metres, ground ~= 0), so groundY 0 and a spawn at the origin are correct --
            // which matches the six playtested worlds above, whose groundY runs 0.0 to 1.1.
            // Yaw was chosen by comparing 0 and 180 in Assets/Screenshots/probe-*.png:
            //   palace garden  180 -> the glass dome, symmetric arches, pools both sides
            //                    0 -> the back of the room
            //   shimmering       0 -> a corridor with a vanishing point, somewhere to walk
            //                  180 -> a cluttered alcove
            Small_("enchanted-palace-garden", "Palace Garden",
                   1.7f, Vector2.zero, 0f, 180f, 400f),
            // NO walk bounds on purpose. worlds.js does carry a profile for this capture, but
            // those bounds (x +-2.08, z -6.83..9.99) are in MESH space: it shipped as render:"mesh"
            // with splatUrl:null, and this .spz measures about +-24 m — roughly 11x apart, and not
            // its metric.scale of 1.5061 either. Using them would describe a room that does not
            // exist. Falls back to splat bounds until someone measures the real walkable box.
            Small_("fantasy-realm-of-shimmering-spheres", "Shimmering Spheres",
                   1.7f, Vector2.zero, 0f, 0f, 200f),
            // The THRESHOLD world — stage 00's backdrop, so the visitor is not addressed in a void.
            //
            // Generated in Marble 1.1 Plus on 23 Sep 2026 from her own hero image,
            // `muse-infinity/assets/generated/muse-hero-conservatory-v3.png`, which is the still
            // that sits behind her web threshold. So both builds open on the same conservatory;
            // ours is simply standing in it. 500,000 splats, exported and converted at Medium like
            // every other world here.
            //
            // Bounds run 327 x 220 x 231 m, which is sky and terrain rather than room — the same
            // shape that threw the spawn heuristic for the outdoor captures. Hence a stated spawn
            // at the origin rather than the bounds fallback. `cameraFar` is wide because the
            // capture genuinely extends that far and the visitor is looking down a garden path.
            // yaw 180 -> the glass dome at the end of the path, arch overhead, wisteria either
            //            side: her hero image's own composition.
            //        0 -> the back of the terrace, trees and a balustrade.
            Small_("grand-conservatory-garden-path", "Threshold Conservatory",
                   1.7f, Vector2.zero, 0f, 180f, 400f),
            // The same capture, visibility-pruned for a visitor who does not walk: 339,578 of
            // 500,000 splats kept, every one of which adds at least 1/255 to some pixel from a
            // head anywhere within 0.3 m of the spawn at 1.2-1.9 m. Built by Tools/splat/prune.py
            // and checked on held-out views by validate.py. Same frame, so the same spawn.
            Small_("grand-conservatory-garden-path-cut", "Threshold Conservatory (pruned)",
                   1.7f, Vector2.zero, 0f, 180f, 400f),
            // Chapter 01's chosen world (see ExhibitionSpine). Downloaded by Saul 25 Sep 2026,
            // SPZ v2, 500,000 splats, sh 0. Spawn on the white stone plaza south of the lake
            // (26 Sep 2026): white splats form a flat surface at y 2.3 m (p10-p90 within 0.3 m)
            // from z -4 to -18 m; at z -11 m the view is open all round — lake, temple, waterfalls
            // and the props ahead over the balustrade, pavilions behind, a blossom tree to the
            // left. Chosen from offline renders (Tools/splat/splatvis). The origin, used before,
            // put the eye on the lake surface. Still no Marble collider, so the floor is the flat
            // fallback at groundY. yaw 0 faces the temple on its hill.
            // cameraFar 1000: the sky sits beyond 400 m and a 400 m clip left black holes in it.
            Small_("celestial-peach-blossom-paradise", "Celestial Peach Blossom Paradise",
                   1.7f, new Vector2(1f / 1.7f, -11f / 1.7f), 2.3f / 1.7f, 0f, 1000f),
            // Skylar's Scene 1 (27 Sep 2026), generated by Saul in Marble from our prompts, both
            // SPZ v2, 500,000 splats, sh 0, converted at Medium, each with a Marble collider in
            // Assets/Worlds/Colliders. The courtyard is where the opening stages stand (the
            // scene's homeWorldKey); the hall is chapter 01, where the monumental Buddha stands.
            // Spawns chosen by eye in Play Mode, 27 Sep (Assets/Screenshots/scene1/), heights from
            // the Marble colliders swept down the centre line.
            // Courtyard: paving at y 0 from z +10 to -8 m, the stair climbs 0.4 -> 4.2 m over
            // z -10..-20, the hall front stands at z ~ -21 with its central doors open. Spawn 6 m
            // back from the origin so the whole hall and its roof frame (12 m back smears the
            // paving underfoot). yaw 180 faces the hall; yaw 0 is the gate wall.
            // groundY +0.35 m over the collider's floor (0.25 m, then 0.10 m more after the Quest test): standing on the collider's height, the
            // visitor and the masters sat visibly sunk into the splat paving (Saul, Quest, 27 Sep).
            Small_("imperial-courtyard-forbidden-city", "Imperial Courtyard",
                   1.7f, new Vector2(0f, 6f / 1.7f), 0.35f / 1.7f, 180f, 400f),
            // Hall: floor y -0.6, ceiling 17-19 m, ~34 m between the side walls at mid-hall,
            // ~100 m long. A small altar at the +z end, the grand shrine at -z. The capture is only
            // sharp within ~15 m of its centre (z ~ +6): at 24 m the view looked through a brown
            // fog of edge splats, and behind a Buddha at z -8 the same fog hid its back. So the
            // 12 m Buddha stands at z 0 (spanning -5..+5) and the visitor arrives 16 m out,
            // facing it and the shrine beyond. Its props live under WorldProps_<this key>.
            // groundY -0.25: the collider's -0.6 plus the same 0.35 m lift as the courtyard.
            Small_("empty-chinese-imperial-temple-hall", "Hall of the Great Buddha",
                   1.7f, new Vector2(0f, 16f / 1.7f), -0.25f / 1.7f, 180f, 250f),
        };

        /// <summary>
        /// The 500k re-exports are converted alongside the full-res assets, and Addressables takes
        /// its address from the file name — so six of the eight would collide with the full-res
        /// asset of the same capture. The suffix is what keeps them apart, and it lives here so a
        /// caller never has to remember it.
        /// </summary>
        public const string SmallSuffix = "-500k";

        /// <summary>
        /// <paramref name="walk"/> is her playtested `profile.bounds` as (minX, maxX, minZ, maxZ),
        /// pre-scale. It is the room, not the capture: see WorldDefinition.walkBounds for why the
        /// two differ by tens of metres.
        /// </summary>
        static WorldDefinition Small_(string baseKey, string name, float scale, Vector2 spawn,
                                      float groundY, float yaw, float far,
                                      Vector4 walk = default) => new WorldDefinition
        {
            key = baseKey + SmallSuffix, displayName = name,
            worldScale = scale, spawn = spawn, groundY = groundY, yawDegrees = yaw,
            cameraFar = far, hasMeasuredSpawn = true, walkBounds = walk,
        };

        static WorldDefinition SmallUnmeasured(string baseKey, string name, float far) =>
            new WorldDefinition
            {
                key = baseKey + SmallSuffix, displayName = name,
                worldScale = 1f, spawn = Vector2.zero, groundY = 0f, yawDegrees = 0f,
                cameraFar = far, hasMeasuredSpawn = false,
            };

        public static IReadOnlyList<WorldDefinition> Get(WorldSet set)
        {
            switch (set)
            {
                case WorldSet.Samples: return Samples;
                case WorldSet.Small:   return Small;
                default:               return Skylar;
            }
        }

        public static int IndexOf(IReadOnlyList<WorldDefinition> list, WorldDefinition w)
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], w)) return i;
            return -1;
        }
    }
}
