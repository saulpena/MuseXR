using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// Hanging on the walls a capture actually has.
    ///
    /// The case these exist for, measured on van-gogh-500k: sweeping the +X side of her walk box at
    /// picture height found no wall within 30 m for z -20 to +4. Four of eight works were hanging
    /// outside the building. The box says where you may stand, not what you can see.
    /// </summary>
    public class WallGalleryTests
    {
        /// <summary>One long wall facing +X, running down Z — the shape van-gogh actually has.</summary>
        static List<WallSurface> OneLongWall(float from = -20f, float to = 20f, float step = 1f)
        {
            var s = new List<WallSurface>();
            for (var z = from; z <= to; z += step)
                s.Add(new WallSurface(new Vector3(-4f, 2.2f, z), Vector3.right));
            return s;
        }

        [Test]
        public void WorksHangOnTheWallAndFaceTheRoom()
        {
            var hung = WallGallery.Lay(OneLongWall(), Vector3.zero, 2.2f, 4);

            Assert.AreEqual(4, hung.Count);
            foreach (var h in hung)
            {
                Assert.AreEqual(-4f + WallGallery.Inset, h.Position.x, 1e-3f, "seated just off the surface");
                Assert.Greater(Vector3.Dot(h.Rotation * Vector3.forward, Vector3.right), 0.9f);
            }
        }

        [Test]
        public void HeightIsAbsolute_BecauseAScannedWallLeans()
        {
            var leaning = new List<WallSurface>
            {
                new WallSurface(new Vector3(-4f, 0.4f, -10f), Vector3.right),
                new WallSurface(new Vector3(-4f, 5.9f, 10f), Vector3.right),
            };

            foreach (var h in WallGallery.Lay(leaning, Vector3.zero, 2.2f, 2))
                Assert.AreEqual(2.2f, h.Position.y, 1e-3f);
        }

        [Test]
        public void TheWorksSpreadOutRatherThanClumping()
        {
            var hung = WallGallery.Lay(OneLongWall(), Vector3.zero, 2.2f, 5);

            for (var i = 0; i < hung.Count; i++)
                for (var j = i + 1; j < hung.Count; j++)
                    Assert.GreaterOrEqual(
                        Vector3.Distance(hung[i].Position, hung[j].Position),
                        WallGallery.MinSpacing - 1e-3f,
                        "farthest-point sampling must keep them apart");
        }

        [Test]
        public void AShortWallReturnsFewerWorksRatherThanCrowdingThem()
        {
            // 4 m of wall cannot carry eight works at 3 m spacing; the caller tops up from the box.
            var stub = OneLongWall(-2f, 2f, 0.5f);

            var hung = WallGallery.Lay(stub, Vector3.zero, 2.2f, 8);

            Assert.Less(hung.Count, 8, "a full wall must say it is full");
            Assert.GreaterOrEqual(hung.Count, 1);
        }

        [Test]
        public void IndexOrderIsWalkOrder_SoTheTourHudCounts1ToN()
        {
            var from = new Vector3(0f, 0f, -18f);
            var hung = WallGallery.Lay(OneLongWall(), from, 2.2f, 4);

            var last = -1f;
            foreach (var h in hung)
            {
                var d = GalleryWall.GroundDistance(from, h.Position);
                Assert.GreaterOrEqual(d, last - 1e-3f, "the walk moves outward, never doubles back");
                last = d;
                Assert.AreEqual(hung.IndexOf(h), h.Index, "index must match the stop number");
            }
        }

        [Test]
        public void TheFirstWorkIsTheOneTheVisitorIsStandingBy()
        {
            var from = new Vector3(0f, 0f, 15f);
            var hung = WallGallery.Lay(OneLongWall(), from, 2.2f, 3);

            Assert.AreEqual(15f, hung[0].Position.z, 1.5f,
                "seeded at the surface nearest the spawn");
        }

        [Test]
        public void SelectionIsDeterministic_SoACaptureAlwaysHangsTheSameWay()
        {
            var a = WallGallery.Lay(OneLongWall(), Vector3.zero, 2.2f, 5);
            var b = WallGallery.Lay(OneLongWall(), Vector3.zero, 2.2f, 5);

            for (var i = 0; i < a.Count; i++) Assert.AreEqual(a[i].Position, b[i].Position);
        }

        [Test]
        public void JunkGeometryAtTheVisitorsFeetIsNotAWall()
        {
            // Measured on van-gogh-500k: probing from the spawn returns hits at 0.1-0.2 m in
            // almost every direction. They pass every geometric test for a wall and are not one.
            var junk = new List<WallSurface>
            {
                new WallSurface(new Vector3(0.2f, 2.2f, 0.1f), Vector3.right),
                new WallSurface(new Vector3(-0.3f, 2.2f, 0.2f), Vector3.left),
            };
            junk.AddRange(OneLongWall());

            var hung = WallGallery.Lay(junk, Vector3.zero, 2.2f, 4);

            Assert.AreEqual(4, hung.Count);
            foreach (var h in hung)
                Assert.GreaterOrEqual(GalleryWall.GroundDistance(Vector3.zero, h.Position),
                    WallGallery.MinFromVisitor - 1e-3f,
                    "you cannot look at a painting from 30 cm");
        }

        [Test]
        public void AWallEntirelyInsideTheVisitorIsNoWallAtAll()
        {
            var junk = new List<WallSurface>
            {
                new WallSurface(new Vector3(0.2f, 2.2f, 0.1f), Vector3.right),
            };

            Assert.IsEmpty(WallGallery.Lay(junk, Vector3.zero, 2.2f, 4),
                "better to hang nothing than to hang it on the visitor's nose");
        }

        [Test]
        public void NoWallMeansNoPlacement_NotACrash()
        {
            Assert.IsEmpty(WallGallery.Lay(null, Vector3.zero, 2.2f, 4));
            Assert.IsEmpty(WallGallery.Lay(new List<WallSurface>(), Vector3.zero, 2.2f, 4));
            Assert.IsEmpty(WallGallery.Lay(OneLongWall(), Vector3.zero, 2.2f, 0));
        }

        [Test]
        public void AnUnusableNormalDoesNotProduceAnInvalidRotation()
        {
            var degenerate = new List<WallSurface> { new WallSurface(Vector3.zero, Vector3.zero) };

            var hung = WallGallery.Lay(degenerate, new Vector3(0f, 0f, 10f), 2.2f, 1);

            Assert.AreEqual(1, hung.Count);
            Assert.AreEqual(1f, hung[0].Rotation.normalized.w * hung[0].Rotation.normalized.w
                              + hung[0].Rotation.normalized.x * hung[0].Rotation.normalized.x
                              + hung[0].Rotation.normalized.y * hung[0].Rotation.normalized.y
                              + hung[0].Rotation.normalized.z * hung[0].Rotation.normalized.z, 1e-3f);
        }
    }
}

namespace MusePico.Tests
{
    /// <summary>
    /// Which way a hung work actually faces.
    ///
    /// These exist because every artwork in Museum.unity was hung backwards and rendered nothing:
    /// Unity's Quad primitive has its normal along -Z, and URP/Unlit culls back faces.
    /// </summary>
    public class ArtworkFacingTests
    {
        [Test]
        public void RotationFacesTheRoom_ForAnyoneReasoningAboutWhereToStand()
        {
            var hung = GalleryWall.Lay(new Vector3(-10f, 0f, -20f), new Vector3(10f, 6f, 20f), 0f, 2);

            foreach (var h in hung)
            {
                var toCentre = new Vector3(-h.Position.x, 0f, -h.Position.z).normalized;
                Assert.Greater(Vector3.Dot(h.Facing, toCentre), 0.9f);
            }
        }

        [Test]
        public void AQuadIsTurnedTheOtherWay_OrItRendersNothing()
        {
            var h = new HungArtwork(Vector3.zero, Quaternion.LookRotation(Vector3.right, Vector3.up), 0);

            var quadForward = h.QuadRotation * Vector3.forward;
            Assert.Less(Vector3.Dot(quadForward, h.Facing), -0.99f,
                "a Unity Quad's +Z must point INTO the wall for its face to reach the visitor");
        }

        [Test]
        public void TheQuadStaysUpright_SoNothingHangsUpsideDown()
        {
            var h = new HungArtwork(Vector3.zero, Quaternion.LookRotation(Vector3.right, Vector3.up), 0);

            Assert.Greater(Vector3.Dot(h.QuadRotation * Vector3.up, Vector3.up), 0.99f);
        }
    }
}
