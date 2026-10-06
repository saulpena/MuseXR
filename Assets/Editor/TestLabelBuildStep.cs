using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// A test build gets its own app ID and library label, so test APKs sit side by side on the headset instead of
    /// replacing each other (Saul, 6 Oct: "future builds make sure they have different names").
    ///
    /// Write <c>Builds/testlabel.txt</c> as <c>Label|app.id</c> (e.g. <c>MuseXR T6 72Hz|com.musexr.impossiblemuseum.t6</c>)
    /// before a build. XRBuild applies the vendor profile first, which resets the product name, so the label is applied
    /// here, inside the build, and the master name and ID are put back afterwards. The file is deleted once used: a
    /// label applies to one build only, and the next build is the master unless a new label is written.
    /// </summary>
    public sealed class TestLabelBuildStep : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string LabelFile = "Builds/testlabel.txt";
        public const string MasterId = "com.musexr.impossiblemuseum";
        static string _name, _id;

        public int callbackOrder => 1000;   // after every other preprocess step

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android || !File.Exists(LabelFile)) return;
            var parts = File.ReadAllText(LabelFile).Trim().Split('|');
            File.Delete(LabelFile);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) return;
            _name = PlayerSettings.productName;
            _id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            PlayerSettings.productName = parts[0].Trim();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, parts[1].Trim());
            Debug.Log("[TestLabel] this build is \"" + parts[0].Trim() + "\" / " + parts[1].Trim());
        }

        public void OnPostprocessBuild(BuildReport report) => Restore();

        [InitializeOnLoadMethod]
        static void RestoreAfterReload() => EditorApplication.delayCall += Restore;   // a failed build still restores

        static void Restore()
        {
            if (_name == null) return;
            PlayerSettings.productName = _name;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, _id ?? MasterId);
            AssetDatabase.SaveAssets();
            Debug.Log("[TestLabel] back to \"" + _name + "\" / " + (_id ?? MasterId));
            _name = _id = null;
        }
    }
}
