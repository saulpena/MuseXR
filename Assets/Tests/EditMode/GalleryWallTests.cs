using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// Wall placement, derived from each capture's bounds rather than her box-gallery constants.
    ///
    /// The case these exist for: our captures range from about 39 x 48 m
    /// (`elegant-floral-palace-interior`) to about 345 x 348 m (`grand-conservatory`). A fixed
    /// offset would hang half the works inside a wall and the rest in open sky.
    /// </summary>
    public class GalleryWallTests
    {
        static readonly Vector3 Min = new Vector3(-18f, -13f, -22f);   // van-gogh-500k, roughly
        static readonly Vector3 Max = new Vector3(17f, 26f, 20f);

        [Test]
        public void EveryWorkLandsInsideTheCapture()
        {
            var hung = GalleryWall.Lay(Min, Max, 0f, 8);
            foreach (var h in hung)
            {
                Assert.GreaterOrEqual(h.Position.x, Min.x, "left of the capture");
                Assert.LessOrEqual(h.Position.x, Max.x, "right of the capture");
                Assert.GreaterOrEqual(h.Position.z, Min.z);
                Assert.LessOrEqual(h.Position.z, Max.z);
            }
        }

        [Test]
        public void WorksHangAtHeadHeightAboveTheFLOOR_NotAboveTheBoundsFloor()
        {
            // The bounds of these captures run well below the walkable surface — that is the same
            // property that threw the spawn heuristic off by 44 m. Height must come from groundY.
            var hung = GalleryWall.Lay(Min, Max, groundY: 0f, count: 4);
            foreach (var h in hung)
                Assert.AreEqual(GalleryWall.DefaultHeight, h.Position.y, 1e-3f);

            var raised = GalleryWall.Lay(Min, Max, groundY: 5f, count: 4);
            foreach (var h in raised)
                Assert.AreEqual(5f + GalleryWall.DefaultHeight, h.Position.y, 1e-3f);
        }

        [Test]
        public void WorksAlternateBetweenTheTwoWalls()
        {
            var hung = GalleryWall.Lay(Min, Max, 0f, 6);
            // This capture is longer in Z, so the walls are the +X and -X sides.
            Assert.Less(hung[0].Position.x, 0f, "first work on one wall");
            Assert.Greater(hung[1].Position.x, 0f, "second on the other");
            Assert.Less(hung[2].Position.x, 0f);
        }

        [Test]
        public void TheLongAxisIsTheOneYouWalkDown()
        {
            // Wide-and-shallow: the walls should be the +Z/-Z sides instead.
            var hung = GalleryWall.Lay(new Vector3(-50f, 0f, -6f), new Vector3(50f, 10f, 6f), 0f, 4);
            Assert.Less(hung[0].Position.z, 0f);
            Assert.Greater(hung[1].Position.z, 0f);
            Assert.AreNotEqual(hung[0].Position.x, hung[2].Position.x, "works spread along X");
        }

        [Test]
        public void EachWorkFacesTheMiddleOfTheRoom()
        {
            var hung = GalleryWall.Lay(Min, Max, 0f, 4);
            var centre = (Min + Max) * 0.5f;
            foreach (var h in hung)
            {
                var toCentre = new Vector3(centre.x - h.Position.x, 0f, centre.z - h.Position.z).normalized;
                var facing = h.Rotation * Vector3.forward;
                Assert.Greater(Vector3.Dot(facing, toCentre), 0.9f,
                    "a work facing the wall it hangs on is unreadable");
            }
        }

        [Test]
        public void AHugeCaptureSpreadsWorksOutRatherThanCrowdingThem()
        {
            // grand-conservatory is ~345 x 348 m. Works should use that length.
            var hung = GalleryWall.Lay(new Vector3(-165f, -20f, -108f), new Vector3(180f, 292f, 237f), 0f, 8);
            // That capture is ~345 x 345, so which axis counts as "long" is a coin flip. Measure
            // the actual spread between the furthest two works on one wall instead of guessing.
            var sameWall = hung.Where(h => h.Index % 2 == 0).Select(h => h.Position).ToList();
            var spread = 0f;
            for (var i = 0; i < sameWall.Count; i++)
                for (var j = i + 1; j < sameWall.Count; j++)
                    spread = Mathf.Max(spread, Vector3.Distance(sameWall[i], sameWall[j]));
            Assert.Greater(spread, 50f, "a 345 m room should not hang everything in one corner");
        }

        [Test]
        public void ATinyCaptureStillKeepsWorksApart()
        {
            var hung = GalleryWall.Lay(new Vector3(-3f, 0f, -4f), new Vector3(3f, 4f, 4f), 0f, 6);
            var sameWall = hung.Where(h => h.Index % 2 == 0).Select(h => h.Position).ToList();
            for (var i = 1; i < sameWall.Count; i++)
                Assert.GreaterOrEqual(Vector3.Distance(sameWall[i - 1], sameWall[i]),
                    GalleryWall.MinSpacing - 1e-3f, "works must not overlap in a small room");
        }

        [Test]
        public void NoWorksMeansNoPlacement()
        {
            Assert.IsEmpty(GalleryWall.Lay(Min, Max, 0f, 0));
            Assert.IsEmpty(GalleryWall.Lay(Min, Max, 0f, -1));
        }

        [Test]
        public void TheTourVisitsTheNearestWorkFirst()
        {
            var hung = GalleryWall.Lay(Min, Max, 0f, 6);
            var from = new Vector3(3f, 0f, 0.5f);          // the measured spawn
            var order = GalleryWall.TourOrder(hung, from);

            Assert.AreEqual(hung.Count, order.Count);
            CollectionAssert.AllItemsAreUnique(order, "every work is a stop, exactly once");

            var last = -1f;
            foreach (var i in order)
            {
                var d = GalleryWall.GroundDistance(from, hung[i].Position);
                Assert.GreaterOrEqual(d, last - 1e-3f, "the walk moves outward, never doubles back");
                last = d;
            }
        }

        [Test]
        public void DistanceIgnoresHeight_BecauseTheVisitorWalksTheFloor()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(3f, 40f, 4f);
            Assert.AreEqual(5f, GalleryWall.GroundDistance(a, b), 1e-3f);
        }
    }
}
