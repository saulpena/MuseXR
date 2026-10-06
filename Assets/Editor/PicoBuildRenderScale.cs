using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// The PICO build renders at URP renderScale 1.0; the Quest keeps the shared asset's 0.8. Set inside the build and
    /// put back after it, so Mobile_RPAsset on disk stays the Quest's tuning.
    ///
    /// Why at build time: on the PICO 4 Ultra the eye buffer was 1152x1268 for a 2160-wide display and the masters,
    /// panels and text read pixelated. Setting renderScale from a startup script came too late - the eye textures were
    /// already sized (test M3a, 6 Oct: the log showed 1.0 and the same 1152x1268). musexr-b-3e's T4 set it in the
    /// asset: small text more readable.
    /// </summary>
    public sealed class PicoBuildRenderScale : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const float PicoRenderScale = 1.0f;
        const string AssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        static float? _was;

        public int callbackOrder => 900;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            if (!PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android).Contains("MUSEXR_PICO")) return;
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (urp == null) return;
            _was = urp.renderScale;
            urp.renderScale = PicoRenderScale;
            EditorUtility.SetDirty(urp); AssetDatabase.SaveAssets();
            Debug.Log("[PicoBuild] renderScale " + _was.Value.ToString("0.00") + " -> " + PicoRenderScale.ToString("0.00") + " for this PICO build");
        }

        public void OnPostprocessBuild(BuildReport report) => Restore();

        [InitializeOnLoadMethod]
        static void RestoreAfterReload() => EditorApplication.delayCall += Restore;

        static void Restore()
        {
            if (_was == null) return;
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (urp != null) { urp.renderScale = _was.Value; EditorUtility.SetDirty(urp); AssetDatabase.SaveAssets(); }
            Debug.Log("[PicoBuild] renderScale back to " + _was.Value.ToString("0.00"));
            _was = null;
        }
    }
}
