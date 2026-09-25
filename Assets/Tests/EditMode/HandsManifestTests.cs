using System.Linq;
using System.Xml;
using MuseXR.EditorTools.Manifest;
using NUnit.Framework;

namespace MusePico.Tests.EditMode
{
    public class HandsManifestTests
    {
        const string Ns = HandsManifestRules.AndroidNs;

        // The shape PICO's PXR_Manifest leaves behind: controller=1, no hand declarations.
        static XmlDocument Manifest() {
            var d = new XmlDocument();
            d.LoadXml("<manifest xmlns:android=\"" + Ns + "\" package=\"x\">" +
                      "<uses-feature android:name=\"android.hardware.vr.headtracking\" android:required=\"true\"/>" +
                      "<application><meta-data android:name=\"controller\" android:value=\"1\"/></application>" +
                      "</manifest>");
            return d;
        }

        static XmlElement[] Named(XmlElement parent, string tag, string name) =>
            parent.ChildNodes.OfType<XmlElement>()
                  .Where(e => e.Name == tag && e.GetAttribute("name", Ns) == name).ToArray();

        [Test]
        public void Quest_DeclaresHandsAsOptional_AndThePermission()
        {
            var d = Manifest();
            Assert.IsTrue(HandsManifestRules.Apply(d, HandsManifestRules.Vendor.Quest));
            var f = Named(d.DocumentElement, "uses-feature", "oculus.software.handtracking");
            Assert.AreEqual(1, f.Length);
            Assert.AreEqual("false", f[0].GetAttribute("required", Ns), "required=true would exclude controller-only users");
            Assert.AreEqual(1, Named(d.DocumentElement, "uses-permission", "com.oculus.permission.HAND_TRACKING").Length);
        }

        [Test]
        public void Pico_AddsHandtracking_AndKeepsTheControllerFlag()
        {
            var d = Manifest();
            Assert.IsTrue(HandsManifestRules.Apply(d, HandsManifestRules.Vendor.Pico));
            var app = d.DocumentElement["application"];
            Assert.AreEqual("1", Named(app, "meta-data", "handtracking").Single().GetAttribute("value", Ns));
            Assert.AreEqual("1", Named(app, "meta-data", "controller").Single().GetAttribute("value", Ns),
                "removing controller=1 would make PICO treat the app as hands-only");
            Assert.AreEqual(1, Named(d.DocumentElement, "uses-permission", "com.picovr.permission.HAND_TRACKING").Length);
        }

        [Test]
        public void RunningTwice_AddsNothingTheSecondTime()
        {
            foreach (var v in new[] { HandsManifestRules.Vendor.Quest, HandsManifestRules.Vendor.Pico })
            {
                var d = Manifest();
                HandsManifestRules.Apply(d, v);
                string once = d.OuterXml;
                Assert.IsFalse(HandsManifestRules.Apply(d, v), v + " changed on the second pass");
                Assert.AreEqual(once, d.OuterXml);
            }
        }

        [Test]
        public void Quest_FixesAnExistingRequiredHandFeature()
        {
            var d = Manifest();
            var e = d.CreateElement("uses-feature");
            e.SetAttribute("name", Ns, "oculus.software.handtracking");
            e.SetAttribute("required", Ns, "true");
            d.DocumentElement.AppendChild(e);
            Assert.IsTrue(HandsManifestRules.Apply(d, HandsManifestRules.Vendor.Quest));
            Assert.AreEqual("false", Named(d.DocumentElement, "uses-feature", "oculus.software.handtracking")
                                     .Single().GetAttribute("required", Ns));
        }

        [TestCase("ENABLE_PICO_OPENXR_SDK;PICO_SPATIALIZER;MUSEXR_QUEST", HandsManifestRules.Vendor.Quest)]
        [TestCase("ENABLE_PICO_OPENXR_SDK;PICO_SPATIALIZER;MUSEXR_PICO", HandsManifestRules.Vendor.Pico)]
        [TestCase("ENABLE_PICO_OPENXR_SDK", HandsManifestRules.Vendor.Unknown)]
        [TestCase(null, HandsManifestRules.Vendor.Unknown)]
        public void Vendor_ComesFromTheBuildProfileDefine(string defines, HandsManifestRules.Vendor expected)
        {
            Assert.AreEqual(expected, HandsManifestRules.VendorFromDefines(defines));
        }
    }
}
