using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;

namespace MuseXR.EditorTools.Manifest
{
    /// <summary>
    /// Quest 3 / 3S: trade one CPU level for one GPU level (test T15).
    ///
    /// Measured on Quest 3S with the peach world missing 72 FPS: GPU 92% busy yet held at
    /// GPU level 4 (545 MHz), CPU ~20% busy. Level 4 is the ceiling unless the app opts in: Meta
    /// makes GPU level 5 available only with dynamic resolution or with CPU/GPU trading set to +1
    /// (developers.meta.com/horizon/documentation/native/android/po-quest-boost). This app is
    /// GPU-bound with CPU to spare, which is exactly the trade.
    ///
    /// OpenXR backend only (this app is), Quest 3 / 3S only; other headsets ignore the key.
    /// Proof it applied: logcat prints "CreateClient: Value of tradeCpuForGpu is 1" at launch.
    /// </summary>
    public class PerformanceManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 20001;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            if (HandsManifestRules.VendorFromDefines(defines) != HandsManifestRules.Vendor.Quest) return;

            var file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(file);
            if (PerformanceManifestRules.Apply(doc))
                doc.Save(file);
        }
    }

    /// <summary>Pure manifest edit, separate from the build hook so it can be tested.</summary>
    public static class PerformanceManifestRules
    {
        public const string TradeKey = "com.oculus.trade_cpu_for_gpu_amount";

        /// <summary>+1 = one level to the GPU, one taken from the CPU. 0 = no change.</summary>
        public const string TradeValue = "1";

        /// <summary>Sets the trade on the application element. Idempotent. True if changed.</summary>
        public static bool Apply(XmlDocument doc)
        {
            var app = doc.DocumentElement?["application"];
            if (app == null) return false;
            const string ns = HandsManifestRules.AndroidNs;
            foreach (XmlNode n in app.ChildNodes)
                if (n is XmlElement e && e.Name == "meta-data" && e.GetAttribute("name", ns) == TradeKey)
                {
                    if (e.GetAttribute("value", ns) == TradeValue) return false;
                    e.SetAttribute("value", ns, TradeValue);
                    return true;
                }
            var el = doc.CreateElement("meta-data");
            el.SetAttribute("name", ns, TradeKey);
            el.SetAttribute("value", ns, TradeValue);
            app.AppendChild(el);
            return true;
        }
    }
}
