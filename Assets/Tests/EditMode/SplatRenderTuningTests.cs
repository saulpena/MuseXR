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

        /// The package counts render passes, and Multi Pass runs two a frame. Only 1 and 2 keep
        /// the same eye doing the sorting every frame; 3+ alternates it.
        [Test]
        public void SortIntervalNeverAlternatesTheSortingEye()
        {
            Assert.That(SplatRenderTuning.SortNthPass, Is.InRange(1, 2));
        }

        [Test]
        public void SHOrderIsAValidBand()
        {
            Assert.That(SplatRenderTuning.SHOrder, Is.InRange(0, 3));
        }
    }
}
