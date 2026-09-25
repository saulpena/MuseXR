using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests.EditMode
{
    public class CapturePoseTests
    {
        [Test]
        public void EyeSitsAboveTheOrigin()
        {
            var p = CapturePose.For(new Vector3(2f, 0.5f, -3f), Quaternion.identity);
            Assert.AreEqual(new Vector3(2f, 0.5f + CapturePose.EyeHeight, -3f), p.position);
        }

        [Test]
        public void KeepsTheOriginsYaw_AndDropsAnyTilt()
        {
            // A tilted origin must still give a level view: pitch and roll are what made two
            // captures of a resting headset disagree.
            var p = CapturePose.For(Vector3.zero, Quaternion.Euler(12f, 180f, -7f));
            var e = p.rotation.eulerAngles;
            Assert.AreEqual(0f, Mathf.DeltaAngle(e.x, 0f), 1e-3f, "pitch");
            Assert.AreEqual(0f, Mathf.DeltaAngle(e.z, 0f), 1e-3f, "roll");
            Assert.AreEqual(0f, Mathf.DeltaAngle(e.y, Quaternion.Euler(12f, 180f, -7f).eulerAngles.y), 1e-3f, "yaw");
        }

        [Test]
        public void SameOriginGivesTheSamePose()
        {
            var a = CapturePose.For(new Vector3(1, 0, 1), Quaternion.Euler(0, 42, 0));
            var b = CapturePose.For(new Vector3(1, 0, 1), Quaternion.Euler(0, 42, 0));
            Assert.AreEqual(a.position, b.position);
            Assert.AreEqual(a.rotation, b.rotation);
        }
    }
}
