using GaussianSplatting.Runtime;
using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class DesktopMotionTests
    {
        [Test]
        public void OffWheneverAnXrDeviceIsActive()
        {
            Assert.IsFalse(DesktopMotion.Active(xrDeviceActive: true));
            Assert.IsTrue(DesktopMotion.Active(xrDeviceActive: false));
        }

        [Test]
        public void ForwardFollowsTheYaw()
        {
            var north = DesktopMotion.Planar(Vector2.up, 0f, 1f, 1f);
            var east = DesktopMotion.Planar(Vector2.up, 90f, 1f, 1f);
            Assert.That(Vector3.Distance(north, Vector3.forward), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(east, Vector3.right), Is.LessThan(1e-4f));
        }

        [Test]
        public void StrafeIsSidewaysAndNeverVertical()
        {
            var step = DesktopMotion.Planar(Vector2.right, 180f, 2f, 0.5f);
            Assert.That(Vector3.Distance(step, Vector3.left), Is.LessThan(1e-4f));
            Assert.AreEqual(0f, step.y);
        }

        [Test]
        public void DiagonalIsNotFaster()
        {
            var diagonal = DesktopMotion.Planar(new Vector2(1f, 1f), 30f, 3f, 1f);
            Assert.AreEqual(3f, diagonal.magnitude, 1e-4f);
        }

        [Test]
        public void PitchStopsAtTheLimit()
        {
            Assert.AreEqual(10f, DesktopMotion.ClampPitchDelta(0f, 10f), 1e-4f);
            Assert.AreEqual(5f, DesktopMotion.ClampPitchDelta(75f, 30f), 1e-4f);
            Assert.AreEqual(-5f, DesktopMotion.ClampPitchDelta(-75f, -30f), 1e-4f);
        }
    }

    public class SortScheduleTests
    {
        [Test]
        public void ADifferentCameraAlwaysSorts()
        {
            // The Editor bug: the Scene view sorted, then the Game view drew with its order.
            for (int frame = 0; frame < 8; frame++)
                Assert.IsTrue(SortSchedule.ShouldSort(frame, 4, sameCameraAsLastSort: false), $"frame {frame}");
        }

        [Test]
        public void TheSameCameraKeepsTheSkipSchedule()
        {
            // The headset case T14 measured: one camera, sort on every 4th pass only.
            var sorted = new bool[8];
            for (int frame = 0; frame < 8; frame++) sorted[frame] = SortSchedule.ShouldSort(frame, 4, true);
            CollectionAssert.AreEqual(new[] { true, false, false, false, true, false, false, false }, sorted);
        }

        [Test]
        public void NthOfOneOrLessSortsEveryPass()
        {
            Assert.IsTrue(SortSchedule.ShouldSort(3, 1, true));
            Assert.IsTrue(SortSchedule.ShouldSort(3, 0, true));
        }
    }
}
