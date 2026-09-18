using MusePico.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The rules that decide whether a world is walkable, tested without a scene.
    ///
    /// These matter more than they look: they are applied unchanged to every Marble world we
    /// generate, so a threshold that is wrong here quietly passes a broken world or fails a good
    /// one on all of them at once.
    /// </summary>
    public class WalkAuditTests
    {
        // The Sunlit gallery's measured box, as a realistic fixture.
        static readonly Vector4 Box = new Vector4(-2.90f, 2.71f, -29.64f, 22.10f);

        [Test]
        public void Inside_the_box_has_not_escaped()
        {
            Assert.IsFalse(WalkAudit.Escaped(new Vector3(0f, 0f, 0f), Box));
            Assert.IsFalse(WalkAudit.Escaped(new Vector3(-2.8f, 0f, -29f), Box));
        }

        [Test]
        public void Resting_against_a_wall_within_the_margin_has_not_escaped()
        {
            // A CharacterController's radius and skin width leave it slightly proud of a boundary
            // collider, so exact containment would fail every honest run.
            var justOutside = new Vector3(Box.y + WalkAudit.EscapeMargin * 0.5f, 0f, 0f);
            Assert.IsFalse(WalkAudit.Escaped(justOutside, Box));
        }

        [Test]
        public void Well_past_the_wall_has_escaped()
        {
            Assert.IsTrue(WalkAudit.Escaped(new Vector3(Box.y + 2f, 0f, 0f), Box), "+X");
            Assert.IsTrue(WalkAudit.Escaped(new Vector3(Box.x - 2f, 0f, 0f), Box), "-X");
            Assert.IsTrue(WalkAudit.Escaped(new Vector3(0f, 0f, Box.w + 2f), Box), "+Z");
            Assert.IsTrue(WalkAudit.Escaped(new Vector3(0f, 0f, Box.z - 2f), Box), "-Z");
        }

        [Test]
        public void Stepping_down_is_not_falling()
        {
            Assert.IsFalse(WalkAudit.Fell(-0.4f, 0f), "a sunken room is walkable");
        }

        [Test]
        public void Dropping_far_below_the_floor_is_falling()
        {
            Assert.IsTrue(WalkAudit.Fell(-40f, 0f), "through the floor and still going");
        }

        [Test]
        public void A_clean_leg_reports_no_problems()
        {
            var leg = new WalkLeg
            {
                name = "forward",
                start = new Vector3(0f, 0.04f, 6f),
                end = new Vector3(0f, 0.03f, -19f),
                floorMinY = 0f, floorMaxY = 0.06f,
                frames = 600, ungroundedFrames = 3,
                stoppedBy = "marble collider",
            };
            Assert.IsEmpty(WalkAudit.Problems(leg, Box, 0f));
        }

        [Test]
        public void A_leg_that_left_the_box_is_reported()
        {
            var leg = new WalkLeg
            {
                name = "out the doorway",
                start = Vector3.zero,
                end = new Vector3(0f, 0f, Box.w + 6f),
                floorMinY = 0f, floorMaxY = 0f,
                frames = 600, ungroundedFrames = 0,
                stoppedBy = "nothing",
            };
            var problems = WalkAudit.Problems(leg, Box, 0f);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("escaped the walk box", problems[0]);
        }

        [Test]
        public void A_leg_that_fell_through_is_reported()
        {
            var leg = new WalkLeg
            {
                name = "forward",
                start = Vector3.zero,
                end = new Vector3(0f, -50f, 0f),
                floorMinY = -50f, floorMaxY = 0f,
                frames = 600, ungroundedFrames = 590,
            };
            var problems = WalkAudit.Problems(leg, Box, 0f);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("fell through the floor", problems[0]);
        }

        [Test]
        public void Climbing_the_furniture_is_a_note_not_a_failure()
        {
            // The real 1.17 m plinth climb from the Sunlit gallery, measured through the actual
            // locomotion stack: contained, never fell, and still worth knowing about. It must not
            // fail the world — every Marble world we have does this.
            var leg = new WalkLeg
            {
                name = "diagonal back-L",
                start = Vector3.zero,
                end = new Vector3(1.83f, 1.17f, 8.20f),
                floorMinY = 0f, floorMaxY = 1.17f,
                frames = 2089, ungroundedFrames = 1966,
                stoppedBy = "marble collider",
            };
            Assert.IsEmpty(WalkAudit.Problems(leg, Box, 0f), "climbing a plinth is not a containment failure");

            var notes = WalkAudit.Notes(leg, 0f);
            Assert.AreEqual(2, notes.Count);
            StringAssert.Contains("climbed 1.17 m", notes[0]);
            StringAssert.Contains("standing on furniture", notes[1]);
        }

        [Test]
        public void Ungrounded_frames_never_fail_a_world()
        {
            // isGrounded read false for 1902 of 1997 frames on a leg that never left the floor.
            // It is not a usable signal here; Y position is. See the CORRECTION on WalkAudit.
            var leg = new WalkLeg
            {
                name = "forward",
                start = new Vector3(0f, 0.04f, 6f),
                end = new Vector3(0f, 0.06f, -24.01f),
                floorMinY = 0.04f, floorMaxY = 0.06f,
                frames = 1997, ungroundedFrames = 1902,
                stoppedBy = "marble collider",
            };
            Assert.IsEmpty(WalkAudit.Problems(leg, Box, 0f));
        }

        [Test]
        public void A_leg_that_never_met_an_obstacle_is_noted()
        {
            // Otherwise a leg that simply ran out of time reads as a clean pass, and that
            // direction's boundary was never actually tested.
            var leg = new WalkLeg
            {
                name = "forward",
                start = new Vector3(0f, 0f, 6f),
                end = new Vector3(0f, 0f, -24f),
                floorMinY = 0f, floorMaxY = 0.02f,
                frames = 1997,
                stoppedBy = "nothing",
            };
            Assert.IsEmpty(WalkAudit.Problems(leg, Box, 0f));
            var notes = WalkAudit.Notes(leg, 0f);
            Assert.AreEqual(1, notes.Count);
            StringAssert.Contains("never met an obstacle", notes[0]);
        }

        [Test]
        public void Report_names_every_leg()
        {
            var legs = new[]
            {
                new WalkLeg { name = "forward", end = new Vector3(0f, 0f, -19f), frames = 10, stoppedBy = "marble collider" },
                new WalkLeg { name = "back", end = new Vector3(0f, 0f, 21.9f), frames = 10, stoppedBy = "fallback box" },
            };
            var report = WalkAudit.Report("SunlitGallery", legs, Box, 0f);
            StringAssert.Contains("forward", report);
            StringAssert.Contains("back", report);
            StringAssert.Contains("fallback box", report);
        }
    }
}
