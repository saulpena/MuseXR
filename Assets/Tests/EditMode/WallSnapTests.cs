using NUnit.Framework;
using UnityEngine;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// Snapping a hung work onto the capture's real wall.
    ///
    /// The case these exist for, measured on van-gogh-500k: four of eight works found wall within
    /// 0.3-3.4 m and four found none, because her walk box runs past the collider shell. A miss
    /// must leave the playtested position alone rather than dropping the work somewhere.
    /// </summary>
    public class WallSnapTests
    {
        static HungArtwork Candidate(Vector3 at, Vector3 facing) =>
            new HungArtwork(at, Quaternion.LookRotation(facing, Vector3.up), 0);

        [Test]
        public void NoWallLeavesThePlaytestedPositionExactlyWhereItWas()
        {
            var c = Candidate(new Vector3(16f, 2.2f, -15.2f), Vector3.left);
            var r = WallSnap.Apply(c, Vector3.right, hit: false, point: Vector3.zero, normal: Vector3.zero);

            Assert.AreEqual(c.Position, r.Position, "a patchy collider must not move the work");
            Assert.AreEqual(c.Rotation, r.Rotation);
        }

        [Test]
        public void AWallJustBehindTheWorkPullsItOntoTheSurface()
        {
            var c = Candidate(new Vector3(-3.2f, 2.2f, -15.2f), Vector3.right);
            var r = WallSnap.Apply(c, Vector3.left, true,
                point: new Vector3(-3.5f, 2.2f, -15.2f), normal: Vector3.right);

            Assert.AreEqual(-3.5f + WallSnap.Inset, r.Position.x, 1e-3f, "seated just off the wall");
            Assert.AreEqual(-15.2f, r.Position.z, 1e-3f);
        }

        [Test]
        public void HeightComesFromTheCandidate_NotFromWhereTheRayLanded()
        {
            // A scanned wall leans; taking Y from the hit would let the works drift up the room.
            var c = Candidate(new Vector3(-3.2f, 2.2f, 0f), Vector3.right);
            var r = WallSnap.Apply(c, Vector3.left, true,
                point: new Vector3(-3.4f, 4.9f, 0f), normal: Vector3.right);

            Assert.AreEqual(2.2f, r.Position.y, 1e-3f);
        }

        [Test]
        public void AFloorOrACeilingIsNotAWall()
        {
            var c = Candidate(new Vector3(0f, 2.2f, 0f), Vector3.right);

            Assert.IsFalse(WallSnap.IsWall(Vector3.up));
            Assert.IsFalse(WallSnap.IsWall(Vector3.down));
            Assert.IsTrue(WallSnap.IsWall(Vector3.right));

            var r = WallSnap.Apply(c, Vector3.left, true, new Vector3(-1f, 2.2f, 0f), Vector3.up);
            Assert.AreEqual(c.Position, r.Position, "hanging a canvas on the floor faces it at nobody");
        }

        [Test]
        public void ACorrectionAcrossTheRoomIsRefused()
        {
            var c = Candidate(new Vector3(-3.2f, 2.2f, 0f), Vector3.right);
            var far = new Vector3(-3.2f - WallSnap.MaxCorrection - 1f, 2.2f, 0f);

            var r = WallSnap.Apply(c, Vector3.left, true, far, Vector3.right);
            Assert.AreEqual(c.Position, r.Position, "that collider is describing the next gallery");
        }

        [Test]
        public void ACorrectionJustInsideTheLimitIsAccepted()
        {
            var c = Candidate(new Vector3(0f, 2.2f, 0f), Vector3.right);
            // Land so that seating it (point + normal*Inset) sits just under the limit.
            var near = new Vector3(-WallSnap.MaxCorrection + WallSnap.Inset + 0.05f, 2.2f, 0f);

            var r = WallSnap.Apply(c, Vector3.left, true, near, Vector3.right);
            Assert.AreNotEqual(c.Position.x, r.Position.x, "a wall this close is the wall behind the work");
        }

        [Test]
        public void AnInsideOutTriangleStillFacesTheWorkIntoTheRoom()
        {
            // Marble trimeshes have inconsistent winding, so the raw normal may point either way.
            var c = Candidate(new Vector3(-3.2f, 2.2f, 0f), Vector3.right);

            var correct = WallSnap.Apply(c, Vector3.left, true, new Vector3(-3.4f, 2.2f, 0f), Vector3.right);
            var flipped = WallSnap.Apply(c, Vector3.left, true, new Vector3(-3.4f, 2.2f, 0f), Vector3.left);

            Assert.AreEqual(correct.Position, flipped.Position, "winding must not decide where it hangs");
            Assert.AreEqual(correct.Rotation, flipped.Rotation, "nor which way it faces");
            Assert.Greater(Vector3.Dot(correct.Rotation * Vector3.forward, Vector3.right), 0.9f,
                "a work on the left wall looks right, into the room");
        }

        [Test]
        public void FaceIntoRoomAlwaysOpposesTheProbe()
        {
            foreach (var n in new[] { Vector3.right, Vector3.left, new Vector3(1f, 0.2f, 0.3f) })
            {
                var facing = WallSnap.FaceIntoRoom(n, Vector3.right);
                Assert.Less(Vector3.Dot(facing, Vector3.right), 0f);
                Assert.AreEqual(1f, facing.magnitude, 1e-3f, "and it stays a unit vector");
            }
        }
    }
}
