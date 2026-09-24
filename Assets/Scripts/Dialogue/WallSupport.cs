using System.Collections.Generic;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Throws away wall surfaces that are isolated collider fragments rather than wall.
    ///
    /// <b>Why.</b> <see cref="WallGallery"/> hangs on whatever the collider sweep struck, and on
    /// van-gogh-500k that put <c>aic-14556</c> at (5.1, 2.2, -14.9) — a surface the collider
    /// asserts and the capture does not visibly support, so the work hung against a smear. Skylar's
    /// colliders are decimated trimeshes with junk in them ("collider junk geometry below the real
    /// floor made feet sink half-body in van-gogh/floral"); a single triangle in mid-air passes
    /// every per-hit test for a wall, because every per-hit test can only see one hit.
    ///
    /// <b>The rule.</b> A real wall is big. So a surface is trusted only when enough OTHER struck
    /// surfaces lie near it, face the same way, and sit in the same plane. That is three conditions
    /// and all three are needed:
    ///
    /// <list type="bullet">
    /// <item>near — an isolated fragment has no neighbours at all;</item>
    /// <item>facing the same way — the far side of a thin wall is near, and is not this wall;</item>
    /// <item>in the same plane — a parallel wall across a 3 m corridor is near AND faces the same
    /// way, and is still a different wall.</item>
    /// </list>
    ///
    /// Pure arithmetic, no scene. Same split as <see cref="GalleryWall"/> and <see cref="WallSnap"/>.
    /// </summary>
    public static class WallSupport
    {
        /// <summary>
        /// How far away a neighbour may be and still be the same wall. The sweep dedupes at 1.5 m,
        /// so this admits roughly two dedupe cells in each direction along the surface.
        /// </summary>
        public const float Radius = 4.0f;

        /// <summary>
        /// How many neighbours a surface needs. Two, with <see cref="Radius"/>, means a wall has to
        /// be continuous for a few metres before anything hangs on it — which is the same thing as
        /// saying it is a wall and not a shard.
        /// </summary>
        public const int MinNeighbours = 2;

        /// <summary>Normals this far apart still count as the same wall: about 45 degrees.</summary>
        public const float NormalAgreement = 0.7f;

        /// <summary>
        /// How far out of the plane a neighbour may sit. Generous, because a scanned wall is not
        /// flat — but far tighter than the width of any corridor worth walking down.
        /// </summary>
        public const float PlaneTolerance = 0.75f;

        /// <summary>
        /// The subset of <paramref name="surfaces"/> that looks like real wall.
        ///
        /// Order is preserved, so this stays deterministic and composes in front of
        /// <see cref="WallGallery.Lay"/> without disturbing its seeding.
        /// </summary>
        public static List<WallSurface> Supported(
            IReadOnlyList<WallSurface> surfaces,
            int minNeighbours = MinNeighbours,
            float radius = Radius,
            float normalAgreement = NormalAgreement,
            float planeTolerance = PlaneTolerance)
        {
            var kept = new List<WallSurface>();
            if (surfaces == null) return kept;

            for (var i = 0; i < surfaces.Count; i++)
            {
                if (Neighbours(surfaces, i, radius, normalAgreement, planeTolerance) >= minNeighbours)
                    kept.Add(surfaces[i]);
            }
            return kept;
        }

        /// <summary>How many other surfaces agree that <paramref name="index"/> is part of a wall.</summary>
        public static int Neighbours(
            IReadOnlyList<WallSurface> surfaces, int index,
            float radius = Radius, float normalAgreement = NormalAgreement,
            float planeTolerance = PlaneTolerance)
        {
            if (surfaces == null || index < 0 || index >= surfaces.Count) return 0;

            var self = surfaces[index];
            var normal = self.Normal.sqrMagnitude < 1e-6f ? Vector3.forward : self.Normal.normalized;

            var count = 0;
            for (var j = 0; j < surfaces.Count; j++)
            {
                if (j == index) continue;
                var other = surfaces[j];

                var offset = other.Point - self.Point;
                if (offset.magnitude > radius) continue;

                var otherNormal = other.Normal.sqrMagnitude < 1e-6f
                    ? Vector3.forward : other.Normal.normalized;
                if (Vector3.Dot(normal, otherNormal) < normalAgreement) continue;

                // Distance out of plane: the corridor test. Measured against the MEAN of the two
                // normals, not this surface's own, for two reasons. It makes the relation
                // symmetric — "A supports B" and "B supports A" must agree, or which end of a wall
                // you ask from changes the answer. And on a curved wall the tangent plane runs away
                // quadratically, so judging from one end alone rejects the far end of any curve.
                var between = (normal + otherNormal).sqrMagnitude < 1e-6f
                    ? normal : (normal + otherNormal).normalized;
                if (Mathf.Abs(Vector3.Dot(offset, between)) > planeTolerance) continue;

                count++;
            }
            return count;
        }
    }
}
