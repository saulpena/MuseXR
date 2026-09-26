using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusePico.Tests.EditMode
{
    public class LaunchOptionsTests
    {
        [Test]
        public void AKnownWorldKeyIsHonoured()
        {
            Assert.AreEqual("celestial-peach-blossom-paradise-500k",
                LaunchOptions.Validated("celestial-peach-blossom-paradise-500k"));
        }

        [Test]
        public void ATypoIsIgnored_SoItCannotLoadNothing()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not a WorldCatalog.Small key"));
            Assert.IsNull(LaunchOptions.Validated("celestial-peach-blossom-paradise"), "missing -500k suffix");
        }

        [Test]
        public void NoExtraMeansNoOverride()
        {
            Assert.IsNull(LaunchOptions.Validated(null));
            Assert.IsNull(LaunchOptions.Validated("  "));
        }

        [Test]
        public void InTheEditorNoSwitchIsSet()
        {
            Assert.IsNull(LaunchOptions.HomeWorldOverride);
            Assert.IsFalse(LaunchOptions.CapturePose);
            Assert.IsFalse(LaunchOptions.FreeWalk);
        }
    }
}
