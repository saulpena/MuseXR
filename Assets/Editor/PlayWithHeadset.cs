using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Whether Play in the Editor starts OpenXR. Play uses the Standalone XR settings whatever the build target, and
    /// with the Meta runtime active but no Link connected, starting OpenXR blocks the Editor for good ("Not
    /// Responding" right after "registered Provider for OpenXR Display" - Saul, 5 Oct). Off: desktop play (mouse
    /// hand, WASD). On: start Quest Link first, then press Play. Android builds use their own settings: untouched.
    /// </summary>
    static class PlayWithHeadset
    {
        const string Item = "MuseXR/Play With Headset (Link)";

        static XRGeneralSettings Standalone =>
            XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);

        [MenuItem(Item)]
        static void Toggle()
        {
            var s = Standalone;
            if (s == null) { UnityEngine.Debug.LogWarning("[XR] no Standalone XR settings"); return; }
            s.InitManagerOnStart = !s.InitManagerOnStart;
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[XR] Play With Headset (Link): " +
                (s.InitManagerOnStart ? "ON - start Quest Link before pressing Play" : "OFF - desktop play, XR not started"));
        }

        [MenuItem(Item, true)]
        static bool Validate()
        {
            var s = Standalone;
            Menu.SetChecked(Item, s != null && s.InitManagerOnStart);
            return s != null;
        }
    }
}
