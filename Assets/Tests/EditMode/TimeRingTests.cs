using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class TimeRingTests
    {
        [Test]
        public void AfternoonLeavesTheCaptureAsWorldLabsMadeIt()
        {
            Assert.IsTrue(TimeRing.Preset(TimeOfDay.Afternoon).LeavesSplatsAsCaptured);
            Assert.IsFalse(TimeRing.Preset(TimeOfDay.Mist).LeavesSplatsAsCaptured);
            Assert.IsFalse(TimeRing.Preset(TimeOfDay.Dusk).LeavesSplatsAsCaptured);
        }

        [Test]
        public void MistHazesAndDuskDarkens()
        {
            var mist = TimeRing.Preset(TimeOfDay.Mist);
            var dusk = TimeRing.Preset(TimeOfDay.Dusk);
            Assert.Greater(mist.hazeDensity, dusk.hazeDensity);
            Assert.Less(mist.saturation, 1f);
            Assert.Less(dusk.exposure, 1f);
            Assert.Less(dusk.splatTint.b, dusk.splatTint.r, "dusk is warm");
        }

        [Test]
        public void EaseTakesFourSecondsAndLandsOnThePreset()
        {
            var ring = new TimeRing(TimeOfDay.Afternoon);
            Assert.IsTrue(ring.Choose(TimeOfDay.Dusk));
            ring.Tick(2f);
            Assert.IsTrue(ring.IsEasing);
            Assert.Less(ring.Current.exposure, 1f);
            Assert.Greater(ring.Current.exposure, TimeRing.Preset(TimeOfDay.Dusk).exposure);
            ring.Tick(2f);
            Assert.IsFalse(ring.IsEasing);
            Assert.AreEqual(TimeRing.Preset(TimeOfDay.Dusk).exposure, ring.Current.exposure, 1e-5f);
        }

        [Test]
        public void TurningMidEaseStartsFromWhereTheLookIsNotWhereItWasHeading()
        {
            var ring = new TimeRing(TimeOfDay.Afternoon);
            ring.Choose(TimeOfDay.Mist);
            var halfway = ring.Tick(2f);
            ring.Choose(TimeOfDay.Dusk);
            var next = ring.Tick(0.01f);
            Assert.AreEqual(halfway.hazeDensity, next.hazeDensity, 0.002f, "no jump when the ring turns mid-ease");
        }

        [Test]
        public void ChoosingTheSameTimeIsNotATurn()
        {
            var ring = new TimeRing(TimeOfDay.Mist);
            Assert.IsFalse(ring.Choose(TimeOfDay.Mist));
            Assert.IsFalse(ring.IsEasing);
        }

        [Test]
        public void ArtworksTakeTheWorldsGradeAndAfternoonLeavesThemWhite()
        {
            Assert.AreEqual(Color.white, TimeRing.Preset(TimeOfDay.Afternoon).ArtworkTint);
            var dusk = TimeRing.Preset(TimeOfDay.Dusk).ArtworkTint;
            Assert.Less(dusk.g, 0.6f, "a painting at dusk is darker and warmer than at noon");
            Assert.Less(dusk.b, dusk.r);
        }

        [Test]
        public void NextWrapsRoundTheRing()
        {
            Assert.AreEqual(TimeOfDay.Afternoon, TimeRing.Next(TimeOfDay.Mist));
            Assert.AreEqual(TimeOfDay.Dusk, TimeRing.Next(TimeOfDay.Afternoon));
            Assert.AreEqual(TimeOfDay.Mist, TimeRing.Next(TimeOfDay.Dusk));
        }
    }
}
