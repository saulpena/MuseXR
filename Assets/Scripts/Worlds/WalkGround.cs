using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>
    /// The pure arithmetic behind standing on a Marble collider. No Unity scene, no physics —
    /// raycasting is the caller's job; this decides what to DO with the heights that came back.
    ///
    /// Ported from muse-infinity's <c>lib/museum3d.js:groundAt</c>. Every rule in
    /// <see cref="PickGroundHeight"/> records a specific acceptance failure in that project and
    /// the comments are kept because the reasoning is the asset, not the arithmetic.
    /// </summary>
    public static class WalkGround
    {
        /// <summary>
        /// Real steps and platforms never exceed about this, so a hit higher than the reference
        /// floor by more than this is a mezzanine, an arch, or ceiling junk — never somewhere a
        /// visitor could be standing. Capped in absolute metres rather than scaled: at
        /// worldScale 1.7 the original's <c>1.2 * ws</c> window was ~2.04 m, wide enough for a
        /// collider fragment above the real floor to win and lift the visitor off the ground.
        /// </summary>
        public const float MaxStepUp = 0.6f;

        /// <summary>How far BELOW the reference floor a hit may still be real ground. Wide,
        /// because interiors are genuinely uneven and a sunken room is walkable.</summary>
        public const float MaxDropBelow = 2.0f;

        /// <summary>
        /// Choose the height to stand at, given every downward hit under the visitor.
        ///
        /// Three tiers, in order, and the later two exist because the first one alone put
        /// visitors inside hillsides:
        ///
        /// 1. The HIGHEST hit inside an asymmetric window around <paramref name="referenceY"/> —
        ///    a small up-window so a doorway arch can never win, a wide down-window so uneven
        ///    floors still read. Tuned for flat interiors, which is most of the museum.
        /// 2. If hits exist but none land in the window, the hit CLOSEST to the reference. In
        ///    hilly open worlds real terrain sits many metres above or below the flat profile
        ///    ground, so every hit misses the window; without this tier the visitor falls
        ///    through to the flat plane and is buried inside the hill with the ground
        ///    rendering overhead.
        /// 3. No hits at all — the flat profile ground. A collider with holes in it is normal.
        /// </summary>
        /// <param name="hitHeights">World-space Y of every downward hit. May be empty.</param>
        /// <param name="referenceY">The world's measured flat ground height, already scaled.</param>
        /// <param name="worldScale">Immersion multiplier, so the down-window scales with it.</param>
        public static float PickGroundHeight(System.Collections.Generic.IReadOnlyList<float> hitHeights,
                                             float referenceY, float worldScale)
        {
            float ws = worldScale <= 0f ? 1f : worldScale;
            float floor = referenceY - MaxDropBelow * ws;
            float ceiling = referenceY + Mathf.Min(1.2f * ws, MaxStepUp);

            bool haveBest = false, haveNearest = false;
            float best = 0f, nearest = 0f;

            if (hitHeights != null)
            {
                for (int i = 0; i < hitHeights.Count; i++)
                {
                    float y = hitHeights[i];
                    if (!haveNearest || Mathf.Abs(y - referenceY) < Mathf.Abs(nearest - referenceY))
                    {
                        nearest = y;
                        haveNearest = true;
                    }
                    if (y < floor || y > ceiling) continue;
                    if (!haveBest || y > best)
                    {
                        best = y;
                        haveBest = true;
                    }
                }
            }

            if (haveBest) return best;
            if (haveNearest) return nearest;
            return referenceY;
        }

    }
}
