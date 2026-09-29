using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.DynamicWorlds
{
    /// <summary>
    /// Where a world generated from the DepthRoom panorama goes, in the room's own frame (metres,
    /// floor at y 0, the panorama camera at the origin looking +Z).
    ///
    /// Measured on three generations from the same depth pano (DYNAMIC-GENERATION.md, 29 Sep 2026):
    /// a raw API splat is marble_raw_opencv (Y down) with the room's front and back swapped, which
    /// together is a 180 degree turn about Z; and one uniform scale in RAW units fits every wall to
    /// within 2-3% — 2.911 for marble-1.1, 2.902 for Draft. The scale is a property of this depth
    /// room, not of the model, so it lives here as a constant rather than coming from
    /// metric_scale_factor, which Marble infers from the painting and got 3.8x wrong.
    /// </summary>
    public static class DynamicWorldPlacement
    {
        public static readonly Quaternion Rotation = Quaternion.Euler(0f, 0f, 180f);
        public const float DepthRoomRawScale = 2.906f;

        /// <summary>
        /// The floor's height in the splat's RAW frame, where Y points down, so the floor is the
        /// dense band at the high end of Y. Returns the centre of the fullest bin between the 80th
        /// and 99.5th percentile — measured 0.586 (marble-1.1) and 0.604 (Draft) on the DepthRoom worlds.
        /// </summary>
        public static float FloorRawY(IReadOnlyList<float> rawY, float binSize = 0.01f)
        {
            if (rawY == null || rawY.Count == 0) throw new ArgumentException("no splats");
            var sorted = new float[rawY.Count];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = rawY[i];
            Array.Sort(sorted);
            float lo = sorted[(int)(0.80f * (sorted.Length - 1))];
            float hi = sorted[(int)(0.995f * (sorted.Length - 1))];
            if (hi <= lo) return lo;

            int bins = Mathf.Max(1, Mathf.CeilToInt((hi - lo) / binSize));
            var counts = new int[bins];
            foreach (float y in sorted)
            {
                if (y < lo || y > hi) continue;
                counts[Mathf.Min(bins - 1, (int)((y - lo) / binSize))]++;
            }
            int best = 0;
            for (int b = 1; b < bins; b++) if (counts[b] > counts[best]) best = b;
            return lo + (best + 0.5f) * binSize;
        }

        /// <summary>Lift that puts the raw floor at y 0 once rotated and scaled.</summary>
        public static float LiftForFloor(float floorRawY, float scale = DepthRoomRawScale) => floorRawY * scale;

        /// <summary>Same lift from Marble's own ground data: its offset is in metres of ITS metric scale.</summary>
        public static float LiftFromSemantics(float groundPlaneOffset, float metricScaleFactor, float scale = DepthRoomRawScale) =>
            groundPlaneOffset / metricScaleFactor * scale;

        public static void Apply(Transform t, float lift, float scale = DepthRoomRawScale)
        {
            t.SetPositionAndRotation(new Vector3(0f, lift, 0f), Rotation);
            t.localScale = Vector3.one * scale;
        }
    }
}
