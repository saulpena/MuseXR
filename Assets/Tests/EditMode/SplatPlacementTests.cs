using MusePico.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The World Labs samples carry no measured spawn, so where the viewer ends up is computed
    /// rather than authored. These lock in the properties that make the difference between
    /// "standing in a room" and the two failures actually observed on the emulator: a camera
    /// jammed into the back wall, and a camera lifted through the ceiling because the XR rig
    /// added its own eye height on top of a pose that already had one.
    ///
    /// Bounds below are the real measured values from the converted assets, not invented ones.
    /// </summary>
    public class SplatPlacementTests
    {
        // warm_traditional_kitchen_interior_500k — a small interior.
        static readonly Vector3 InteriorMin = new Vector3(-1.90f, -2.22f, -2.43f);
        static readonly Vector3 InteriorMax = new Vector3(1.88f, 1.30f, 4.82f);

        // modern_house_with_lush_landscaping_500k — an outdoor capture whose bounds run 47 m
        // below the walkable ground. This is the one that breaks height-as-a-fraction schemes.
        static readonly Vector3 OutdoorMin = new Vector3(-10.71f, -47.57f, -33.67f);
        static readonly Vector3 OutdoorMax = new Vector3(45.62f, 0.96f, 37.60f);

        const float HeadHeight = 1.6f;

        [Test]
        public void EyeLandsJustBelowTheTopOfTheBounds_Interior()
        {
            SplatPlacement.FromBounds(InteriorMin, InteriorMax, 1f, HeadHeight, out var origin, out _);
            float eyeY = origin.y + HeadHeight;

            Assert.AreEqual(InteriorMax.y - SplatPlacement.EyeBelowTop, eyeY, 1e-3f);
            Assert.Less(eyeY, InteriorMax.y, "eye must stay under the ceiling");
            Assert.Greater(eyeY, InteriorMin.y, "eye must stay above the floor");
        }

        [Test]
        public void OutdoorCaptureDoesNotPutTheViewerUnderground()
        {
            // The bounds descend 47 m; anchoring to a fraction of that height would place the
            // eye ~20 m below the ground the capture actually shows.
            SplatPlacement.FromBounds(OutdoorMin, OutdoorMax, 1f, HeadHeight, out var origin, out _);
            float eyeY = origin.y + HeadHeight;

            Assert.Greater(eyeY, OutdoorMax.y - 3f,
                "eye should sit near the top of the capture, where the walkable ground is");
        }

        [Test]
        public void HeadHeightIsSubtractedSoTheRigDoesNotDoubleCountIt()
        {
            SplatPlacement.FromBounds(InteriorMin, InteriorMax, 1f, 0f, out var noHead, out _);
            SplatPlacement.FromBounds(InteriorMin, InteriorMax, 1f, HeadHeight, out var withHead, out _);

            Assert.AreEqual(noHead.y - HeadHeight, withHead.y, 1e-4f);
            Assert.AreEqual(noHead.x, withHead.x, 1e-4f, "head height must not move the viewer sideways");
            Assert.AreEqual(noHead.z, withHead.z, 1e-4f);
        }

        [Test]
        public void ViewerStandsBackFromTheCentreButStaysInsideTheBounds()
        {
            SplatPlacement.FromBounds(InteriorMin, InteriorMax, 1f, HeadHeight, out var origin, out _);
            var centre = (InteriorMin + InteriorMax) * 0.5f;

            Assert.Less(origin.z, centre.z, "viewer should step back from the centre");
            Assert.Greater(origin.z, InteriorMin.z,
                "viewer must not end up behind the back wall — this is what rendered as a blur");
            Assert.AreEqual(centre.x, origin.x, 1e-4f, "viewer stays on the centre line in X");
        }

        [Test]
        public void ViewerLooksTowardsTheCentre()
        {
            SplatPlacement.FromBounds(InteriorMin, InteriorMax, 1f, HeadHeight, out var origin, out var rot);
            var centre = (InteriorMin + InteriorMax) * 0.5f;
            var toCentre = new Vector3(centre.x - origin.x, 0f, centre.z - origin.z).normalized;
            var facing = rot * Vector3.forward;

            Assert.Greater(Vector3.Dot(new Vector3(facing.x, 0f, facing.z).normalized, toCentre),
                           0.99f, "the viewer should be facing into the world, not away from it");
        }

        [Test]
        public void WorldScaleScalesThePoseLinearly()
        {
            SplatPlacement.FromBounds(InteriorMin, InteriorMax, 1f, 0f, out var a, out _);
            SplatPlacement.FromBounds(InteriorMin, InteriorMax, 2f, 0f, out var b, out _);

            Assert.AreEqual(a.x * 2f, b.x, 1e-3f);
            Assert.AreEqual(a.y * 2f, b.y, 1e-3f);
            Assert.AreEqual(a.z * 2f, b.z, 1e-3f);
        }

        [Test]
        public void FarClipContainsTheWholeWorld()
        {
            foreach (var (min, max) in new[] { (InteriorMin, InteriorMax), (OutdoorMin, OutdoorMax) })
            {
                float far = SplatPlacement.FarClipFor(min, max, 1f);
                SplatPlacement.FromBounds(min, max, 1f, HeadHeight, out var origin, out _);
                var eye = origin + Vector3.up * HeadHeight;

                float furthest = 0f;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? min.x : max.x,
                                        (i & 2) == 0 ? min.y : max.y,
                                        (i & 4) == 0 ? min.z : max.z);
                    furthest = Mathf.Max(furthest, Vector3.Distance(eye, c));
                }

                Assert.Greater(far, furthest, "far clip must reach the furthest corner of the bounds");
            }
        }

        [Test]
        public void DegenerateBoundsDoNotProduceANaNPose()
        {
            SplatPlacement.FromBounds(Vector3.zero, Vector3.zero, 1f, 1.6f, out var pos, out var rot);

            Assert.IsFalse(float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z));
            Assert.IsFalse(float.IsNaN(rot.x) || float.IsNaN(rot.y) ||
                           float.IsNaN(rot.z) || float.IsNaN(rot.w));
        }
    }
}
