using GaussianSplatting.Runtime;
using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class PortalGeometryTests
    {
        static readonly Vector2 Half = new(1.2f, 1.8f);   // a 2.4 x 3.6 m door

        [Test]
        public void WalkingStraightThroughTheDoorCrosses()
        {
            Assert.IsTrue(PortalGeometry.SegmentCrossesAperture(new Vector3(0, 0, -0.3f), new Vector3(0, 0, 0.3f), Half));
        }

        [Test]
        public void WalkingPastTheFrameDoesNotCross()
        {
            // Crosses the plane 2 m to the side of the door's centre: round it, not through it.
            Assert.IsFalse(PortalGeometry.SegmentCrossesAperture(new Vector3(2, 0, -0.3f), new Vector3(2, 0, 0.3f), Half));
            // Stays on one side.
            Assert.IsFalse(PortalGeometry.SegmentCrossesAperture(new Vector3(0, 0, -1f), new Vector3(0, 0, -0.1f), Half));
        }

        [Test]
        public void WhereTheSegmentMeetsThePlaneDecides()
        {
            // Both ends outside the door's width, but the line meets the plane inside it.
            Assert.IsTrue(PortalGeometry.SegmentCrossesAperture(new Vector3(-2, 0, -1), new Vector3(2, 0, 1), Half));
            // Both ends in line with the door, but the line meets the plane above it.
            Assert.IsFalse(PortalGeometry.SegmentCrossesAperture(new Vector3(0, 1, -1), new Vector3(0, 5, 1), Half));
        }

        [Test]
        public void OnlyTheFarSideIsEverSeenThrough()
        {
            var eye = new Vector3(0, 0, -3);
            Assert.IsTrue(PortalGeometry.SeenThroughAperture(eye, new Vector3(0, 0, 5), Half, 0f));
            Assert.IsFalse(PortalGeometry.SeenThroughAperture(eye, new Vector3(0, 0, -1), Half, 0f));
        }

        [Test]
        public void TheDoorFramesWhatIsSeenThroughIt()
        {
            var eye = new Vector3(0, 0, -2);
            // 2 m in front, straight ahead: through.
            Assert.IsTrue(PortalGeometry.SeenThroughAperture(eye, new Vector3(0.5f, 0, 6), Half, 0f));
            // Far off to the side behind the wall: the ray meets the plane at x = 2.5, outside.
            Assert.IsFalse(PortalGeometry.SeenThroughAperture(eye, new Vector3(10, 0, 6), Half, 0f));
            // ...unless the margin is grown past it.
            Assert.IsTrue(PortalGeometry.SeenThroughAperture(eye, new Vector3(10, 0, 6), Half, 1.5f));
        }

        [Test]
        public void ItWorksFromEitherSide()
        {
            // After the visitor crosses, the old world is what the door shows.
            var eye = new Vector3(0, 0, 2);
            Assert.IsTrue(PortalGeometry.SeenThroughAperture(eye, new Vector3(0, 0, -4), Half, 0f));
            Assert.IsFalse(PortalGeometry.SeenThroughAperture(eye, new Vector3(0, 0, 4), Half, 0f));
        }

        [Test]
        public void CrossingZoneIsInsideTheDoorAndCloseToThePlane()
        {
            Assert.IsTrue(PortalGeometry.InCrossingZone(new Vector3(0, 0, 0.05f), Half, 0.12f));
            Assert.IsFalse(PortalGeometry.InCrossingZone(new Vector3(0, 0, 0.5f), Half, 0.12f));
            Assert.IsFalse(PortalGeometry.InCrossingZone(new Vector3(3, 0, 0.05f), Half, 0.12f));
        }

        [Test]
        public void ApproachEyesAreAllInFrontOfTheDoorAtHeadHeight()
        {
            var eyes = PortalGeometry.ApproachEyes(Half, 8f, 3f, new[] { 1.2f, 1.9f }, 4, 3);
            Assert.AreEqual(5 * 4 * 2, eyes.Length);
            foreach (var e in eyes)
            {
                Assert.Less(e.z, 0f);
                Assert.GreaterOrEqual(e.z, -8f - 1e-4f);
                Assert.LessOrEqual(Mathf.Abs(e.x), 3f + 1e-4f);
                // Floor is the bottom of the aperture (y = -half.y in door space).
                float aboveFloor = e.y + Half.y;
                Assert.That(aboveFloor, Is.InRange(1.2f - 1e-4f, 1.9f + 1e-4f));
            }
        }

        [Test]
        public void VisibleFromAnyNeedsOnlyOneEye()
        {
            var eyes = new[] { new Vector3(-3, 0, -1), new Vector3(0, 0, -3) };
            // Seen only from the second eye: far behind the door, straight ahead.
            Assert.IsTrue(PortalGeometry.VisibleFromAny(eyes, new Vector3(0, 0, 10), Half, 0f));
            // On the approach side: never.
            Assert.IsFalse(PortalGeometry.VisibleFromAny(eyes, new Vector3(0, 0, -5), Half, 0f));
        }
    }

    public class PortalSequenceTests
    {
        static readonly Vector2 Half = new(1.2f, 1.8f);
        static readonly Vector3 Far = new(0, 0, -10);
        static readonly Vector3 Near = new(0.5f, 0, -2f);

        static PortalSequence Make() => new()
        {
            AppearSeconds = 1f, OpenSeconds = 2f, CloseSeconds = 1f, SeepSeconds = 4f,
            TriggerDistance = 3f, LookSeconds = 0.3f, PassableOpening = 0.6f
        };

        /// <summary>Asked to open, run until the door has appeared and is fully open.</summary>
        static PortalSequence OpenedDoor()
        {
            var s = Make();
            s.RequestOpen();
            for (int i = 0; i < 60 && s.Phase != PortalPhase.Open; ++i) s.Step(0.1f, Far, Far, Half);
            Assert.AreEqual(PortalPhase.Open, s.Phase);
            return s;
        }

        [Test]
        public void NothingAppearsUntilTheVisitorComesNearAndLooks()
        {
            var s = Make();
            Assert.AreEqual(PortalEvent.None, s.Step(0.1f, Far, Far, Half, looking: true));
            // Near but looking elsewhere: still nothing, however long.
            for (int i = 0; i < 20; ++i) Assert.AreEqual(PortalEvent.None, s.Step(0.1f, Near, Near, Half, looking: false));
            Assert.AreEqual(PortalPhase.Waiting, s.Phase);
            Assert.AreEqual(0f, s.Appear);
        }

        [Test]
        public void AGlanceIsNotEnoughALookIs()
        {
            var s = Make();
            s.Step(0.1f, Near, Near, Half, looking: true);
            s.Step(0.1f, Near, Near, Half, looking: false);   // looked away: the count restarts
            s.Step(0.1f, Near, Near, Half, looking: true);
            s.Step(0.1f, Near, Near, Half, looking: true);
            Assert.AreEqual(PortalPhase.Waiting, s.Phase);
            Assert.AreEqual(PortalEvent.StartedAppearing, s.Step(0.15f, Near, Near, Half, looking: true));
            Assert.AreEqual(PortalPhase.Appearing, s.Phase);
        }

        [Test]
        public void BeingNearButBehindTheDoorDoesNothing()
        {
            var s = Make();
            var behind = new Vector3(0, 0, 1f);
            for (int i = 0; i < 20; ++i) Assert.AreEqual(PortalEvent.None, s.Step(0.1f, behind, behind, Half));
        }

        [Test]
        public void ItAppearsBeforeItOpens()
        {
            var s = Make();
            s.RequestOpen();
            Assert.AreEqual(PortalEvent.StartedAppearing, s.Step(0.1f, Far, Far, Half));
            for (int i = 0; i < 5; ++i) Assert.AreEqual(PortalEvent.None, s.Step(0.1f, Far, Far, Half));
            Assert.That(s.Appear, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.AreEqual(0f, s.Opening);
            PortalEvent e = PortalEvent.None;
            for (int i = 0; i < 10 && e == PortalEvent.None; ++i) e = s.Step(0.1f, Far, Far, Half);
            Assert.AreEqual(PortalEvent.StartedOpening, e);
            Assert.AreEqual(1f, s.Appear);
        }

        [Test]
        public void OpeningBringsTheNextWorldsMusicAndLetsItSeepOut()
        {
            var s = Make();
            s.RequestOpen();
            for (int i = 0; i < 12; ++i) s.Step(0.1f, Far, Far, Half);   // appeared, opening
            Assert.AreEqual(PortalPhase.Opening, s.Phase);
            float music = s.MusicBlend, seep = s.Seep;
            for (int i = 0; i < 5; ++i) s.Step(0.1f, Far, Far, Half);
            Assert.Greater(s.MusicBlend, music);
            Assert.Greater(s.Seep, seep);
            Assert.That(s.MusicBlend, Is.EqualTo(s.Opening).Within(1e-4f));
        }

        [Test]
        public void WalkingIntoABarelyOpenDoorIsNotGoingThrough()
        {
            var s = Make();
            s.RequestOpen();
            for (int i = 0; i < 12; ++i) s.Step(0.1f, Far, Far, Half);   // about 5% open
            Assert.Less(s.Opening, 0.6f);
            Assert.AreEqual(PortalEvent.None, s.Step(0.01f, new Vector3(0, 0, -0.1f), new Vector3(0, 0, 0.1f), Half));
            Assert.AreNotEqual(PortalPhase.Closing, s.Phase);
        }

        [Test]
        public void GoingThroughShutsTheDoorBehindYouForGood()
        {
            var s = OpenedDoor();
            Assert.AreEqual(PortalEvent.Crossed, s.Step(0.01f, new Vector3(0, 0, -0.1f), new Vector3(0, 0, 0.1f), Half));
            Assert.AreEqual(PortalPhase.Closing, s.Phase);
            Assert.AreEqual(1f, s.MusicBlend);

            var inside = new Vector3(0, 0, 2f);
            PortalEvent last = PortalEvent.None;
            for (int i = 0; i < 20 && last != PortalEvent.Closed; ++i) last = s.Step(0.1f, inside, inside, Half);
            Assert.AreEqual(PortalEvent.Closed, last);
            Assert.AreEqual(PortalPhase.Done, s.Phase);
            Assert.AreEqual(0f, s.Opening);
            Assert.AreEqual(0f, s.Seep);
            Assert.AreEqual(1f, s.MusicBlend, "the music never goes back");

            // Nothing brings it back, not even asking.
            s.RequestOpen();
            Assert.AreEqual(PortalEvent.None, s.Step(0.1f, Near, Near, Half));
            Assert.AreEqual(PortalPhase.Done, s.Phase);
        }

        [Test]
        public void WalkingRoundTheFrameIsNotGoingThrough()
        {
            var s = OpenedDoor();
            Assert.AreEqual(PortalEvent.None, s.Step(0.01f, new Vector3(2.5f, 0, -0.1f), new Vector3(2.5f, 0, 0.1f), Half));
            Assert.AreEqual(PortalPhase.Open, s.Phase);
        }

        [Test]
        public void LookingIsJudgedOnTheFloorPlane()
        {
            var eye = new Vector3(0, 0, -4);
            Assert.IsTrue(PortalSequence.IsLookingAt(eye, new Vector3(0, 0, 1), 30f));
            // Looking up at a 12 m statue still counts: pitch is ignored.
            Assert.IsTrue(PortalSequence.IsLookingAt(eye, new Vector3(0, 0.9f, 0.3f), 30f));
            Assert.IsFalse(PortalSequence.IsLookingAt(eye, new Vector3(1, 0, 0), 30f));
            Assert.IsFalse(PortalSequence.IsLookingAt(eye, new Vector3(0, 0, -1), 30f));
        }

        [Test]
        public void EaseStartsAndEndsFlat()
        {
            Assert.AreEqual(0f, PortalSequence.Ease(0f));
            Assert.AreEqual(1f, PortalSequence.Ease(1f));
            Assert.That(PortalSequence.Ease(0.5f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.Less(PortalSequence.Ease(0.1f), 0.1f);
        }
    }

    public class RevealTests
    {
        static RevealSequence Make() => new()
        {
            DuskSeconds = 1f, RiseSeconds = 2f, SettleSeconds = 1f, LookSeconds = 0.3f,
            FrontFrom = 0f, FrontTo = 10f
        };

        static RevealEvent RunUntil(RevealSequence s, RevealPhase phase, int maxSteps = 100)
        {
            RevealEvent last = RevealEvent.None;
            for (int i = 0; i < maxSteps && s.Phase != phase; ++i) last = s.Step(0.1f, true, true);
            return last;
        }

        [Test]
        public void NightFallsOnlyWhenTheVisitorStandsThereAndLooksUp()
        {
            var s = Make();
            for (int i = 0; i < 10; ++i) Assert.AreEqual(RevealEvent.None, s.Step(0.1f, inPlace: true, lookingUp: false));
            for (int i = 0; i < 10; ++i) Assert.AreEqual(RevealEvent.None, s.Step(0.1f, inPlace: false, lookingUp: true));
            Assert.AreEqual(RevealPhase.Waiting, s.Phase);
            s.Step(0.1f, true, true); s.Step(0.1f, true, true);
            Assert.AreEqual(RevealEvent.StartedDusk, s.Step(0.15f, true, true));
        }

        [Test]
        public void ItRunsDuskThenRiseThenSettleAndNeverRepeats()
        {
            var s = Make();
            s.Request();
            Assert.AreEqual(RevealEvent.StartedDusk, s.Step(0.1f, false, false));
            Assert.AreEqual(RevealEvent.StartedRising, RunUntil(s, RevealPhase.Rising));
            Assert.AreEqual(1f, s.Night);
            Assert.AreEqual(0f, s.MusicBlend, "the music waits for the water");
            Assert.AreEqual(RevealEvent.FrontPassed, RunUntil(s, RevealPhase.Settling));
            Assert.AreEqual(1f, s.MusicBlend);
            Assert.AreEqual(1f, s.Dawn, "dawn is full as the front passes");
            Assert.AreEqual(RevealEvent.Finished, RunUntil(s, RevealPhase.Done));
            Assert.AreEqual(0f, s.Dawn);
            s.Request();
            Assert.AreEqual(RevealEvent.None, s.Step(0.1f, true, true));
        }

        [Test]
        public void TheFrontRisesSlowlyAtTheFloorAndFastAtTheTop()
        {
            var s = Make();
            s.Request();
            RunUntil(s, RevealPhase.Rising);
            Assert.AreEqual(0f, s.Front);
            for (int i = 0; i < 10; ++i) s.Step(0.1f, true, true);   // halfway through the rise
            Assert.That(s.Front, Is.EqualTo(2.5f).Within(1e-3f));    // a quarter of the height
            RunUntil(s, RevealPhase.Settling);
            Assert.AreEqual(10f, s.Front);
        }

        [Test]
        public void TheTwoWorldsShareTheBandAndNothingElse()
        {
            const float band = 2f;
            // Far above the front: all old world, no new.
            Assert.AreEqual(1f, SplatReveal.Presence(SplatRevealRole.Leaving, 5f, band));
            Assert.AreEqual(0f, SplatReveal.Presence(SplatRevealRole.Arriving, 5f, band));
            // Far below: the reverse.
            Assert.AreEqual(0f, SplatReveal.Presence(SplatRevealRole.Leaving, -5f, band));
            Assert.AreEqual(1f, SplatReveal.Presence(SplatRevealRole.Arriving, -5f, band));
            // On the front: half of each, and anywhere in the band they sum to one.
            Assert.AreEqual(0.5f, SplatReveal.Presence(SplatRevealRole.Leaving, 0f, band));
            foreach (var h in new[] { -0.9f, -0.3f, 0.2f, 0.8f })
                Assert.That(SplatReveal.Presence(SplatRevealRole.Leaving, h, band) +
                            SplatReveal.Presence(SplatRevealRole.Arriving, h, band), Is.EqualTo(1f).Within(1e-5f));
            Assert.AreEqual(1f, SplatReveal.Presence(SplatRevealRole.None, -5f, band));
        }
    }
}
