using System.Collections.Generic;
using MusePico.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The walk mask on a synthetic splat room: a 10 x 8 m floor, walls round it with a doorway in
    /// one, a pillar inside, and nothing but empty space outside the door. Each test fails if the
    /// painters could end up standing in a wall.
    /// </summary>
    public class WalkMaskTests
    {
        static readonly WalkMask.Settings S = new WalkMask.Settings { reach = 12f };

        /// <summary>Floor splats every 0.1 m over x -5..5, z -4..4; wall splats on the edge except a
        /// 2.4 m doorway at +x (the size of the SplatPortal door); a 1 m pillar at (2, 0); optionally
        /// a 0.6 m crack in the west wall. Heights in metres above a floor at y = 0.</summary>
        static List<Vector3> Room(bool pillar = true, bool floater = false, bool crack = false)
        {
            var pts = new List<Vector3>();
            for (float x = -5f; x <= 5f; x += 0.1f)
            for (float z = -4f; z <= 4f; z += 0.1f)
                pts.Add(new Vector3(x, 0.02f, z));

            void Column(float x, float z)
            {
                for (float y = 0.5f; y <= 1.7f; y += 0.1f) pts.Add(new Vector3(x, y, z));
            }
            for (float x = -5.2f; x <= 5.2f; x += 0.1f) { Column(x, -4.2f); Column(x, 4.2f); }
            for (float z = -4.2f; z <= 4.2f; z += 0.1f)
            {
                if (!crack || Mathf.Abs(z - 2.25f) > 0.3f) Column(-5.2f, z);   // 0.6 m crack spanning the 2.0-2.5 cell
                if (Mathf.Abs(z) > 1.2f) Column(5.2f, z);          // doorway at +x, |z| < 1.2
            }
            if (pillar)
                for (float x = 1.6f; x <= 2.4f; x += 0.1f)
                for (float z = -0.4f; z <= 0.4f; z += 0.1f) Column(x, z);
            if (floater)
                for (int i = 0; i < 3; i++) pts.Add(new Vector3(-2f, 1.0f, 2f));   // three stray splats
            return pts;
        }

        static WalkMask.Grid Build(List<Vector3> pts) => WalkMask.Build(pts, 0f, new[] { Vector2.zero }, S);

        [Test]
        public void The_room_is_walkable_and_its_walls_are_not()
        {
            var g = Build(Room());
            Assert.IsTrue(g.WalkableAt(new Vector2(-3f, -2f)), "open floor");
            Assert.IsTrue(g.WalkableAt(new Vector2(3f, 3f)), "open floor beyond the pillar");
            Assert.IsFalse(g.WalkableAt(new Vector2(-5.2f, 0f)), "west wall");
            Assert.IsFalse(g.WalkableAt(new Vector2(0f, 4.2f)), "north wall");
        }

        [Test]
        public void A_pillar_is_not_walkable()
        {
            Assert.IsFalse(Build(Room()).WalkableAt(new Vector2(2f, 0f)));
            Assert.IsTrue(Build(Room(pillar: false)).WalkableAt(new Vector2(2f, 0f)), "control: without the pillar it is");
        }

        [Test]
        public void The_fill_does_not_leak_out_of_a_doorway_where_there_is_no_floor()
        {
            var g = Build(Room());
            Assert.IsTrue(g.WalkableAt(new Vector2(5.2f, 0f)), "the doorway itself has floor next to it");
            Assert.IsFalse(g.WalkableAt(new Vector2(8f, 0f)), "outside the building there is no floor");
            Assert.Less(g.WalkableCount, (11f * 9f) / (S.cell * S.cell), "more walkable cells than the room holds");
        }

        [Test]
        public void A_crack_in_a_wall_is_sealed_and_a_doorway_is_not()
        {
            // Floor outside the west wall, so a leak through the crack would have somewhere to go.
            var pts = Room(crack: true);
            for (float x = -8f; x <= -5.5f; x += 0.1f)
            for (float z = -4f; z <= 4f; z += 0.1f) pts.Add(new Vector3(x, 0.02f, z));
            var g = Build(pts);
            Assert.IsFalse(g.WalkableAt(new Vector2(-7f, 2.25f)), "leaked through a 0.6 m crack");
            Assert.IsTrue(g.WalkableAt(new Vector2(5.2f, 0f)), "a 2.4 m doorway was sealed");

            var open = WalkMask.Build(pts, 0f, new[] { Vector2.zero }, new WalkMask.Settings { reach = 12f, closeCells = 0 });
            Assert.IsTrue(open.WalkableAt(new Vector2(-7f, 2.25f)), "control: without closing the crack does leak");
        }

        [Test]
        public void A_few_stray_splats_do_not_make_a_solid_blob()
        {
            Assert.IsTrue(Build(Room(floater: true)).WalkableAt(new Vector2(-2f, 2f)));
        }

        [Test]
        public void A_seed_on_a_dark_patch_still_finds_the_floor()
        {
            var pts = Room();
            pts.RemoveAll(p => p.y < 0.1f && Mathf.Abs(p.x) < 1.6f && Mathf.Abs(p.z) < 1.6f);   // 3 m of dark floor round the seed: wider than the neighbour rule bridges
            var g = Build(pts);
            Assert.Greater(g.WalkableCount, 100, "the seed found no floor and nothing was filled");
        }

        [Test]
        public void Rectangles_cover_exactly_the_walkable_cells()
        {
            var g = Build(Room());
            float area = 0f;
            foreach (var (min, max) in WalkMask.Rectangles(g)) area += (max.x - min.x) * (max.y - min.y);
            Assert.AreEqual(g.WalkableCount * g.cell * g.cell, area, 1e-3f);
        }

        [Test]
        public void Opacity_is_the_logistic_of_the_stored_logit()
        {
            Assert.AreEqual(0.5f, WalkMask.Opacity(0f), 1e-6f);
            Assert.Greater(WalkMask.Opacity(5f), 0.99f);
            Assert.Less(WalkMask.Opacity(-5f), 0.01f);
        }

        [Test]
        public void The_current_stage_is_the_first_not_yet_left()
        {
            Assert.AreEqual(0, EscortStages.Current(new[] { false, false, false }));
            Assert.AreEqual(1, EscortStages.Current(new[] { true, false, false }), "crossed the door");
            Assert.AreEqual(2, EscortStages.Current(new[] { true, true, false }));
            Assert.AreEqual(2, EscortStages.Current(new[] { true, true, true }), "last world stays current");
            Assert.AreEqual(-1, EscortStages.Current(new bool[0]));
        }
    }
}
