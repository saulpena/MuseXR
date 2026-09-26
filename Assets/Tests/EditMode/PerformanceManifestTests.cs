using System.Linq;
using System.Xml;
using MuseXR.EditorTools.Manifest;
using NUnit.Framework;

namespace MusePico.Tests.EditMode
{
    public class PerformanceManifestTests
    {
        const string Ns = HandsManifestRules.AndroidNs;

        static XmlDocument Manifest(string appChildren = "") {
            var d = new XmlDocument();
            d.LoadXml("<manifest xmlns:android=\"" + Ns + "\" package=\"x\"><application>" + appChildren +
                      "</application></manifest>");
            return d;
        }

        static XmlElement[] Trade(XmlDocument d) =>
            d.DocumentElement["application"].ChildNodes.OfType<XmlElement>()
             .Where(e => e.Name == "meta-data" && e.GetAttribute("name", Ns) == PerformanceManifestRules.TradeKey).ToArray();

        [Test]
        public void TradesOneCpuLevelForOneGpuLevel()
        {
            var d = Manifest();
            Assert.IsTrue(PerformanceManifestRules.Apply(d));
            Assert.AreEqual("1", Trade(d).Single().GetAttribute("value", Ns));
        }

        [Test]
        public void RunningTwice_AddsNothingTheSecondTime()
        {
            var d = Manifest();
            PerformanceManifestRules.Apply(d);
            string once = d.OuterXml;
            Assert.IsFalse(PerformanceManifestRules.Apply(d));
            Assert.AreEqual(once, d.OuterXml);
        }

        [Test]
        public void OverwritesAnOppositeTrade_RatherThanAddingASecondKey()
        {
            var d = Manifest("<meta-data android:name=\"" + PerformanceManifestRules.TradeKey + "\" android:value=\"-1\"/>");
            Assert.IsTrue(PerformanceManifestRules.Apply(d));
            Assert.AreEqual("1", Trade(d).Single().GetAttribute("value", Ns));
        }

        [Test]
        public void AManifestWithoutAnApplicationIsLeftAlone()
        {
            var d = new XmlDocument();
            d.LoadXml("<manifest xmlns:android=\"" + Ns + "\" package=\"x\"/>");
            Assert.IsFalse(PerformanceManifestRules.Apply(d));
        }
    }
}
