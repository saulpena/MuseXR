using MuseXR.UI;
using NUnit.Framework;

namespace MusePico.Tests
{
    public class PanelScaleTests
    {
        [Test]
        public void BodyTextIsOneDegreeAtTheDistanceThePanelIsBuiltFor()
        {
            foreach (var d in new[] { 1f, 2f, 2.5f, 50f })
                Assert.AreEqual(1f, PanelScale.CapDegrees(MuseTheme.BodyPx, d, d), 1e-3f);
        }

        [Test]
        public void HerTwoMetreRuleIsAbout35MillimetresOfCap()
        {
            float cap = MuseTheme.BodyPx * PanelScale.CapHeightEm * PanelScale.MetresPerPixel(2f);
            Assert.AreEqual(0.0349f, cap, 0.001f, "her own figure: ~3.5 cm cap at 2 m");
        }

        [Test]
        public void ReadFromCloserTheTextIsLargerNotSmaller()
        {
            Assert.Greater(PanelScale.CapDegrees(MuseTheme.BodyPx, 2f, 1.5f), 1f);
            Assert.Less(PanelScale.CapDegrees(MuseTheme.BodyPx, 2f, 3f), 1f);
        }

        [Test]
        public void HerArtworkCardIsAboutOneAndAHalfMetresWideAtTwoMetres()
        {
            Assert.AreEqual(1.5f, 440f * PanelScale.MetresPerPixel(2f), 0.05f);
        }
    }
}
