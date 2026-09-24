using System.Collections.Generic;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>Where one artwork hangs, and which way it faces.</summary>
    public readonly struct HungArtwork
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly int Index;
        public HungArtwork(Vector3 position, Quaternion rotation, int index)
        {
            Position = position; Rotation = rotation; Index = index;
        }

        /// <summary>Out of the wall, into the room — the way a visitor stands to look at it.</summary>
        public Vector3 Facing => Rotation * Vector3.forward;

        /// <summary>
        /// The rotation to put on a Unity <b>Quad</b>, which is not <see cref="Rotation"/>.
        ///
        /// <b>Measured 23 Sep 2026, by photographing a black screen.</b> Unity's Quad primitive has
        /// its normal along its own <b>-Z</b>, and URP/Unlit culls back faces, so a quad built with
        /// <c>LookRotation(facing)</c> is visible only from <i>inside</i> the wall. Every artwork in
        /// <c>Museum.unity</c> had been hung backwards since the wall was first built; nothing
        /// rendered where the camera was, and the failure looks exactly like "the artwork is
        /// missing" rather than like a rotation.
        ///
        /// This is the same rule CLAUDE.md records for world-space TextMeshPro — a flat thing reads
        /// correctly when its own +Z points the SAME way the viewer is looking, i.e. away from
        /// them. Kept here as a named property rather than a 180 at each call site, because a 180
        /// someone has to remember is how this happened.
        /// </summary>
        public Quaternion QuadRotation => Rotation * Quaternion.Euler(0f, 180f, 0f);
    }

    /// <summary>
    /// Hangs artworks on the walls of a Gaussian-splat capture.
    ///
    /// <b>Her constants do not port.</b> muse-infinity places planes at <c>side * 7.12, 3.35, z</c>
    /// with rows every 8.6 m — numbers fitted to her box gallery and reused unchanged in world
    /// mode. Our captures are nothing like that size: `elegant-floral-palace-interior` measures
    /// about 39 x 48 m, `grand-conservatory-with-lush-gardens` about 345 x 348 m. Fixed offsets
    /// would hang half the works inside a wall and the rest in open sky.
    ///
    /// So placement is derived from each world's own bounds: works alternate down the two long
    /// walls, inset from the shell, at a height that reads from standing eye level.
    ///
    /// Pure arithmetic — no scene, no GameObjects — so it can be argued about in EditMode rather
    /// than by putting a headset on. Same reasoning as <c>CompanionFormation</c>.
    /// </summary>
    public static class GalleryWall
    {
        /// <summary>Keep this far inside the capture's shell, which is noisy at its outer edge.</summary>
        public const float DefaultInset = 2.5f;

        /// <summary>Centre height of a hung work, in world units before world scale.</summary>
        public const float DefaultHeight = 2.2f;

        /// <summary>Never crowd works closer than this, however many there are.</summary>
        public const float MinSpacing = 3.0f;

        /// <summary>
        /// Lay <paramref name="count"/> works along the two long walls of a box.
        ///
        /// <paramref name="boundsMin"/> and <paramref name="boundsMax"/> are the capture's bounds
        /// AFTER world scale. <paramref name="groundY"/> is the floor the visitor stands on, so a
        /// world whose bounds run far below the walkable surface still hangs its works at head
        /// height rather than underground.
        /// </summary>
        public static List<HungArtwork> Lay(
            Vector3 boundsMin, Vector3 boundsMax, float groundY, int count,
            float inset = DefaultInset, float height = DefaultHeight)
        {
            var hung = new List<HungArtwork>();
            if (count <= 0) return hung;

            var size = boundsMax - boundsMin;
            var centre = (boundsMin + boundsMax) * 0.5f;

            // The long horizontal axis is the one you walk down; works hang on the walls either
            // side of it.
            var alongX = size.x >= size.z;
            var alongLength = Mathf.Max(alongX ? size.x : size.z, 1f);
            var acrossHalf = Mathf.Max((alongX ? size.z : size.x) * 0.5f - inset, inset);

            var perSide = Mathf.CeilToInt(count / 2f);
            // Spacing leaves a margin at each end: perSide works occupy perSide+1 gaps.
            var spacing = Mathf.Max(alongLength / (perSide + 1), MinSpacing);
            var span = spacing * (perSide - 1);
            var start = -span * 0.5f;

            for (var i = 0; i < count; i++)
            {
                var side = (i % 2 == 0) ? -1f : 1f;   // alternate walls, as hers does
                var slot = i / 2;
                var along = start + spacing * slot;

                var pos = alongX
                    ? new Vector3(centre.x + along, groundY + height, centre.z + acrossHalf * side)
                    : new Vector3(centre.x + acrossHalf * side, groundY + height, centre.z + along);

                // Face the middle of the room: a work on the left wall looks right, and vice versa.
                var inward = alongX ? new Vector3(0f, 0f, -side) : new Vector3(-side, 0f, 0f);
                hung.Add(new HungArtwork(pos, Quaternion.LookRotation(inward, Vector3.up), i));
            }

            return hung;
        }

        /// <summary>
        /// The stop order for the guided walk: nearest first from where the visitor is standing.
        ///
        /// Her HUD reads <c>WALK TO STOP 1 / 4 · &lt;title&gt; · 17 m</c>, so the tour has to know
        /// which work is next and how far away it is. Ordering by distance from the spawn means
        /// the walk moves outward rather than doubling back.
        /// </summary>
        public static List<int> TourOrder(IReadOnlyList<HungArtwork> hung, Vector3 from)
        {
            var order = new List<int>();
            for (var i = 0; i < hung.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                var da = (hung[a].Position - from).sqrMagnitude;
                var db = (hung[b].Position - from).sqrMagnitude;
                var c = da.CompareTo(db);
                return c != 0 ? c : a.CompareTo(b);   // stable: ties keep wall order
            });
            return order;
        }

        /// <summary>
        /// Lay works against the room the visitor can actually walk, falling back to the capture
        /// bounds when no walk box has been measured.
        ///
        /// <b>Measured 23 Sep 2026, by hanging them wrong first.</b> Laying against the splat
        /// bounds put every work about 28 m outside the van-gogh corridor, floating in black void —
        /// the same mistake class as the spawn heuristic, and visible only in a screenshot. A
        /// capture's bounds include sky and outlying noise; her `profile.bounds` describe the room.
        ///
        /// Takes plain vectors rather than a <c>WorldDefinition</c> on purpose: this assembly must
        /// not depend on <c>MusePico.Worlds</c>, which would drag splats, XRI and Addressables into
        /// the dialogue assembly. The caller decides which box is authoritative.
        /// </summary>
        public static List<HungArtwork> LayInRoom(
            Vector3 walkMin, Vector3 walkMax, bool haveWalkBox,
            Vector3 splatMin, Vector3 splatMax, float groundY, int count,
            float height = DefaultHeight)
        {
            // A playtested walk box is already inset from the shell, so take a smaller bite of it.
            return haveWalkBox
                ? Lay(walkMin, walkMax, groundY, count, 1.0f, height)
                : Lay(splatMin, splatMax, groundY, count, DefaultInset, height);
        }

        /// <summary>Metres to a work, ignoring height — the number her HUD shows.</summary>
        public static float GroundDistance(Vector3 from, Vector3 to)
        {
            var a = new Vector2(from.x, from.z);
            var b = new Vector2(to.x, to.z);
            return Vector2.Distance(a, b);
        }
    }
}
