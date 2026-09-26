using GaussianSplatting.Runtime;
using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests.EditMode
{
    public class SplatRenderTuningTests
    {
        [Test]
        public void ApplyWritesBothKnobsOntoTheRenderer()
        {
            var go = new GameObject("tuning-test");
            try
            {
                var r = go.AddComponent<GaussianSplatRenderer>();
                r.m_SortNthFrame = 99;
                r.m_SHOrder = 99;
                SplatRenderTuning.Apply(r);
                Assert.AreEqual(SplatRenderTuning.SortNthPass, r.m_SortNthFrame);
                Assert.AreEqual(SplatRenderTuning.SHOrder, r.m_SHOrder);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// The package counts render passes, and Multi Pass runs two a frame. 1 sorts for each
        /// eye; an even value always lands on the left eye; an odd value above 1 alternates eyes.
        [Test]
        public void SortIntervalNeverAlternatesTheSortingEye()
        {
            int n = SplatRenderTuning.SortNthPass;
            Assert.That(n == 1 || (n > 0 && n % 2 == 0), $"SortNthPass {n} alternates the sorting eye");
        }

        [Test]
        public void SplatScale_CyclesThroughEveryStepAndWraps()
        {
            float s = SplatRenderTuning.SplatScaleSteps[0];
            var seen = new System.Collections.Generic.List<float> { s };
            for (int i = 1; i < SplatRenderTuning.SplatScaleSteps.Length; i++)
                seen.Add(s = SplatRenderTuning.NextSplatScale(s));
            CollectionAssert.AreEqual(SplatRenderTuning.SplatScaleSteps, seen);
            Assert.AreEqual(SplatRenderTuning.SplatScaleSteps[0], SplatRenderTuning.NextSplatScale(s), "must wrap");
        }

        [Test]
        public void SplatScale_UnknownValueRestartsTheCycle()
        {
            Assert.AreEqual(SplatRenderTuning.SplatScaleSteps[0], SplatRenderTuning.NextSplatScale(0.123f));
        }

        [Test]
        public void SplatScale_DefaultIsAStep_AndEveryStepIsWithinTheSupportedRange()
        {
            CollectionAssert.Contains(SplatRenderTuning.SplatScaleSteps, SplatRenderTuning.SplatResolutionScale);
            foreach (var s in SplatRenderTuning.SplatScaleSteps)
                Assert.That(s, Is.InRange(GaussianSplatSettings.MinScale, 1f));
            CollectionAssert.Contains(SplatRenderTuning.SplatScaleSteps, 1f, "1.0 must stay reachable for A/B");
        }

        [Test]
        public void Settings_ClampOutOfRangeScales()
        {
            float keep = GaussianSplatSettings.ResolutionScale;
            try
            {
                GaussianSplatSettings.ResolutionScale = 0.1f;
                Assert.AreEqual(GaussianSplatSettings.MinScale, GaussianSplatSettings.ResolutionScale);
                GaussianSplatSettings.ResolutionScale = 3f;
                Assert.AreEqual(1f, GaussianSplatSettings.ResolutionScale);
            }
            finally { GaussianSplatSettings.ResolutionScale = keep; }
        }

        [Test]
        public void SHOrderIsAValidBand()
        {
            Assert.That(SplatRenderTuning.SHOrder, Is.InRange(0, 3));
        }
    }
}
