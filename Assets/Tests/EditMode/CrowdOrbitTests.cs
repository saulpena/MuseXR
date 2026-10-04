using MuseXR.Slots;
using NUnit.Framework;

namespace MusePico.Tests
{
    public class CrowdOrbitTests
    {
        [Test]
        public void APlaceInTheGazeIsPushedToTheSideItIsOn()
        {
            Assert.GreaterOrEqual(CrowdOrbit.Delta(0f, CrowdOrbit.AvoidGaze(20f, 0f, 20f)), CrowdOrbit.GazeHalfAngle);
            Assert.LessOrEqual(CrowdOrbit.Delta(0f, CrowdOrbit.AvoidGaze(-20f, 0f, -20f)), -CrowdOrbit.GazeHalfAngle);
        }

        [Test]
        public void APlaceOutsideTheGazeIsLeftAlone() => Assert.AreEqual(70f, CrowdOrbit.AvoidGaze(70f, 0f, 70f));

        [Test]
        public void OneTheGazeSweepsOntoSlipsOutToItsOwnSide()
        {
            var yaw = 10f;   // just right of the gaze
            for (var i = 0; i < 30; i++) yaw = CrowdOrbit.Step(yaw, 60f, 0f, 2f, 8f);
            Assert.IsFalse(CrowdOrbit.InGaze(yaw, 0f));
            Assert.Greater(CrowdOrbit.Delta(0f, yaw), 0f);
        }

        [Test]
        public void CrossingSidesGoesRoundTheBackNeverTheFront()
        {
            // From the right (+60) to the left (-60): every step stays out of the gaze cone.
            var yaw = 60f;
            for (var i = 0; i < 200; i++)
            {
                yaw = CrowdOrbit.Step(yaw, -60f, 0f, 3f, 8f);
                Assert.IsFalse(CrowdOrbit.InGaze(yaw, 0f), "stepped in front at " + yaw);
            }
            Assert.AreEqual(-60f, CrowdOrbit.Delta(0f, yaw), 0.01f);
        }

        [Test]
        public void ARadiusNeverComesInsideTheMinimum() =>
            Assert.AreEqual(CrowdOrbit.MinRadius, CrowdOrbit.StepRadius(1.5f, 0.2f, 5f));

        [Test]
        public void DeltaWrapsBothWays()
        {
            Assert.AreEqual(-20f, CrowdOrbit.Delta(10f, 350f), 1e-4f);
            Assert.AreEqual(20f, CrowdOrbit.Delta(350f, 10f), 1e-4f);
            Assert.AreEqual(180f, CrowdOrbit.Delta(0f, 180f), 1e-4f);
        }
    }
}
