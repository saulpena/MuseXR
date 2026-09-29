using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class DepthPanoTests
    {
        static void Near(Vector3 expected, Vector3 actual) =>
            Assert.Less((expected - actual).magnitude, 1e-4f, $"expected {expected}, got {actual}");

        [Test]
        public void TheCentreLooksForward() => Near(Vector3.forward, DepthPano.Direction(0.5f, 0.5f));

        [Test]
        public void TheTopRowLooksUpAndTheBottomRowDown()
        {
            Near(Vector3.up, DepthPano.Direction(0.5f, 0f));
            Near(Vector3.down, DepthPano.Direction(0.5f, 1f));
        }

        [Test]
        public void ThreeQuartersAcrossIsTheViewersRight()
        {
            Near(Vector3.right, DepthPano.Direction(0.75f, 0.5f));
            Near(Vector3.left, DepthPano.Direction(0.25f, 0.5f));
            Near(Vector3.back, DepthPano.Direction(0f, 0.5f));
        }

        [Test]
        public void NearIsWhiteFarIsBlackAndAMissIsBlack()
        {
            Assert.AreEqual(255, DepthPano.Encode(2f, 2f, 20f));
            Assert.AreEqual(0, DepthPano.Encode(20f, 2f, 20f));
            Assert.AreEqual(0, DepthPano.Encode(0f, 2f, 20f));
            Assert.Greater(DepthPano.Encode(3f, 2f, 20f), DepthPano.Encode(10f, 2f, 20f));
        }

        [Test]
        public void TheEncodingIsLogarithmic()
        {
            // The geometric mean of zMin and zMax sits halfway; a linear encoding would put 11 m there.
            Assert.AreEqual(128, DepthPano.Encode(Mathf.Sqrt(2f * 20f), 2f, 20f), 1);
        }

        [Test]
        public void DecodeRecoversDistanceToWithinOneStep()
        {
            // One byte step over ln(20/2) is ~0.9% of the distance.
            foreach (float d in new[] { 2f, 3.3f, 7f, 12.5f, 20f })
            {
                float back = DepthPano.Decode(DepthPano.Encode(d, 2f, 20f), 2f, 20f);
                Assert.Less(Mathf.Abs(back - d) / d, 0.01f, $"{d} m came back as {back} m");
            }
        }

        [Test]
        public void MeasuringABoxRoomFindsItsNearestAndFarthestSurfaces()
        {
            // Eye 1.6 m above the floor of a 20 x 19.2 x 15.4 m box: the DepthRoom shell.
            var min = new Vector3(-10f, 0f, -13.4f);
            var max = new Vector3(10f, 15.4f, 5.8f);
            var eye = new Vector3(0f, 1.6f, 0f);
            var r = DepthPano.Measure(d => InsideBox(eye, d, min, max), 128, 64);

            Assert.AreEqual(0, r.misses);
            Assert.That(r.zMin, Is.InRange(1.6f, 1.65f), "nearest is the floor straight below");
            float farthest = (new Vector3(10f, 15.4f, -13.4f) - eye).magnitude;
            Assert.That(r.zMax, Is.InRange(farthest * 0.97f, farthest), "farthest is a back top corner");
        }

        [Test]
        public void TheImageIsOrientedTheWayWorldLabsReadsIt()
        {
            // Floor below, and the front wall (+Z, 5.8 m) nearer than the back wall (-Z, 13.4 m):
            // the centre pixel must be brighter than the left/right edge pixel.
            var min = new Vector3(-10f, 0f, -13.4f);
            var max = new Vector3(10f, 15.4f, 5.8f);
            var eye = new Vector3(0f, 1.6f, 0f);
            var r = DepthPano.Measure(d => InsideBox(eye, d, min, max), 64, 32);
            var g = DepthPano.EncodeGray(r);
            byte centre = g[16 * 64 + 32], edge = g[16 * 64 + 0];
            byte bottom = g[31 * 64 + 32], top = g[0 * 64 + 32];
            Assert.Greater(centre, edge, "front wall (centre) is nearer than the back wall (edges)");
            Assert.Greater(bottom, top, "the floor (bottom) is nearer than the ceiling (top)");
        }

        [Test]
        public void EmptySpaceIsAllMisses()
        {
            var r = DepthPano.Measure(_ => 0f, 16, 8);
            Assert.AreEqual(16 * 8, r.misses);
            Assert.AreEqual(0f, r.zMax);
        }

        /// <summary>Distance from a point inside an axis-aligned box to its wall along a unit dir.</summary>
        static float InsideBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max)
        {
            float t = float.PositiveInfinity;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(d[a]) < 1e-7f) continue;
                float wall = d[a] > 0 ? max[a] : min[a];
                t = Mathf.Min(t, (wall - o[a]) / d[a]);
            }
            return t;
        }
    }
}
