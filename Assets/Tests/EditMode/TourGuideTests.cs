using NUnit.Framework;
using UnityEngine;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// The guided walk's compass, against muse-infinity's <c>emitTourUpdate</c>.
    ///
    /// The bearing is the part worth pinning: it is relative to where the visitor is LOOKING, so
    /// an arrow means "turn that way" rather than "north is over there". Getting the handedness
    /// wrong sends every visitor the opposite way and looks perfectly plausible in a screenshot.
    /// </summary>
    public class TourGuideTests
    {
        static readonly Vector3 Here = new Vector3(0f, 0f, 0f);

        [Test]
        public void SomethingStraightAheadReadsZero()
        {
            var bearing = TourGuide.Bearing(Here, 0f, new Vector3(0f, 0f, 10f));
            Assert.AreEqual(0f, bearing, 0.5f);
        }

        [Test]
        public void PositiveIsToTheRight_NegativeIsToTheLeft()
        {
            // Facing +Z. A target at +X is on the visitor's right.
            Assert.AreEqual(90f, TourGuide.Bearing(Here, 0f, new Vector3(10f, 0f, 0f)), 0.5f);
            Assert.AreEqual(-90f, TourGuide.Bearing(Here, 0f, new Vector3(-10f, 0f, 0f)), 0.5f);
        }

        [Test]
        public void TurningTheVisitorTurnsTheArrow()
        {
            var target = new Vector3(10f, 0f, 0f);

            Assert.AreEqual(90f, TourGuide.Bearing(Here, 0f, target), 0.5f, "target is to the right");
            Assert.AreEqual(0f, TourGuide.Bearing(Here, 90f, target), 0.5f, "now they face it");
            Assert.AreEqual(-90f, TourGuide.Bearing(Here, 180f, target), 0.5f, "now it is behind-left");
        }

        [Test]
        public void SomethingBehindYouIsHalfATurnEitherWay()
        {
            var behind = Mathf.Abs(TourGuide.Bearing(Here, 0f, new Vector3(0f, 0f, -10f)));
            Assert.AreEqual(180f, behind, 0.5f);
        }

        [Test]
        public void HeightIsIgnored_BecauseTheVisitorWalksTheFloor()
        {
            var low = TourGuide.Bearing(Here, 0f, new Vector3(5f, 0f, 5f));
            var high = TourGuide.Bearing(Here, 0f, new Vector3(5f, 40f, 5f));
            Assert.AreEqual(low, high, 1e-3f);
        }

        [Test]
        public void StandingOnTheStopDoesNotSpinTheArrow()
        {
            Assert.AreEqual(0f, TourGuide.Bearing(Here, 33f, Here), 1e-3f);
        }

        [Test]
        public void ArrivalIsHerThreePointFourMetres()
        {
            var near = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 3.0f), 0, 4);
            var far = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 4.0f), 0, 4);

            Assert.IsTrue(near.Arrived);
            Assert.IsFalse(far.Arrived);
            Assert.AreEqual(3.4f, TourGuide.ArrivedWithin, 1e-3f);
        }

        [Test]
        public void TheStepLineCountsFromOne_NotFromZero()
        {
            var walking = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 20f), 0, 4);
            Assert.AreEqual("WALK TO STOP 1 / 4", TourGuide.Step(walking));

            var arrived = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 1f), 2, 4);
            Assert.AreEqual("STOP 3 / 4 · YOU ARE HERE", TourGuide.Step(arrived));
        }

        [Test]
        public void TheHintIsDistanceWhileWalkingAndAnInstructionOnArrival()
        {
            var walking = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 17.2f), 0, 4,
                artist: "Claude Monet");
            StringAssert.Contains("17 M", TourGuide.Hint(walking));
            StringAssert.Contains("Claude Monet", TourGuide.Hint(walking));

            var arrived = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 1f), 0, 4,
                artist: "Claude Monet");
            StringAssert.Contains("TRIGGER", TourGuide.Hint(arrived));
            StringAssert.DoesNotContain("M ·", TourGuide.Hint(arrived));
        }

        [Test]
        public void AnUnknownArtistDoesNotLeaveADanglingSeparator()
        {
            var walking = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 9f), 0, 3);
            Assert.AreEqual("9 M", TourGuide.Hint(walking), "we have no artwork titles yet");
        }

        [Test]
        public void NoStopsMeansNoCompassRatherThanStopZeroOfZero()
        {
            var none = TourGuide.Describe(Here, 0f, Vector3.zero, 0, 0);
            Assert.IsFalse(none.HasStop);
            Assert.IsEmpty(TourGuide.Step(none));
            Assert.IsEmpty(TourGuide.Hint(none));
        }

        [Test]
        public void TheIndexIsClampedRatherThanRunningOffTheEnd()
        {
            // 5 m away, so still walking — the point here is the index, not the arrival.
            var past = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 5f), 99, 4);
            Assert.AreEqual(3, past.Index);
            Assert.AreEqual("WALK TO STOP 4 / 4", TourGuide.Step(past));

            var negative = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 5f), -7, 4);
            Assert.AreEqual(0, negative.Index);
        }

        [Test]
        public void TheHudOnlyRebuildsWhenSomethingVisibleChanged()
        {
            var a = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 10f), 0, 4);

            // A centimetre of walking and a degree of turning are not worth rebuilding TMP for.
            var twitch = TourGuide.Describe(new Vector3(0f, 0f, 0.02f), 1f, new Vector3(0f, 0f, 10f), 0, 4);
            Assert.IsFalse(TourGuide.Differs(a, twitch));

            var stepped = TourGuide.Describe(new Vector3(0f, 0f, 0.5f), 0f, new Vector3(0f, 0f, 10f), 0, 4);
            Assert.IsTrue(TourGuide.Differs(a, stepped), "half a metre shows on the distance");

            var turned = TourGuide.Describe(Here, 25f, new Vector3(0f, 0f, 10f), 0, 4);
            Assert.IsTrue(TourGuide.Differs(a, turned), "25 degrees moves the arrow visibly");
        }

        [Test]
        public void ArrivingAlwaysRebuilds_BecauseTheWholeLineChanges()
        {
            var almost = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 3.45f), 0, 4);
            var arrived = TourGuide.Describe(Here, 0f, new Vector3(0f, 0f, 3.35f), 0, 4);

            Assert.IsFalse(almost.Arrived);
            Assert.IsTrue(arrived.Arrived);
            Assert.IsTrue(TourGuide.Differs(almost, arrived));
        }
    }
}
