using MuseXR.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>Her Van Gogh stroke: max 256 points, smoothed into a thick ribbon.</summary>
    public class StrokeBrushTests
    {
        static void Draw(StrokeBrush b, int frames, System.Func<int, Vector3> at)
        {
            for (var i = 0; i < frames; i++) b.Add(at(i), Vector3.up);
        }

        [Test]
        public void ALongStrokeNeverPassesTheCapAndStillReachesTheHand()
        {
            var b = new StrokeBrush();
            // 20 m of stroke, one centimetre a frame, which ran out at 3.8 m when points were simply dropped.
            Draw(b, 2000, i => new Vector3(i * 0.01f, 1f, 0f));
            Assert.LessOrEqual(b.Count, StrokeBrush.MaxPoints);
            Assert.Greater(b.Points[b.Count - 1].x, 18f, "the stroke kept drawing to the end");
            Assert.AreEqual(0f, b.Points[0].x, 1e-4f, "and kept its start");
        }

        [Test]
        public void TremorIsSmoothedOut()
        {
            var b = new StrokeBrush();
            // A straight line with 1 cm of alternating jitter, as a held controller shakes.
            Draw(b, 400, i => new Vector3(i * 0.005f, 1f + (i % 2 == 0 ? 0.01f : -0.01f), 0f));
            var worst = 0f;
            // From the fourth point: the first is where the trigger went down, and easing it would lag the start.
            for (var i = 3; i < b.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(b.Points[i].y - 1f));
            Assert.Less(worst, 0.006f, "jitter of 1 cm reaches the paint at under 6 mm");
        }

        [Test]
        public void TheRibbonIsAThickBandThatTapersAtBothEnds()
        {
            var b = new StrokeBrush();
            Draw(b, 200, i => new Vector3(i * 0.01f, 1f, 0f));
            var mesh = new Mesh();
            StrokeBrush.BuildRibbon(mesh, b.Points, b.Ups);
            Assert.Greater(mesh.vertexCount, b.Count, "the spline adds samples between the points");
            var v = mesh.vertices; var mid = v.Length / 2 & ~1;
            var midWidth = Vector3.Distance(v[mid], v[mid + 1]);
            var startWidth = Vector3.Distance(v[0], v[1]);
            var endWidth = Vector3.Distance(v[v.Length - 2], v[v.Length - 1]);
            Assert.AreEqual(StrokeBrush.Width, midWidth, 0.005f, "full width in the middle");
            Assert.Less(startWidth, midWidth * 0.5f);
            Assert.Less(endWidth, midWidth * 0.3f);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void TheBandLiesAcrossTheBrushNotEdgeOn()
        {
            var b = new StrokeBrush();
            Draw(b, 100, i => new Vector3(i * 0.01f, 1f, 0f));   // travelling +x, brush up is +y
            var mesh = new Mesh();
            StrokeBrush.BuildRibbon(mesh, b.Points, b.Ups);
            var v = mesh.vertices; var mid = v.Length / 2 & ~1;
            var across = (v[mid + 1] - v[mid]).normalized;
            Assert.Greater(Mathf.Abs(across.y), 0.99f, "spread along the brush's up");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void TheDescriptionReadsTheShapeInWords()
        {
            var up = new[] { new Vector3(0, 0.5f, 0), new Vector3(0, 0.9f, 0), new Vector3(0.02f, 1.4f, 0), new Vector3(0, 1.9f, 0) };
            var d = StrokeBrush.Describe(up);
            StringAssert.Contains("rising", d);
            StringAssert.Contains("long", d);
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(d, "[0-9]"), "no numbers for the masters to recite");
        }
    }
}
