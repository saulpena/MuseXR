using System.Collections.Generic;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>A piece of real wall found in a capture: where it is, and which way it faces the room.</summary>
    public readonly struct WallSurface
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;   // already turned back into the room
        public WallSurface(Vector3 point, Vector3 normal) { Point = point; Normal = normal; }
    }

    /// <summary>
    /// Hangs works on the walls a capture actually has, rather than on the sides of a box.
    ///
    /// <b>Why this replaces the box for any world with a collider.</b> <see cref="GalleryWall"/>
    /// alternates works down the two long sides of the walk box, which assumes a corridor with a
    /// wall on each side. Measured on van-gogh-500k, that assumption is false: sweeping the +X side
    /// of her walk box at picture height finds **no wall within 30 m for z -20 to +4**, because the
    /// capture's east side is open. Four of eight works were hanging in the open air outside the
    /// building, and no amount of snapping fixes that — there is nothing to snap to. Her
    /// `profile.bounds` describe the **floor a visitor may stand on**, which is not the same shape
    /// as the walls they can see.
    ///
    /// So the collider chooses the walls. The caller sweeps it (engine work) and passes what it
    /// struck; this picks a well-spread subset (pure arithmetic, testable without a scene).
    ///
    /// Degrades rather than fails: with too few surfaces it returns fewer works, and the caller
    /// tops the rest up from <see cref="GalleryWall"/>. A capture with no collider gets the box.
    /// </summary>
    public static class WallGallery
    {
        /// <summary>Never crowd two works closer than this along the wall.</summary>
        public const float MinSpacing = 3.0f;

        /// <summary>
        /// A surface nearer than this to the visitor is not somewhere a painting can hang.
        ///
        /// <b>Measured, not chosen for taste.</b> The first sweep of van-gogh-500k put a work at
        /// (3.0, 2.2, 0.2) against a spawn of (3.0, 0, 0.5) — 0.3 m from the visitor's face. That
        /// is the collider junk Skylar records at this exact capture ("collider junk geometry below
        /// the real floor made feet sink half-body in van-gogh/floral"): probing outward from the
        /// spawn returns hits at 0.1-0.2 m in almost every direction. Those surfaces pass every
        /// geometric test for a wall and are not one.
        /// </summary>
        public const float MinFromVisitor = 2.5f;

        /// <summary>Stand the canvas off the surface so it never z-fights the capture.</summary>
        public const float Inset = WallSnap.Inset;

        /// <summary>
        /// Choose up to <paramref name="count"/> spots from <paramref name="surfaces"/>, spread as
        /// widely as the wall allows, and hang them all at <paramref name="height"/>.
        ///
        /// Selection is farthest-point sampling seeded at the surface nearest
        /// <paramref name="from"/> — the visitor's spawn. That puts the first work where they are
        /// standing and pushes the rest apart, which is what "spread around the room" means when
        /// the room is an arbitrary scanned shape rather than a rectangle. It is deterministic:
        /// ties resolve on index, so the same capture always hangs the same way.
        ///
        /// <paramref name="height"/> is absolute world Y, not an offset — a scanned wall leans, and
        /// taking height from the surface would let the works drift up and down the room.
        ///
        /// Surfaces within <see cref="MinFromVisitor"/> of <paramref name="from"/> are discarded
        /// first; see that constant for what they turned out to be.
        /// </summary>
        public static List<HungArtwork> Lay(
            IReadOnlyList<WallSurface> surfaces, Vector3 from, float height, int count)
        {
            var hung = new List<HungArtwork>();
            if (surfaces == null || surfaces.Count == 0 || count <= 0) return hung;

            // Drop anything the visitor would be standing inside before choosing at all.
            var usable = new List<int>();
            for (var i = 0; i < surfaces.Count; i++)
                if (Flat(surfaces[i].Point, from) >= MinFromVisitor) usable.Add(i);
            if (usable.Count == 0) return hung;

            var chosen = new List<int>();
            var best = usable[0];
            var bestD = float.MaxValue;
            foreach (var i in usable)
            {
                var d = Flat(surfaces[i].Point, from);
                if (d < bestD) { bestD = d; best = i; }
            }
            chosen.Add(best);

            while (chosen.Count < count)
            {
                var pick = -1;
                var pickD = 0f;
                foreach (var i in usable)
                {
                    if (chosen.Contains(i)) continue;
                    var nearest = float.MaxValue;
                    foreach (var c in chosen)
                        nearest = Mathf.Min(nearest, Flat(surfaces[i].Point, surfaces[c].Point));
                    if (nearest > pickD) { pickD = nearest; pick = i; }
                }
                if (pick < 0 || pickD < MinSpacing) break;   // the wall is full
                chosen.Add(pick);
            }

            // Index order is walk order, so the tour HUD reads "STOP 1 / n" outward from the spawn.
            chosen.Sort((a, b) =>
            {
                var c = Flat(surfaces[a].Point, from).CompareTo(Flat(surfaces[b].Point, from));
                return c != 0 ? c : a.CompareTo(b);
            });

            for (var i = 0; i < chosen.Count; i++)
            {
                var s = surfaces[chosen[i]];
                var n = s.Normal.sqrMagnitude < 1e-6f ? Vector3.forward : s.Normal.normalized;
                var seated = s.Point + n * Inset;
                hung.Add(new HungArtwork(
                    new Vector3(seated.x, height, seated.z), Quaternion.LookRotation(n, Vector3.up), i));
            }
            return hung;
        }

        static float Flat(Vector3 a, Vector3 b) =>
            new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
