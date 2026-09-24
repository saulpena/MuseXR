using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// Rejecting collider fragments that are not wall.
    ///
    /// The case these exist for, measured on van-gogh-500k: aic-14556 hung at (5.1, 2.2, -14.9) on
    /// a surface the collider asserts and the capture does not visibly support. A single triangle
    /// in mid-air passes every per-hit test for a wall, because a per-hit test sees only one hit.
    /// </summary>
    public class WallSupportTests
    {
        /// <summary>A continuous wall facing +X, running down Z.</summary>
        static List<WallSurface> Wall(float x = -4f, float from = -8f, float to = 8f, float step = 1.6f)
        {
            var s = new List<WallSurface>();
            for (var z = from; z <= to; z += step) s.Add(new WallSurface(new Vector3(x, 2.2f, z), Vector3.right));
            return s;
        }

        [Test]
        public void AContinuousWallIsKeptWholesale()
        {
            var wall = Wall();
            Assert.AreEqual(wall.Count, WallSupport.Supported(wall).Count);
        }

        [Test]
        public void AFragmentAloneInMidAirIsDropped()
        {
            var surfaces = Wall();
            var fragment = new WallSurface(new Vector3(20f, 2.2f, 30f), Vector3.right);
            surfaces.Add(fragment);

            var kept = WallSupport.Supported(surfaces);

            Assert.AreEqual(surfaces.Count - 1, kept.Count);
            foreach (var k in kept)
                Assert.AreNotEqual(fragment.Point, k.Point, "an isolated triangle is not a wall");
        }

        [Test]
        public void ATinyClusterIsStillNotAWall()
        {
            // Two shards near each other give each other exactly one neighbour, which is not enough.
            var shards = new List<WallSurface>
            {
                new WallSurface(new Vector3(20f, 2.2f, 30f), Vector3.right),
                new WallSurface(new Vector3(20f, 2.2f, 31.5f), Vector3.right),
            };

            Assert.IsEmpty(WallSupport.Supported(shards));
        }

        [Test]
        public void TheFarSideOfAThinWallDoesNotSupportTheNearSide()
        {
            // Both faces of one wall are 0.2 m apart and face opposite ways. Without the normal
            // test each would prop the other up and a two-triangle shard would read as wall.
            var faces = new List<WallSurface>
            {
                new WallSurface(new Vector3(-4f, 2.2f, 0f), Vector3.right),
                new WallSurface(new Vector3(-4.2f, 2.2f, 0f), Vector3.left),
                new WallSurface(new Vector3(-4f, 2.2f, 1.6f), Vector3.right),
                new WallSurface(new Vector3(-4.2f, 2.2f, 1.6f), Vector3.left),
            };

            Assert.IsEmpty(WallSupport.Supported(faces),
                "opposite faces must not vouch for each other");
        }

        [Test]
        public void AParallelWallAcrossTheCorridorIsADifferentWall()
        {
            // Near, and facing the same way, and 3 m out of plane. Only the plane test catches it.
            var here = new WallSurface(new Vector3(0f, 2.2f, 0f), Vector3.right);
            var across = new List<WallSurface>
            {
                here,
                new WallSurface(new Vector3(3f, 2.2f, 0f), Vector3.right),
                new WallSurface(new Vector3(3f, 2.2f, 1.5f), Vector3.right),
            };

            Assert.AreEqual(0, WallSupport.Neighbours(across, 0),
                "3 m out of plane is another wall, however similar it looks");
        }

        [Test]
        public void ACornerDoesNotPropUpAStubOnTheOtherFace()
        {
            var corner = new List<WallSurface>
            {
                new WallSurface(new Vector3(0f, 2.2f, 0f), Vector3.right),      // the stub
                new WallSurface(new Vector3(0f, 2.2f, 1f), Vector3.forward),    // perpendicular
                new WallSurface(new Vector3(0f, 2.2f, 2f), Vector3.forward),
            };

            Assert.AreEqual(0, WallSupport.Neighbours(corner, 0),
                "a wall at right angles is not this wall");
        }

        [Test]
        public void AGentlyCurvedWallSurvives_BecauseScannedWallsAreNotFlat()
        {
            // A 20 m-radius bow: about 0.06 m of sagitta and 9 degrees of turn across 3.2 m, which
            // is what an undulating scanned wall actually looks like.
            var curved = new List<WallSurface>
            {
                new WallSurface(new Vector3(0f, 2.2f, 0f), Vector3.right),
                new WallSurface(new Vector3(0.032f, 2.2f, 1.6f), new Vector3(1f, 0f, 0.08f).normalized),
                new WallSurface(new Vector3(0.128f, 2.2f, 3.2f), new Vector3(1f, 0f, 0.16f).normalized),
            };

            Assert.AreEqual(curved.Count, WallSupport.Supported(curved).Count);
        }

        [Test]
        public void ASharpBendIsTwoWalls_NotOneCurvedOne()
        {
            // The same three points bent ~26 degrees instead of ~9. The far end is no longer in the
            // near end's plane at all, and treating it as one wall is how a work ends up round a
            // corner from where it was aimed.
            var bent = new List<WallSurface>
            {
                new WallSurface(new Vector3(0f, 2.2f, 0f), Vector3.right),
                new WallSurface(new Vector3(0.1f, 2.2f, 1.6f), new Vector3(1f, 0f, 0.3f).normalized),
                new WallSurface(new Vector3(0.2f, 2.2f, 3.2f), new Vector3(1f, 0f, 0.5f).normalized),
            };

            Assert.Less(WallSupport.Supported(bent).Count, bent.Count);
        }

        [Test]
        public void SupportIsSymmetric_SoTheANSWERDoesNotDependOnWhichEndYouAskFrom()
        {
            var curved = new List<WallSurface>
            {
                new WallSurface(new Vector3(0f, 2.2f, 0f), Vector3.right),
                new WallSurface(new Vector3(0.3f, 2.2f, 2.5f), new Vector3(1f, 0f, 0.25f).normalized),
            };

            Assert.AreEqual(WallSupport.Neighbours(curved, 0), WallSupport.Neighbours(curved, 1),
                "a wall cannot support its far end without its far end supporting it");
        }

        [Test]
        public void FilteringPreservesOrder_SoPlacementStaysDeterministic()
        {
            var surfaces = Wall();
            surfaces.Insert(3, new WallSurface(new Vector3(40f, 2.2f, 40f), Vector3.right));

            var a = WallSupport.Supported(surfaces);
            var b = WallSupport.Supported(surfaces);

            Assert.AreEqual(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++) Assert.AreEqual(a[i].Point, b[i].Point);
            for (var i = 1; i < a.Count; i++)
                Assert.LessOrEqual(surfaces.IndexOf(a[i - 1]), surfaces.IndexOf(a[i]));
        }

        [Test]
        public void NothingInMeansNothingOut_NotACrash()
        {
            Assert.IsEmpty(WallSupport.Supported(null));
            Assert.IsEmpty(WallSupport.Supported(new List<WallSurface>()));
            Assert.AreEqual(0, WallSupport.Neighbours(null, 0));
            Assert.AreEqual(0, WallSupport.Neighbours(Wall(), 99));
        }

        [Test]
        public void ADegenerateNormalIsHandledRatherThanThrowing()
        {
            var surfaces = new List<WallSurface> { new WallSurface(Vector3.zero, Vector3.zero) };
            surfaces.AddRange(Wall());

            Assert.DoesNotThrow(() => WallSupport.Supported(surfaces));
        }
    }
}
