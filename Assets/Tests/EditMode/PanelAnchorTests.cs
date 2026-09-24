using NUnit.Framework;
using UnityEngine;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// The reading panel's motion. These exist because the failure mode is motion sickness, which
    /// is not something a screenshot shows and not something anyone should have to feel twice.
    /// </summary>
    public class PanelAnchorTests
    {
        [Test]
        public void GlancingAroundDoesNotDisturbIt()
        {
            bool moving;
            var yaw = PanelAnchor.Follow(0f, 30f, following: false, deltaTime: 0.02f, stillFollowing: out moving);

            Assert.AreEqual(0f, yaw, 1e-3f, "30 degrees is looking, not turning");
            Assert.IsFalse(moving);
        }

        [Test]
        public void TurningAwayMakesItFollow()
        {
            bool moving;
            var yaw = PanelAnchor.Follow(0f, 90f, following: false, deltaTime: 0.02f, stillFollowing: out moving);

            Assert.IsTrue(moving);
            Assert.Greater(yaw, 0f, "it has started to swing round");
            Assert.Less(yaw, 90f, "but it eases, it does not snap");
        }

        [Test]
        public void ItKeepsMovingUntilItSettles_NotOnlyWhileTurnedFar()
        {
            // The whole point of the latch: once released it finishes the swing, rather than
            // stopping the instant the head comes back inside the release angle.
            bool moving;
            var yaw = PanelAnchor.Follow(0f, 20f, following: true, deltaTime: 0.02f, stillFollowing: out moving);

            Assert.IsTrue(moving);
            Assert.Greater(yaw, 0f);
        }

        [Test]
        public void TheGapBetweenReleaseAndSettleIsTheHysteresis()
        {
            Assert.Greater(PanelAnchor.ReleaseDegrees, PanelAnchor.SettleDegrees * 4f,
                "too narrow a gap and the panel chatters at exactly the angle a visitor holds");
        }

        [Test]
        public void ASwingConverges_AndThenStaysParked()
        {
            var yaw = 0f;
            var moving = false;
            for (var i = 0; i < 400; i++) yaw = PanelAnchor.Follow(yaw, 120f, moving, 0.02f, out moving);

            Assert.AreEqual(120f, yaw, 0.5f, "it arrives");
            Assert.IsFalse(moving, "and stops");

            var parked = PanelAnchor.Follow(yaw, 120f, moving, 0.02f, out moving);
            Assert.AreEqual(yaw, parked, 1e-3f);
            Assert.IsFalse(moving);
        }

        [Test]
        public void ItTurnsTheShortWayRoundThePole()
        {
            // 170 to -130 is 60 degrees clockwise THROUGH 180, or 300 the long way round. Naive
            // subtraction takes the long way and the panel sweeps past the visitor to get there.
            bool moving;
            var yaw = PanelAnchor.Follow(170f, -130f, following: false, deltaTime: 0.02f, stillFollowing: out moving);

            Assert.IsTrue(moving);
            Assert.Greater(yaw, 170f, "60 degrees clockwise, not 300 anticlockwise");
        }

        [Test]
        public void TwentyDegreesAcrossThePoleIsStillJustLookingAround()
        {
            bool moving;
            var yaw = PanelAnchor.Follow(170f, -170f, following: false, deltaTime: 0.02f, stillFollowing: out moving);

            Assert.AreEqual(170f, yaw, 1e-3f, "wrapping must not turn a glance into a turn");
            Assert.IsFalse(moving);
        }

        [Test]
        public void HeightIsConstant_SoLookingUpAndDownNeverDragsIt()
        {
            var low = PanelAnchor.Position(new Vector3(0f, 1.2f, 0f), 0f, floorY: 0f);
            var high = PanelAnchor.Position(new Vector3(0f, 1.9f, 0f), 0f, floorY: 0f);

            Assert.AreEqual(low.y, high.y, 1e-4f, "the head bobbed; the panel must not");
            Assert.AreEqual(PanelAnchor.Height, low.y, 1e-4f);
        }

        [Test]
        public void ItRidesTheFloor_SoAStaircaseStillReads()
        {
            var upstairs = PanelAnchor.Position(new Vector3(0f, 4.6f, 0f), 0f, floorY: 3f);
            Assert.AreEqual(3f + PanelAnchor.Height, upstairs.y, 1e-4f);
        }

        [Test]
        public void ItHangsTheReadingDistanceInFrontOfTheVisitor()
        {
            var at = PanelAnchor.Position(new Vector3(5f, 1.6f, -2f), 90f, 0f);

            Assert.AreEqual(5f + PanelAnchor.Distance, at.x, 1e-3f, "yaw 90 faces +X");
            Assert.AreEqual(-2f, at.z, 1e-3f);
        }

        [Test]
        public void ThePanelFacesAwayFromTheViewer_WhichIsWhatMakesTMPReadable()
        {
            // CLAUDE.md, measured twice: a flat thing reads when its own +Z points AWAY from the
            // viewer. A further 180 here is what mirrored the runtime panel before.
            var forward = PanelAnchor.Rotation(0f) * Vector3.forward;
            Assert.Greater(Vector3.Dot(forward, Vector3.forward), 0.99f);
        }
    
        // ------------------------------------------------------------------ the shake
        //
        // These are the checks that were missing. Every earlier test asked about YAW, and the
        // panel's yaw was never the problem: its ground position was read from the head every
        // frame. A test suite can be entirely green while the thing shakes, and this one was.

        [Test]
        public void StandingStillDoesNotMoveThePanelAtAll()
        {
            // A headset reports a few millimetres of tracking noise even on a tripod, and a person
            // standing still sways centimetres. None of it may reach the panel.
            var anchor = new Vector2(3f, -2f);
            var following = false;
            var rng = new System.Random(20260924);

            for (var i = 0; i < 600; i++)
            {
                var jitterX = (float)(rng.NextDouble() - 0.5) * 0.09f;   // +/- 45 mm
                var jitterZ = (float)(rng.NextDouble() - 0.5) * 0.09f;
                var moved = PanelAnchor.FollowGround(
                    anchor, new Vector2(3f + jitterX, -2f + jitterZ), following, 0.0111f, out following);

                Assert.AreEqual(anchor, moved, "a still visitor must not move the panel by any amount");
                Assert.IsFalse(following);
            }
        }

        [Test]
        public void LeaningAndSwayingIsStillNotWalking()
        {
            // A deliberate lean is far bigger than tracking noise and still is not leaving.
            var anchor = Vector2.zero;
            var following = false;
            var lean = PanelAnchor.FollowGround(
                anchor, new Vector2(0f, 0.5f), following, 0.02f, out following);

            Assert.AreEqual(anchor, lean);
            Assert.IsFalse(following);
        }

        [Test]
        public void WalkingAwayBringsThePanelWithYou()
        {
            var anchor = Vector2.zero;
            var head = new Vector2(0f, 4f);
            var following = false;

            var first = PanelAnchor.FollowGround(anchor, head, following, 0.02f, out following);
            Assert.IsTrue(following, "four metres is unambiguously walking");
            Assert.Greater(first.y, 0f, "it must start coming");

            var at = first;
            for (var i = 0; i < 400 && following; i++)
                at = PanelAnchor.FollowGround(at, head, following, 0.02f, out following);

            Assert.IsFalse(following, "it must park, not chase forever");
            Assert.AreEqual(head.x, at.x, 1e-4f);
            Assert.AreEqual(head.y, at.y, 1e-4f);
        }

        [Test]
        public void ItSlidesAtTheStatedPaceRatherThanTeleporting()
        {
            var following = false;
            var moved = PanelAnchor.FollowGround(
                Vector2.zero, new Vector2(10f, 0f), following, 0.1f, out following);

            Assert.AreEqual(PanelAnchor.MetresPerSecond * 0.1f, moved.x, 1e-4f);
        }

        [Test]
        public void OnceMovingItKeepsMovingBelowTheReleaseDistance()
        {
            // The hysteresis: having started, it does not stop the instant it is back inside
            // ReleaseMetres, or it would park half a step behind the visitor every time.
            var following = true;
            var offset = PanelAnchor.ReleaseMetres * 0.5f;
            var moved = PanelAnchor.FollowGround(
                Vector2.zero, new Vector2(offset, 0f), following, 0.02f, out following);

            Assert.IsTrue(following);
            Assert.Greater(moved.x, 0f);
        }

        [Test]
        public void TheReleaseDistanceIsComfortablyOutsideAnyStandingSway()
        {
            // The number itself is the check: a threshold inside a person's own sway is the bug.
            Assert.Greater(PanelAnchor.ReleaseMetres, 0.3f);
            Assert.Greater(PanelAnchor.ReleaseMetres, PanelAnchor.SettleMetres * 5f);
        }

        [Test]
        public void TheFloorIgnoresMicroBounceAndTakesARealStep()
        {
            // A CharacterController resting on a collider never settles exactly.
            Assert.AreEqual(1.2f, PanelAnchor.FollowFloor(1.2f, 1.2004f), 1e-6f);
            Assert.AreEqual(1.2f, PanelAnchor.FollowFloor(1.2f, 1.19f), 1e-6f);

            // A stair riser is 0.17 m or so and must be taken.
            Assert.AreEqual(1.37f, PanelAnchor.FollowFloor(1.2f, 1.37f), 1e-6f);
        }

        [Test]
        public void APanelHeightIsMeasuredFromTheFloorNotTheHead()
        {
            // The head bobs; the floor does not. Two different head heights over one floor must
            // put the panel at exactly the same height.
            var low = PanelAnchor.Position(new Vector3(0f, 1.2f, 0f), 0f, 0f);
            var high = PanelAnchor.Position(new Vector3(0f, 1.9f, 0f), 0f, 0f);

            Assert.AreEqual(low.y, high.y, 1e-6f);
            Assert.AreEqual(PanelAnchor.Height, low.y, 1e-6f);
        }

}
}
