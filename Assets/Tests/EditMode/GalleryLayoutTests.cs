using MusePico.Gallery;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The gallery has to make models from a generator look deliberately placed. Tripo returns
    /// each mesh in its own units with its origin wherever the solve put it, so the two things
    /// that go wrong are exhibits at wildly different sizes, and exhibits floating above or sunk
    /// through their pedestals. Both are arithmetic, so both are tested here rather than
    /// discovered in a headset.
    /// </summary>
    public class GalleryLayoutTests
    {
        [Test]
        public void ASingleExhibitStandsStraightAhead()
        {
            var placement = GalleryLayout.Arc(0, 1, radius: 3.5f);

            Assert.AreEqual(0f, placement.Position.x, 1e-4f);
            Assert.AreEqual(3.5f, placement.Position.z, 1e-4f);
        }

        [Test]
        public void ExhibitsAreEvenlySpacedAndCentredOnTheViewersForward()
        {
            const int count = 5;
            var first = GalleryLayout.Arc(0, count, 3.5f, 170f);
            var middle = GalleryLayout.Arc(2, count, 3.5f, 170f);
            var last = GalleryLayout.Arc(4, count, 3.5f, 170f);

            // The middle exhibit is dead ahead; the outermost two mirror each other.
            Assert.AreEqual(0f, middle.Position.x, 1e-3f);
            Assert.AreEqual(-first.Position.x, last.Position.x, 1e-3f);
            Assert.AreEqual(first.Position.z, last.Position.z, 1e-3f);
        }

        [Test]
        public void EveryExhibitIsTheSameDistanceFromTheViewer()
        {
            for (var i = 0; i < 7; i++)
            {
                var placement = GalleryLayout.Arc(i, 7, radius: 4f);
                Assert.AreEqual(4f, new Vector2(placement.Position.x, placement.Position.z).magnitude, 1e-3f);
            }
        }

        [Test]
        public void EveryExhibitFacesTheViewer()
        {
            for (var i = 0; i < 6; i++)
            {
                var placement = GalleryLayout.Arc(i, 6, radius: 3f);
                var facing = placement.Rotation * Vector3.forward;
                var towardViewer = (-placement.Position).normalized;

                Assert.AreEqual(1f, Vector3.Dot(facing.normalized, towardViewer), 1e-3f,
                    "exhibit " + i + " has its back to the viewer");
            }
        }

        [Test]
        public void Fit_ScalesTheTallestAxisToTheTargetHeight()
        {
            // A 4 m tall mesh and a 0.2 m one must end up the same height on their pedestals.
            var tall = GalleryLayout.Fit(new Bounds(Vector3.zero, new Vector3(1f, 4f, 1f)), 1.8f);
            var small = GalleryLayout.Fit(new Bounds(Vector3.zero, new Vector3(0.1f, 0.2f, 0.1f)), 1.8f);

            Assert.AreEqual(1.8f / 4f, tall.Scale, 1e-4f);
            Assert.AreEqual(1.8f / 0.2f, small.Scale, 1e-4f);
        }

        [Test]
        public void Fit_StandsTheModelOnThePedestal_WhateverItsOriginWas()
        {
            // Origin at the navel: bounds centred 1.2 m above the model's own zero.
            var bounds = new Bounds(new Vector3(0.3f, 1.2f, -0.4f), new Vector3(0.6f, 1.6f, 0.5f));
            var fit = GalleryLayout.Fit(bounds, targetHeight: 1.8f, pedestalTopY: 0.9f);

            // Apply the fit the way the builder does, then check where the model's feet land.
            var scaledBottomY = (bounds.center.y - bounds.extents.y) * fit.Scale + fit.Offset.y;
            var scaledCentreX = bounds.center.x * fit.Scale + fit.Offset.x;
            var scaledCentreZ = bounds.center.z * fit.Scale + fit.Offset.z;

            Assert.AreEqual(0.9f, scaledBottomY, 1e-4f, "the model must rest on the pedestal top");
            Assert.AreEqual(0f, scaledCentreX, 1e-4f, "the model must be centred over the pedestal");
            Assert.AreEqual(0f, scaledCentreZ, 1e-4f);
        }

        [Test]
        public void Fit_ScaledHeightIsTheTargetHeight()
        {
            var bounds = new Bounds(new Vector3(0f, 1.2f, 0f), new Vector3(0.6f, 1.6f, 0.5f));
            var fit = GalleryLayout.Fit(bounds, targetHeight: 1.8f, pedestalTopY: 0.9f);

            Assert.AreEqual(1.8f, bounds.size.y * fit.Scale, 1e-4f);
        }

        [Test]
        public void Fit_DoesNotDivideByZeroOnADegenerateMesh()
        {
            // A failed import gives empty bounds. Unguarded this scales to infinity, which in a
            // headset is a full-screen grey wall with no clue as to why.
            var fit = GalleryLayout.Fit(new Bounds(Vector3.zero, Vector3.zero), 1.8f);

            Assert.AreEqual(1f, fit.Scale, 1e-4f);
            Assert.IsFalse(float.IsNaN(fit.Offset.y) || float.IsInfinity(fit.Offset.y));
        }
    }
}
