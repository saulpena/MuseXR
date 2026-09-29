using MusePico.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The painters' escort rules, checked by simulation: a visitor walks, turns, glances and
    /// teleports, a painter walks wherever the brain says at a human pace, and the tests read what
    /// happened. Every rule Saul gave (28 Sep 2026) has a test that fails if it is broken.
    /// </summary>
    public class EscortBrainTests
    {
        const float Dt = 1f / 60f;
        const float PainterSpeed = 1.2f;

        /// <summary>One painter and the visitor, stepped a frame at a time.</summary>
        class Sim
        {
            public readonly EscortSettings s = new EscortSettings();
            public readonly EscortBrain brain;
            public Vector2 visitor, self;
            public float gaze, heading, visitorSpeed;
            public Vector2[] others = new Vector2[0], art = new Vector2[0];
            public bool holdGround, clearCentre;
            public EscortOutput last;
            public float walked;
            public bool everMoved, everTrailed;

            public Sim(Vector2 self, float side = -1f) { this.self = self; brain = new EscortBrain(s, side); }

            public void Step()
            {
                last = brain.Tick(new EscortInput
                {
                    visitor = visitor, gazeYaw = gaze, heading = heading, visitorSpeed = visitorSpeed,
                    self = self, others = others, art = art, holdGround = holdGround, clearCentre = clearCentre, dt = Dt,
                });
                if (last.mode == EscortMode.Trailing) everTrailed = true;
                if (last.warp) self = last.destination;
                else if (last.move)
                {
                    everMoved = true;
                    var before = self;
                    self = Vector2.MoveTowards(self, last.destination, PainterSpeed * Dt);
                    walked += Vector2.Distance(before, self);
                }
            }

            public void Wait(float seconds)
            {
                visitorSpeed = 0f;
                for (float t = 0; t < seconds; t += Dt) Step();
            }

            /// <summary>Walk the visitor in a straight line, looking where they go.</summary>
            public void Walk(float yaw, float metres, float speed = 1.4f)
            {
                var dir = EscortBrain.YawDirection(yaw);
                heading = gaze = yaw;
                visitorSpeed = speed;
                for (float d = 0; d < metres; d += speed * Dt) { visitor += dir * speed * Dt; Step(); }
                visitorSpeed = 0f;
            }

            public float Angle => EscortBrain.ViewAngle(visitor, gaze, self);
            public float Distance => Vector2.Distance(visitor, self);
        }

        /// <summary>A painter standing at the edge of the view: 50 degrees left, 2.3 m out.</summary>
        static Sim Settled()
        {
            var sim = new Sim(EscortBrain.YawDirection(-50f) * 2.3f);
            sim.Wait(0.5f);
            Assert.AreEqual(EscortMode.Settled, sim.last.mode, "precondition: starts standing");
            return sim;
        }

        [Test]
        public void Glancing_round_moves_nobody()
        {
            // Look hard right, behind, hard left, each held for a second - shorter than the out-of-
            // view delay - and back. Head turns alone must never start a walk.
            var sim = Settled();
            foreach (float yaw in new[] { 90f, 180f, -120f, 0f, 150f, 0f })
            {
                sim.gaze = yaw;
                sim.Wait(1.0f);
            }
            Assert.IsFalse(sim.everMoved, "a glance moved the painter");
        }

        [Test]
        public void A_few_steps_do_not_start_a_follow()
        {
            // Shuffling 1.5 m must not send anyone after the visitor, and nothing moves at all
            // while they shuffle or in the moment after. (If the step leaves a painter at their
            // shoulder and they then stand, that painter strolls once back into view - the
            // out-of-view rule, tested separately.)
            var sim = Settled();
            sim.Walk(0f, 1.5f, 0.6f);
            sim.Wait(1.5f);
            Assert.IsFalse(sim.everMoved, "the painter moved during or just after a 1.5 m shuffle");
            sim.Wait(5f);
            Assert.IsFalse(sim.everTrailed, "a 1.5 m shuffle started a follow");
        }

        [Test]
        public void Walking_off_is_followed_from_behind()
        {
            var sim = Settled();
            sim.Walk(0f, 8f);
            Assert.AreEqual(EscortMode.Trailing, sim.last.mode);
            // Behind the visitor relative to the walk, i.e. out of sight while walking.
            Assert.Greater(sim.Angle, 120f, "trailing painter should be behind the visitor");
            Assert.Less(sim.Distance, 4.5f, "trailing painter fell too far behind");
        }

        [Test]
        public void After_a_walk_they_come_round_to_the_edge_of_the_view_and_stop()
        {
            var sim = Settled();
            sim.Walk(0f, 8f);
            sim.Wait(8f);
            Assert.AreEqual(EscortMode.Settled, sim.last.mode, "never settled after the visitor stopped");
            Assert.That(sim.Angle, Is.InRange(sim.s.minRestAngle - 1f, sim.s.maxRestAngle + 1f), "not in the periphery");
            Assert.That(sim.Distance, Is.InRange(sim.s.minRestDistance - sim.s.arriveRadius, sim.s.maxRestDistance + sim.s.arriveRadius),
                "stopped too close or too far");
            Assert.That(sim.Distance, Is.InRange(sim.s.restDistance - 0.5f, sim.s.restDistance + 0.5f),
                "open floor, yet not near the preferred distance");
        }

        [Test]
        public void Turning_away_for_good_brings_them_back_into_view()
        {
            var sim = Settled();
            sim.gaze = 150f;       // turn to look at something behind
            sim.Wait(10f);         // 2.5 s before they react, then ~4.5 m round to their own side
            Assert.IsTrue(sim.everMoved, "stayed out of view");
            Assert.AreEqual(EscortMode.Settled, sim.last.mode);
            Assert.AreEqual(ViewZone.Peripheral, EscortBrain.Classify(sim.Angle, sim.Distance, sim.s),
                $"ended at {sim.Angle:F0} degrees");
        }

        [Test]
        public void Standing_level_with_the_visitors_shoulder_counts_as_out_of_view()
        {
            // Found in Play Mode: a painter at 93 degrees counted as 'peripheral' and never came
            // back, though a headset shows only about 50 degrees either side.
            var sim = Settled();
            sim.gaze = -50f + 92f;   // the painter is now 92 degrees to the left
            sim.Wait(6f);
            Assert.IsTrue(sim.everMoved, "left standing at the visitor's shoulder, unseen");
            Assert.LessOrEqual(sim.Angle, sim.s.maxRestAngle + 1f);
        }

        [Test]
        public void Turning_to_talk_to_them_keeps_them_there_and_listening()
        {
            var sim = Settled();
            sim.gaze = EscortBrain.YawTo(sim.visitor, sim.self);   // look straight at them
            sim.Wait(10f);
            Assert.IsFalse(sim.everMoved, "stepped away from someone talking to them");
            Assert.IsTrue(sim.last.listening, "should listen when looked at from close by");
        }

        [Test]
        public void Standing_in_front_of_what_the_visitor_looks_at_they_step_aside()
        {
            var sim = Settled();
            // 22 degrees off the gaze: in front of the view, but not being looked at.
            sim.gaze = EscortBrain.YawTo(sim.visitor, sim.self) + 22f;
            sim.Wait(6f);
            Assert.IsTrue(sim.everMoved);
            Assert.GreaterOrEqual(sim.Angle, sim.s.centralCone, "still in front of the view");
        }

        [Test]
        public void A_talking_painter_holds_their_ground_when_out_of_view()
        {
            var sim = Settled();
            sim.holdGround = true;
            sim.gaze = 150f;
            sim.Wait(6f);
            Assert.IsFalse(sim.everMoved);
        }

        [Test]
        public void A_teleport_puts_them_behind_the_visitor_at_once()
        {
            var sim = Settled();
            sim.visitor = new Vector2(0f, 30f);
            sim.gaze = sim.heading = 0f;
            sim.Step();
            Assert.IsTrue(sim.last.warp);
            Assert.Less(sim.Distance, 3f);
            Assert.Greater(sim.Angle, 120f, "reappeared where the visitor could see it happen");
        }

        [Test]
        public void Rest_spots_never_block_a_work_of_art()
        {
            var s = new EscortSettings();
            var visitor = Vector2.zero;
            // A painting 5 m away at 50 degrees left - exactly the preferred standing angle.
            var work = EscortBrain.YawDirection(-50f) * 5f;
            Assert.IsTrue(EscortBrain.PickRestSpot(visitor, 0f, new Vector2(-2f, 0f), -1f, null,
                new[] { work }, s, null, out var spot));
            float offLine = Mathf.Abs(Mathf.DeltaAngle(EscortBrain.YawTo(visitor, spot), EscortBrain.YawTo(visitor, work)));
            Assert.GreaterOrEqual(offLine, s.artSightCone, "stands on the line of sight to the painting");
            Assert.GreaterOrEqual(Vector2.Distance(spot, work), s.artClearance);
        }

        [Test]
        public void Two_painters_never_pick_the_same_spot()
        {
            var s = new EscortSettings();
            Assert.IsTrue(EscortBrain.PickRestSpot(Vector2.zero, 0f, new Vector2(-2, -1), -1f, null, null, s, null, out var a));
            // The second painter wants the same side, and sees where the first is going.
            Assert.IsTrue(EscortBrain.PickRestSpot(Vector2.zero, 0f, new Vector2(-2, -1), -1f, new[] { a }, null, s, null, out var b));
            Assert.GreaterOrEqual(Vector2.Distance(a, b), s.separation);
        }

        [Test]
        public void Rest_spots_respect_the_floor()
        {
            var s = new EscortSettings();
            // Only the right-hand half-plane is walkable (a wall on the left).
            bool ok = EscortBrain.PickRestSpot(Vector2.zero, 0f, new Vector2(-2, 0), -1f, null, null, s,
                p => p.x > 0f, out var spot);
            Assert.IsTrue(ok);
            Assert.Greater(spot.x, 0f, "chose a spot inside the wall");
        }

        [Test]
        public void With_something_big_in_front_they_stand_wide_rather_than_stay_behind()
        {
            var s = new EscortSettings();
            // Everything more than 0.5 m ahead is solid, as when facing the Buddha's back.
            bool ok = EscortBrain.PickRestSpot(Vector2.zero, 0f, new Vector2(0f, -2f), -1f, null, null, s,
                p => p.y < 0.5f, out var spot);
            Assert.IsTrue(ok, "gave up with room to the sides");
            float angle = EscortBrain.ViewAngle(Vector2.zero, 0f, spot);
            Assert.Less(angle, s.outOfViewAngle, "the fallback spot is out of view");
            Assert.Less(spot.y, 0.5f);
        }

        [Test]
        public void With_nowhere_to_stand_aside_they_still_follow_a_visitor_who_walks_off()
        {
            // Found in Play Mode: the visitor walked into the Buddha's footprint, every step-aside
            // attempt failed, and each failure moved the anchor with the visitor - so walking 9 m
            // away never started a follow.
            var sim = Settled();
            sim.brain.Walkable = p => false;
            sim.gaze = EscortBrain.YawTo(sim.visitor, sim.self) + 20f;   // painter in the way
            sim.Wait(3f);
            sim.Walk(180f, 6f);
            Assert.IsTrue(sim.everTrailed, "never followed");
        }

        [Test]
        public void With_the_panel_open_the_painter_that_was_pointed_at_steps_aside_quickly()
        {
            var sim = Settled();
            sim.gaze = EscortBrain.YawTo(sim.visitor, sim.self);   // pointed straight at them
            sim.clearCentre = true;
            sim.Wait(3f);
            Assert.IsTrue(sim.everMoved, "stayed in front of the panel");
            Assert.GreaterOrEqual(sim.Angle, sim.s.centralCone, "still in the centre of the view");
            Assert.Less(sim.Angle, sim.s.outOfViewAngle, "stepped aside out of sight instead of to the edge");
        }

        [Test]
        public void With_the_panel_open_the_painter_speaking_stays_put()
        {
            var sim = Settled();
            sim.gaze = EscortBrain.YawTo(sim.visitor, sim.self);
            sim.clearCentre = true;
            sim.holdGround = true;   // talking
            sim.Wait(3f);
            Assert.IsFalse(sim.everMoved);
        }

        [Test]
        public void Asked_to_hold_mid_walk_they_stop_where_they_are()
        {
            // Found in the Editor (Saul, 29 Sep): a painter still stepping aside when his reply
            // came walked on through it, so the talk animation never played.
            var sim = Settled();
            sim.gaze = 150f;
            sim.Wait(2.6f);                       // out of view long enough to start walking round
            Assert.AreEqual(EscortMode.Repositioning, sim.last.mode, "precondition: walking");
            var at = sim.self;
            sim.holdGround = true;
            sim.Wait(2f);
            Assert.AreEqual(EscortMode.Settled, sim.last.mode);
            Assert.Less(Vector2.Distance(at, sim.self), 0.05f, "kept walking after being asked to hold");
        }

        [Test]
        public void While_holding_ground_a_few_steps_do_not_start_a_follow_but_walking_off_does()
        {
            var sim = Settled();
            sim.holdGround = true;
            sim.Walk(0f, 3.5f);                   // beyond the usual 2.2 m, inside the held 4.5 m
            sim.Wait(1f);
            Assert.IsFalse(sim.everTrailed, "followed a 3.5 m step while talking");
            sim.Walk(0f, 3f);                     // 6.5 m in all
            Assert.IsTrue(sim.everTrailed, "never followed a visitor who walked well off");
        }

        [Test]
        public void Yaw_convention_matches_Unity()
        {
            // The mistake CompanionFormation records: three.js and Unity differ by 180 degrees.
            foreach (float yaw in new[] { 0f, 45f, 90f, 200f })
            {
                var engine = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var ours = EscortBrain.YawDirection(yaw);
                Assert.AreEqual(engine.x, ours.x, 1e-4f);
                Assert.AreEqual(engine.z, ours.y, 1e-4f);
                Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, EscortBrain.YawTo(Vector2.zero, ours)), 1e-3f);
            }
        }
    }
}
