// SPDX-License-Identifier: MIT
// MuseXR local addition to the embedded package.

using UnityEngine;

namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// The door arithmetic shared by the splat shaders' portal clip, the walk-through detection and
    /// the through-door ordering bake. Pure, so it is EditMode-testable; the compute shader repeats
    /// the same tests on the GPU (<c>SplatUtilities.compute</c>, CSCalcViewData).
    ///
    /// Everything is in DOOR space: the aperture is the rectangle |x| &lt;= half.x, |y| &lt;= half.y
    /// on the plane z = 0, centred on the door transform. The approach side is z &lt; 0; the world
    /// seen through the door lies at z &gt; 0. Nothing here assumes which world is which — after
    /// the visitor crosses, the same rules apply with the sides exchanged.
    /// </summary>
    public static class PortalGeometry
    {
        /// <summary>-1 on the approach side of the door plane, +1 on the far side.</summary>
        public static int Side(Vector3 pDoor) => pDoor.z < 0f ? -1 : +1;

        /// <summary>
        /// True if the straight segment a→b passes through the plane INSIDE the aperture. Used for
        /// the walk-through: a head that crosses the plane beside the door frame has walked round
        /// it, not through it.
        /// </summary>
        public static bool SegmentCrossesAperture(Vector3 aDoor, Vector3 bDoor, Vector2 half)
        {
            if (Side(aDoor) == Side(bDoor)) return false;
            float dz = aDoor.z - bDoor.z;
            if (Mathf.Abs(dz) < 1e-9f) return false;
            float t = aDoor.z / dz;
            float x = aDoor.x + t * (bDoor.x - aDoor.x);
            float y = aDoor.y + t * (bDoor.y - aDoor.y);
            return Mathf.Abs(x) <= half.x && Mathf.Abs(y) <= half.y;
        }

        /// <summary>
        /// True if a point on the other side of the door from the eye is seen THROUGH the aperture
        /// (grown by <paramref name="margin"/> metres on every edge). A point on the eye's own side
        /// is never "through".
        /// </summary>
        public static bool SeenThroughAperture(Vector3 eyeDoor, Vector3 pDoor, Vector2 half, float margin)
        {
            if (Side(eyeDoor) == Side(pDoor)) return false;
            return SegmentCrossesAperture(eyeDoor, pDoor, half + new Vector2(margin, margin));
        }

        /// <summary>
        /// The eye is so close to the plane, inside the door, that the aperture fills the view and
        /// may fall behind the near clip. The mask is then the whole screen rather than a drawn
        /// quad, so the crossing frame cannot flash the wrong world.
        /// </summary>
        public static bool InCrossingZone(Vector3 eyeDoor, Vector2 half, float zone)
        {
            return Mathf.Abs(eyeDoor.z) < zone &&
                   Mathf.Abs(eyeDoor.x) <= half.x && Mathf.Abs(eyeDoor.y) <= half.y;
        }

        /// <summary>
        /// True if any of <paramref name="eyesDoor"/> sees <paramref name="pDoor"/> through the
        /// aperture. The through-door ordering bake asks this of every splat of the next world.
        /// </summary>
        public static bool VisibleFromAny(Vector3[] eyesDoor, Vector3 pDoor, Vector2 half, float margin)
        {
            for (int i = 0; i < eyesDoor.Length; ++i)
                if (SeenThroughAperture(eyesDoor[i], pDoor, half, margin)) return true;
            return false;
        }

        /// <summary>
        /// Eye positions on the approach side, in door space, that the ordering bake treats as
        /// "where the visitor can be while the door is open": a grid <paramref name="depth"/> metres
        /// deep in front of the door, <paramref name="halfWidth"/> either side of its centre line,
        /// at the listed heights above the floor. The floor is the bottom of the aperture.
        /// </summary>
        public static Vector3[] ApproachEyes(Vector2 half, float depth, float halfWidth, float[] eyeHeights,
            int stepsDeep, int stepsWide)
        {
            stepsDeep = Mathf.Max(1, stepsDeep);
            stepsWide = Mathf.Max(1, stepsWide);
            var eyes = new Vector3[(stepsDeep + 1) * (stepsWide + 1) * eyeHeights.Length];
            int n = 0;
            for (int d = 0; d <= stepsDeep; ++d)
            {
                // Never exactly on the plane: 5 cm in front of it at the nearest.
                float z = -Mathf.Lerp(0.05f, depth, d / (float)stepsDeep);
                for (int w = 0; w <= stepsWide; ++w)
                {
                    float x = Mathf.Lerp(-halfWidth, halfWidth, w / (float)stepsWide);
                    foreach (var h in eyeHeights)
                        eyes[n++] = new Vector3(x, h - half.y, z);
                }
            }
            return eyes;
        }
    }
}
