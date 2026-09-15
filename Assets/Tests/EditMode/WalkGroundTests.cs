using System.Collections.Generic;
using MusePico.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The rules worth pinning are the ones about which hit to REJECT. Picking the first hit, or
    /// the highest hit outright, both produce a visitor standing in the wrong place — and in
    /// muse-infinity each of these cases was a separate acceptance failure before it was a rule.
    /// </summary>
    public class WalkGroundTests
    {
        static List<float> H(params float[] values) => new List<float>(values);

        [Test]
        public void NoHits_FallsBackToTheFlatProfileGround()
        {
            Assert.AreEqual(1.25f, WalkGround.PickGroundHeight(H(), 1.25f, 1f), 1e-4f);
        }

        [Test]
        public void NullHits_IsTreatedAsNoHits()
        {
            Assert.AreEqual(0f, WalkGround.PickGroundHeight(null, 0f, 1f), 1e-4f);
        }

        [Test]
        public void PicksTheHighestHitInsideTheWindow_NotTheFirst()
        {
            // A collider returns hits in no useful order; taking hits[0] was the original bug.
            Assert.AreEqual(0.35f, WalkGround.PickGroundHeight(H(0.05f, 0.35f, 0.20f), 0f, 1f), 1e-4f);
        }

        [Test]
        public void CeilingAndArchHitsAreRejected()
        {
            // 3.9 m is a doorway arch above the same floor. Taking the highest hit outright
            // lifted companions off the ground in the van-gogh world.
            Assert.AreEqual(0.20f, WalkGround.PickGroundHeight(H(0.20f, 3.9f), 0f, 1f), 1e-4f);
        }

        [Test]
        public void AStepUpIsWalkable_AMezzanineIsNot()
        {
            Assert.AreEqual(0.55f, WalkGround.PickGroundHeight(H(0f, 0.55f), 0f, 1f), 1e-4f,
                "0.55 m is a real step and must win.");
            Assert.AreEqual(0f, WalkGround.PickGroundHeight(H(0f, 0.9f), 0f, 1f), 1e-4f,
                "0.9 m is above the step cap and must be rejected.");
        }

        [Test]
        public void TheUpWindowIsCappedInMetres_NotScaled()
        {
            // The whole point of MaxStepUp: at worldScale 1.7 the original 1.2*ws window was
            // ~2.04 m, so a collider fragment 1 m above the floor won and the visitor floated.
            Assert.AreEqual(0f, WalkGround.PickGroundHeight(H(0f, 1.0f), 0f, 1.7f), 1e-4f);
        }

        [Test]
        public void SunkenFloorInsideTheDownWindowStillWins()
        {
            Assert.AreEqual(-1.5f, WalkGround.PickGroundHeight(H(-1.5f), 0f, 1f), 1e-4f);
        }

        [Test]
        public void HillsideAboveTheWindow_TakesTheNearestHitRatherThanBurying()
        {
            // Tier 2. Every hit misses the window on open terrain; falling through to the flat
            // plane put the camera inside the hill with the ground rendering overhead.
            Assert.AreEqual(4.0f, WalkGround.PickGroundHeight(H(4.0f, 11.0f), 0f, 1f), 1e-4f);
        }

        [Test]
        public void HillsideBelowTheWindow_AlsoTakesTheNearestHit()
        {
            Assert.AreEqual(-6.0f, WalkGround.PickGroundHeight(H(-6.0f, -20.0f), 0f, 1f), 1e-4f);
        }





    }
}
