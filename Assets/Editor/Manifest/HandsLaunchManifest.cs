using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;

namespace MuseXR.EditorTools.Manifest
{
    /// <summary>
    /// Lets both headsets launch the app while the controllers are asleep.
    ///
    /// Without this, Quest shows "Controller required" (LaunchCheckControllerRequiredDialog) and
    /// PICO shows its HandDialogActivity, and the app does not start until a controller wakes —
    /// which also means a headset on a desk cannot be relaunched over adb. Both gates read the
    /// manifest: the app declares controllers and nothing says hands are acceptable.
    ///
    /// This only DECLARES hand support. It does not enable hand input: the rig still reads
    /// controllers, so pointing and the trigger do nothing until one is picked up.
    ///
    /// PICO's own PXR_Manifest (callbackOrder 10000) forces controller=1 and strips handtracking
    /// unless the XR Hands package and its HandTracking feature are installed, so this runs after it.
    /// </summary>
    public class HandsLaunchManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 20000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            var vendor = HandsManifestRules.VendorFromDefines(defines);
            if (vendor == HandsManifestRules.Vendor.Unknown) return;

            var file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(file);
            if (HandsManifestRules.Apply(doc, vendor))
                doc.Save(file);
        }
    }

    /// <summary>Pure manifest edits, separate from the build hook so they can be tested.</summary>
    public static class HandsManifestRules
    {
        public enum Vendor { Unknown, Quest, Pico }

        public const string AndroidNs = "http://schemas.android.com/apk/res/android";

        public static Vendor VendorFromDefines(string defines)
        {
            foreach (var d in (defines ?? "").Split(';'))
            {
                if (d.Trim() == "MUSEXR_QUEST") return Vendor.Quest;
                if (d.Trim() == "MUSEXR_PICO") return Vendor.Pico;
            }
            return Vendor.Unknown;
        }

        /// <summary>Adds the vendor's hand declarations if missing. Idempotent. True if changed.</summary>
        public static bool Apply(XmlDocument doc, Vendor vendor)
        {
            var manifest = doc.DocumentElement;
            if (manifest == null || manifest.Name != "manifest") return false;
            bool changed = false;
            if (vendor == Vendor.Quest)
            {
                changed |= EnsureChild(doc, manifest, "uses-permission", "com.oculus.permission.HAND_TRACKING", null, null);
                changed |= EnsureChild(doc, manifest, "uses-feature", "oculus.software.handtracking", "required", "false");
            }
            else if (vendor == Vendor.Pico)
            {
                var app = manifest["application"];
                if (app == null) return false;
                changed |= EnsureChild(doc, manifest, "uses-permission", "com.picovr.permission.HAND_TRACKING", null, null);
                changed |= EnsureChild(doc, app, "meta-data", "handtracking", "value", "1");
            }
            return changed;
        }

        static bool EnsureChild(XmlDocument doc, XmlElement parent, string tag, string name, string attr, string value)
        {
            foreach (XmlNode n in parent.ChildNodes)
                if (n is XmlElement e && e.Name == tag && e.GetAttribute("name", AndroidNs) == name)
                {
                    if (attr == null || e.GetAttribute(attr, AndroidNs) == value) return false;
                    e.SetAttribute(attr, AndroidNs, value);
                    return true;
                }
            var el = doc.CreateElement(tag);
            el.SetAttribute("name", AndroidNs, name);
            if (attr != null) el.SetAttribute(attr, AndroidNs, value);
            parent.AppendChild(el);
            return true;
        }
    }
}
