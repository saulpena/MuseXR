using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Pulls a candidate hanging position onto the real wall of a Marble capture.
    ///
    /// <b>Why this exists.</b> <see cref="GalleryWall"/> lays works against the playtested walk
    /// box, which is a rectangle inset in the room — close to the walls, but not on them. Skylar's
    /// captures ship a trimesh collider (<c>Assets/Worlds/Colliders/*-collider.glb</c>) that is the
    /// actual architecture, so where that geometry exists a work can sit on the surface a visitor
    /// sees rather than hovering somewhere near it.
    ///
    /// <b>It is a refinement, never a dependency.</b> These colliders are patchy by her own record
    /// ("collider junk geometry below the real floor made feet sink half-body in van-gogh/floral"),
    /// and measured here on van-gogh at 500k: of eight works, the four on the -X side found wall
    /// within 0.3-3.4 m and the four on the +X side found nothing at all, because her walk box runs
    /// to x 17.0 while the collider shell stops at 15.7. So a miss is the expected case, not an
    /// error, and it must leave the playtested position exactly as it was.
    ///
    /// Pure arithmetic — the caller does the raycasting. Same split as <see cref="GalleryWall"/>
    /// and <c>WalkGround</c>: the geometry query is the engine's job, the judgement is testable.
    /// </summary>
    public static class WallSnap
    {
        /// <summary>
        /// Past this the collider is describing a different room, not the wall behind this work.
        /// A correction of several metres is how an artwork ends up in the next gallery.
        /// </summary>
        public const float MaxCorrection = 4.0f;

        /// <summary>
        /// A wall stands up. Anything flatter is a floor, a ceiling or a soffit, and hanging a
        /// canvas on it faces the work at nobody.
        /// </summary>
        public const float MaxNormalY = 0.35f;

        /// <summary>Stand the canvas off the surface so it never z-fights the capture.</summary>
        public const float Inset = 0.12f;

        public static bool IsWall(Vector3 normal) => Mathf.Abs(normal.y) <= MaxNormalY;

        /// <summary>
        /// The wall's face turned back into the room. A trimesh from a scan has inconsistent
        /// winding, so the raw normal may point either way; <paramref name="outward"/> is the
        /// direction the ray travelled, and the room is behind it.
        /// </summary>
        public static Vector3 FaceIntoRoom(Vector3 normal, Vector3 outward)
        {
            var n = normal.normalized;
            return Vector3.Dot(n, outward) > 0f ? -n : n;
        }

        /// <summary>
        /// Move <paramref name="candidate"/> onto the wall, or leave it untouched.
        ///
        /// <paramref name="outward"/> points from the room toward the wall — the direction the
        /// probe was cast. Pass <paramref name="hit"/> false when nothing was struck.
        ///
        /// <b>Height comes from the candidate, not the wall.</b> The candidate's Y is the measured
        /// standing eye height for that world; a scanned wall leans and undulates, so taking Y from
        /// the hit point would let the works drift up and down the room by whatever the capture
        /// happened to do.
        /// </summary>
        public static HungArtwork Apply(
            HungArtwork candidate, Vector3 outward, bool hit, Vector3 point, Vector3 normal)
        {
            if (!hit) return candidate;
            if (!IsWall(normal)) return candidate;

            var facing = FaceIntoRoom(normal, outward);
            var seated = point + facing * Inset;

            // Judge the correction on the ground plane: a tall wall struck high up is still the
            // same wall, and only the horizontal move can put a work in another room.
            var moved = new Vector2(seated.x - candidate.Position.x, seated.z - candidate.Position.z).magnitude;
            if (moved > MaxCorrection) return candidate;

            var placed = new Vector3(seated.x, candidate.Position.y, seated.z);
            return new HungArtwork(placed, Quaternion.LookRotation(facing, Vector3.up), candidate.Index);
        }
    }
}
