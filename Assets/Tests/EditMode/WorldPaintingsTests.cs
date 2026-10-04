using MuseXR.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>Hanging a work in a captured frame: crop to fill, never stretch.</summary>
    public class WorldPaintingsTests
    {
        [Test]
        public void AWideWorkInATallFrameKeepsItsProportionsAndTheFocus()
        {
            // The Peasant Woman (4:3) in the tall frame by Van Gogh's side door, the woman at 70% across.
            var r = WorldPaintings.CropRect(4f / 3f, new Vector2(1.11f, 2.10f), new Vector2(0.70f, 0.5f));
            Assert.AreEqual(1f, r.height, 1e-5f, "full height");
            Assert.AreEqual((1.11f / 2.10f) / (4f / 3f), r.width, 1e-5f, "the frame's shape, not a stretch");
            Assert.AreEqual(0.70f, r.center.x, 1e-5f, "the woman stays in the middle");
        }

        [Test]
        public void TheCropNeverLeavesTheWork()
        {
            var r = WorldPaintings.CropRect(2f, new Vector2(1f, 1f), new Vector2(0.98f, 0.5f));
            Assert.AreEqual(1f, r.xMax, 1e-5f);
            r = WorldPaintings.CropRect(0.5f, new Vector2(1f, 1f), new Vector2(0.5f, 0.01f));
            Assert.AreEqual(0f, r.yMin, 1e-5f);
        }
    }
}
