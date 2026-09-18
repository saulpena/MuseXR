using MusePico.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The formation maths, checked against the behaviour described in
    /// <c>muse-infinity/lib/museum3d.js:updateCompanions</c> — so a port regression shows up here
    /// rather than as three masters standing in a wall on a headset.
    /// </summary>
    public class CompanionFormationTests
    {
        const float Tol = 0.001f;

        [Test]
        public void A_negative_back_slot_is_ahead_of_the_visitor_in_UNITYS_basis()
        {
            // The test that should have caught the port bug and did not, because it was written
            // against the JS formula instead of against Unity. At yaw 0 Unity's forward is +Z, so
            // back = -2.7 must put the master at z = +2.7. The three.js convention gives -2.7.
            var t = CompanionFormation.SlotTarget(Vector3.zero, 0f, new Vector2(-2.7f, 0f));
            Assert.AreEqual(0f, t.x, Tol);
            Assert.AreEqual(2.7f, t.y, Tol, "ahead at yaw 0 is +Z in Unity");
        }

        [Test]
        public void A_slot_ahead_agrees_with_the_transform_it_is_standing_in_front_of()
        {
            // Basis-independent version of the above: whatever the convention, the target must lie
            // along the same transform's forward that the engine itself would produce. This is the
            // assertion that cannot be satisfied by a wrong-but-self-consistent port.
            foreach (float yawDeg in new[] { 0f, 37f, 90f, 180f, 274f })
            {
                var rot = Quaternion.Euler(0f, yawDeg, 0f);
                var expected = rot * new Vector3(0f, 0f, 2.7f);
                var t = CompanionFormation.SlotTarget(Vector3.zero, yawDeg * Mathf.Deg2Rad,
                                                      new Vector2(-2.7f, 0f));
                Assert.AreEqual(expected.x, t.x, 0.01f, $"x at yaw {yawDeg}");
                Assert.AreEqual(expected.z, t.y, 0.01f, $"z at yaw {yawDeg}");
            }
        }

        [Test]
        public void A_side_slot_agrees_with_the_transforms_right()
        {
            foreach (float yawDeg in new[] { 0f, 37f, 90f, 180f, 274f })
            {
                var rot = Quaternion.Euler(0f, yawDeg, 0f);
                var expected = rot * new Vector3(1.6f, 0f, 0f);
                var t = CompanionFormation.SlotTarget(Vector3.zero, yawDeg * Mathf.Deg2Rad,
                                                      new Vector2(0f, 1.6f));
                Assert.AreEqual(expected.x, t.x, 0.01f, $"x at yaw {yawDeg}");
                Assert.AreEqual(expected.z, t.y, 0.01f, $"z at yaw {yawDeg}");
            }
        }

        [Test]
        public void A_positive_side_slot_is_to_the_visitors_right()
        {
            // yaw 0: right is (cos 0, sin 0) = (+1, 0) = +X.
            var t = CompanionFormation.SlotTarget(Vector3.zero, 0f, new Vector2(0f, 1.6f));
            Assert.AreEqual(1.6f, t.x, Tol);
            Assert.AreEqual(0f, t.y, Tol);
        }

        [Test]
        public void Turning_the_visitor_carries_the_whole_formation_round()
        {
            // The point of the mechanism: the slot is rigid in the visitor's frame, so a 90 degree
            // turn rotates every target about them. At yaw 90 Unity faces +X.
            var slot = new Vector2(-2.7f, 0f);
            var t = CompanionFormation.SlotTarget(Vector3.zero, 90f * Mathf.Deg2Rad, slot);
            Assert.AreEqual(2.7f, t.x, Tol, "facing +X, ahead is +X");
            Assert.AreEqual(0f, t.y, Tol);
        }

        [Test]
        public void The_formation_travels_with_the_visitor()
        {
            var slot = new Vector2(-2.7f, 1.6f);
            var atOrigin = CompanionFormation.SlotTarget(Vector3.zero, 0f, slot);
            var moved = CompanionFormation.SlotTarget(new Vector3(10f, 0f, -5f), 0f, slot);
            Assert.AreEqual(atOrigin.x + 10f, moved.x, Tol);
            Assert.AreEqual(atOrigin.y - 5f, moved.y, Tol);
        }

        [Test]
        public void The_three_default_slots_are_the_playtested_ones()
        {
            // These came out of acceptance feedback #11 and are worth pinning: pulled from 1.6 m
            // to 2.7-3.4 m because at point blank the figures filled the frame.
            Assert.AreEqual(3, CompanionFormation.DefaultSlots.Length);
            Assert.AreEqual(new Vector2(-2.7f, -1.6f), CompanionFormation.DefaultSlots[0]);
            Assert.AreEqual(new Vector2(-2.7f, 1.6f), CompanionFormation.DefaultSlots[1]);
            Assert.AreEqual(new Vector2(-3.4f, 0f), CompanionFormation.DefaultSlots[2]);
        }

        [Test]
        public void Damping_at_sixty_fps_matches_the_originals_constant()
        {
            // The port must not change the follow distance, which is the entire feel of this.
            float f = CompanionFormation.DampFactor(0.08f, 1f / 60f);
            Assert.AreEqual(0.08f, f, 0.001f);
        }

        [Test]
        public void Damping_is_frame_rate_independent()
        {
            // One step of 1/30 s must close the same fraction as two steps of 1/60 s, or a Quest
            // at 72 Hz and an editor at 160 would follow at visibly different distances.
            float oneBig = CompanionFormation.DampFactor(0.08f, 1f / 30f);
            float small = CompanionFormation.DampFactor(0.08f, 1f / 60f);
            float twoSmall = 1f - (1f - small) * (1f - small);
            Assert.AreEqual(twoSmall, oneBig, 0.0005f);
        }

        [Test]
        public void A_slot_outside_the_walk_box_is_pulled_back_in()
        {
            var box = new Vector4(-2.90f, 2.71f, -29.64f, 22.10f); // the Sunlit gallery
            var clamped = CompanionFormation.ClampToBounds(new Vector2(50f, 100f), box);
            Assert.AreEqual(2.71f - CompanionFormation.BoundsInset, clamped.x, Tol);
            Assert.AreEqual(22.10f - CompanionFormation.BoundsInset, clamped.y, Tol);
        }

        [Test]
        public void A_box_narrower_than_the_inset_collapses_to_its_centre_instead_of_inverting()
        {
            var narrow = new Vector4(-0.1f, 0.1f, -0.1f, 0.1f);
            var clamped = CompanionFormation.ClampToBounds(new Vector2(9f, -9f), narrow);
            Assert.AreEqual(0f, clamped.x, Tol);
            Assert.AreEqual(0f, clamped.y, Tol);
        }

        [Test]
        public void Walking_into_a_wall_does_not_put_a_master_inside_the_visitor()
        {
            // The measured failure: visitor pinned at the end wall (z -28.61), slots wanted z -32,
            // the bounds clamp pinned everyone to the box edge and Socrates ended 0.68 m away.
            var box = new Vector4(-2.90f, 2.71f, -29.64f, 22.10f);
            var visitor = new Vector3(-0.48f, 0.19f, -28.61f);
            var slot = new Vector2(-3.4f, 0f);                 // the centre slot, the worst offender

            var clamped = CompanionFormation.ClampToBounds(
                CompanionFormation.SlotTarget(visitor, Mathf.PI, slot), box);
            float before = Vector2.Distance(clamped, new Vector2(visitor.x, visitor.z));
            Assert.Less(before, 1.0f, "precondition: the clamp really does pull them into the visitor");

            var pushed = CompanionFormation.PushOutOfPersonalSpace(clamped, visitor, slot);
            float after = Vector2.Distance(pushed, new Vector2(visitor.x, visitor.z));
            Assert.GreaterOrEqual(after, CompanionFormation.PersonalSpace - 0.001f,
                "a master must never be closer than personal space, whatever the walk box says");
        }

        [Test]
        public void Personal_space_leaves_a_comfortable_slot_alone()
        {
            var visitor = new Vector3(0f, 0f, 0f);
            var comfortable = new Vector2(0f, 2.7f);
            var result = CompanionFormation.PushOutOfPersonalSpace(comfortable, visitor, new Vector2(-2.7f, 0f));
            Assert.AreEqual(comfortable.x, result.x, Tol);
            Assert.AreEqual(comfortable.y, result.y, Tol);
        }

        [Test]
        public void A_master_exactly_on_the_visitor_is_pushed_somewhere_definite()
        {
            // Degenerate case: no direction to push along, so the slot's own side decides, and the
            // two flanking masters must not stack on the same spot.
            var visitor = Vector3.zero;
            var onTop = new Vector2(0f, 0f);
            var left  = CompanionFormation.PushOutOfPersonalSpace(onTop, visitor, new Vector2(-2.7f, -1.6f));
            var right = CompanionFormation.PushOutOfPersonalSpace(onTop, visitor, new Vector2(-2.7f, 1.6f));
            Assert.AreEqual(CompanionFormation.PersonalSpace, left.magnitude, 0.001f);
            Assert.AreEqual(CompanionFormation.PersonalSpace, right.magnitude, 0.001f);
            Assert.AreNotEqual(left.x, right.x, "they must not end up in the same place");
        }

        [Test]
        public void Two_masters_on_the_same_spot_are_pushed_apart()
        {
            // Saul, on device: "I wanna make sure they do not go on top of each other." They do
            // converge, because clamped slots pile up at a box edge.
            var targets = new[] { new Vector2(0f, 3f), new Vector2(0.05f, 3f) };
            var slots = new[] { new Vector2(-2.7f, -1.6f), new Vector2(-2.7f, 1.6f) };
            CompanionFormation.Separate(targets, Vector3.zero, slots);
            Assert.GreaterOrEqual(Vector2.Distance(targets[0], targets[1]),
                                  CompanionFormation.CompanionSeparation - 0.01f);
        }

        [Test]
        public void Three_masters_all_end_up_apart_not_just_the_first_pair()
        {
            // The reason it relaxes instead of doing one pass: separating A from B can push B into C.
            var targets = new[] { new Vector2(0f, 3f), new Vector2(0.1f, 3f), new Vector2(-0.1f, 3f) };
            var slots = CompanionFormation.DefaultSlots;
            CompanionFormation.Separate(targets, Vector3.zero, slots);
            for (int i = 0; i < targets.Length; i++)
                for (int j = i + 1; j < targets.Length; j++)
                    Assert.GreaterOrEqual(Vector2.Distance(targets[i], targets[j]),
                                          CompanionFormation.CompanionSeparation - 0.05f,
                                          $"pair {i},{j}");
        }

        [Test]
        public void Separation_never_pushes_a_master_into_the_visitor()
        {
            // Separation runs before personal space for exactly this reason — shoving two apart
            // can send one toward the visitor, so the radius has to be re-applied afterwards.
            var targets = new[] { new Vector2(0f, 1.45f), new Vector2(0f, 1.5f) };
            var slots = new[] { new Vector2(-1.5f, 0f), new Vector2(-1.5f, 0f) };
            CompanionFormation.Separate(targets, Vector3.zero, slots, 1.2f, CompanionFormation.PersonalSpace);
            foreach (var t in targets)
                Assert.GreaterOrEqual(t.magnitude, CompanionFormation.PersonalSpace - 0.01f);
        }

        [Test]
        public void Masters_already_far_apart_are_left_where_they_are()
        {
            var targets = new[] { new Vector2(-1.6f, 2.7f), new Vector2(1.6f, 2.7f) };
            var before = new[] { targets[0], targets[1] };
            CompanionFormation.Separate(targets, Vector3.zero, CompanionFormation.DefaultSlots);
            Assert.AreEqual(before[0].x, targets[0].x, 0.001f);
            Assert.AreEqual(before[1].x, targets[1].x, 0.001f);
        }

        [Test]
        public void A_master_never_stands_more_than_a_step_above_the_visitor()
        {
            // The mezzanine-fragment bug from the original: a collider shard above the real floor
            // sat inside the accept window and lifted a companion into the air.
            float lifted = CompanionFormation.ClampGroundToVisitor(4f, 0f);
            Assert.AreEqual(CompanionFormation.MaxAboveVisitor, lifted, Tol);
        }

        [Test]
        public void A_master_may_stand_below_the_visitor()
        {
            // Sunken rooms are walkable; only the upward direction is suspect.
            Assert.AreEqual(-1.5f, CompanionFormation.ClampGroundToVisitor(-1.5f, 0f), Tol);
        }

        [Test]
        public void Facing_yaw_turns_a_master_toward_the_visitor()
        {
            var master = new Vector3(0f, 0f, 0f);
            Assert.AreEqual(0f, CompanionFormation.FacingYawDegrees(master, new Vector3(0f, 0f, 5f)), 0.01f);
            Assert.AreEqual(90f, CompanionFormation.FacingYawDegrees(master, new Vector3(5f, 0f, 0f)), 0.01f);
            Assert.AreEqual(180f, CompanionFormation.FacingYawDegrees(master, new Vector3(0f, 0f, -5f)), 0.01f);
        }

        [Test]
        public void Body_yaw_takes_the_short_way_round_the_wrap()
        {
            // 350 -> 10 must go forwards through 0, not backwards through 180.
            float stepped = CompanionFormation.StepBodyYaw(350f, 10f, 1.2f, 1f / 60f);
            Assert.Greater(Mathf.DeltaAngle(350f, stepped), 0f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(350f, stepped)), 5f, "and only a little of it per frame");
        }

        [Test]
        public void A_glance_barely_moves_the_formation()
        {
            // The VR departure, pinned: a 60 degree head turn held for a single frame must not
            // swing the party. At 1.2/s over 1/60 s that is ~2% of the delta.
            float stepped = CompanionFormation.StepBodyYaw(0f, 60f, 1.2f, 1f / 60f);
            Assert.Less(stepped, 2f, "a quick look should not drag three people across the view");
        }

        [Test]
        public void A_sustained_turn_does_bring_them_round()
        {
            float yaw = 0f;
            for (int i = 0; i < 120; i++) yaw = CompanionFormation.StepBodyYaw(yaw, 60f, 1.2f, 1f / 60f);
            Assert.Greater(yaw, 50f, "after two seconds of holding the turn they should have followed");
        }

        [Test]
        public void Height_normalising_scales_a_model_to_human_height()
        {
            Assert.AreEqual(0.875f, CompanionFormation.HeightNormalisingScale(2f), Tol);
            Assert.AreEqual(1.75f, CompanionFormation.HeightNormalisingScale(1f), Tol);
        }

        [Test]
        public void A_degenerate_model_height_does_not_produce_an_infinite_scale()
        {
            Assert.AreEqual(1f, CompanionFormation.HeightNormalisingScale(0f), Tol);
            Assert.AreEqual(1f, CompanionFormation.HeightNormalisingScale(float.NaN), Tol);
        }
    }
}
