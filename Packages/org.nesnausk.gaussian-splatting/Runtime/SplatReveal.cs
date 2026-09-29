// SPDX-License-Identifier: MIT
// MuseXR local addition to the embedded package.

using UnityEngine;

namespace GaussianSplatting.Runtime
{
    /// <summary>Which side of the active reveal front a splat renderer belongs to.</summary>
    public enum SplatRevealRole
    {
        None,
        /// <summary>The world being left: kept ABOVE the front, dissolving as the front passes.</summary>
        Leaving,
        /// <summary>The world arriving: kept BELOW the front, forming as the front passes.</summary>
        Arriving,
    }

    /// <summary>
    /// A world-to-world transition with no door: a ragged front sweeps through space (normally
    /// rising from the floor), and the two worlds trade places across it, splat by splat. In the
    /// band either side of the front, a splat's opacity fades and its footprint swells, so the
    /// worlds meet as soft brush-dabs rather than a hard cut. Evaluated per splat in the compute
    /// pass (<c>SplatUtilities.compute</c>, CSCalcViewData), so it costs no extra pass and no mask.
    ///
    /// Both worlds are resident and sorted for the length of the transition, like a portal.
    /// </summary>
    public static class SplatReveal
    {
        public static bool Active;

        /// <summary>Unit normal of the front, world space. Up for a front rising from the floor.</summary>
        public static Vector3 Normal = Vector3.up;

        /// <summary>Position of the front along <see cref="Normal"/>, metres.</summary>
        public static float Front;

        /// <summary>Width of the band where both worlds are partly present, metres.</summary>
        public static float Band = 1.2f;

        /// <summary>How far the ragged edge wanders either side of the front, metres.</summary>
        public static float NoiseAmplitude = 0.8f;

        /// <summary>Size of the ragged edge's features, metres.</summary>
        public static float NoiseScale = 1.6f;

        /// <summary>How much larger a splat grows as it vanishes (0 = not at all; 2 = three times).</summary>
        public static float DabGrow = 2.5f;

        /// <summary>
        /// Per-splat side of the front: positive above (along the normal), negative below. The
        /// compute shader evaluates the same thing; this copy is for tests and tools.
        /// </summary>
        public static float Height(Vector3 world, float noise01) =>
            Vector3.Dot(world, Normal) - Front + (noise01 - 0.5f) * 2f * NoiseAmplitude;

        /// <summary>How present a splat of the given role is at signed height h (0..1).</summary>
        public static float Presence(SplatRevealRole role, float h, float band)
        {
            band = Mathf.Max(1e-3f, band);
            return role switch
            {
                SplatRevealRole.Leaving => Mathf.Clamp01(h / band + 0.5f),
                SplatRevealRole.Arriving => Mathf.Clamp01(-h / band + 0.5f),
                _ => 1f,
            };
        }
    }
}
