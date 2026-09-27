using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Test switches read from the Android launch intent, so a test configuration is chosen at launch
    /// instead of being baked into a build. Every test build is then plain committed code.
    ///
    ///   adb shell am start -n com.musexr.impossiblemuseum/com.unity3d.player.UnityPlayerActivity \
    ///       --ez musexr.capturePose true --es musexr.homeWorld celestial-peach-blossom-paradise-500k
    ///
    /// A normal launch from the headset's library carries no extras, so none of this applies.
    /// In the Editor every switch reads as unset.
    /// </summary>
    public static class LaunchOptions
    {
        public const string CapturePoseExtra = "musexr.capturePose";
        public const string HomeWorldExtra = "musexr.homeWorld";
        public const string FreeWalkExtra = "musexr.freeWalk";
        public const string CaptureYawExtra = "musexr.captureYaw";
        public const string SelfTestExtra = "musexr.selfTest";

        /// <summary>
        /// Walk the gallery's dialogue path unattended and log each step: invite three masters,
        /// open a work (live readings), answer it, ask a master a question (live). For verifying a
        /// build on a headset from the desk, where no controller is in anyone's hand. It makes
        /// real paid calls, which is its point.
        /// </summary>
        public static bool SelfTest => BoolExtra(SelfTestExtra);

        /// <summary>World to open in the opening stages instead of the scene's homeWorldKey; null = no override.</summary>
        public static string HomeWorldOverride => Validated(StringExtra(HomeWorldExtra));

        public static bool CapturePose => BoolExtra(CapturePoseExtra);

        /// <summary>
        /// Walk in every stage once the world's floor exists, not only in the gallery stage. For
        /// recording and testing a world on foot without playing through the journey first.
        /// </summary>
        public static bool FreeWalk => BoolExtra(FreeWalkExtra);

        /// <summary>Capture mode only: degrees added to the fixed view's yaw, to look at what is beside or behind the spawn.</summary>
        public static float CaptureYaw => FloatExtra(CaptureYawExtra);

        /// <summary>
        /// Only a key the catalog knows is honoured: a typo on the command line would otherwise load
        /// nothing and look exactly like a broken build.
        /// </summary>
        public static string Validated(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var w in WorldCatalog.Small)
                if (w.key == key) return key;
            Debug.LogWarning($"[LaunchOptions] {HomeWorldExtra}='{key}' is not a WorldCatalog.Small key; ignored");
            return null;
        }

        static string StringExtra(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intent = Intent();
                return intent.Call<string>("getStringExtra", name);
            }
            catch (System.Exception e) { Debug.LogWarning($"[LaunchOptions] {name}: {e.Message}"); return null; }
#else
            return null;
#endif
        }

        static float FloatExtra(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intent = Intent();
                return intent.Call<float>("getFloatExtra", name, 0f);
            }
            catch (System.Exception e) { Debug.LogWarning($"[LaunchOptions] {name}: {e.Message}"); return 0f; }
#else
            return 0f;
#endif
        }

        static bool BoolExtra(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intent = Intent();
                return intent.Call<bool>("getBooleanExtra", name, false);
            }
            catch (System.Exception e) { Debug.LogWarning($"[LaunchOptions] {name}: {e.Message}"); return false; }
#else
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject Intent()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            return activity.Call<AndroidJavaObject>("getIntent");
        }
#endif
    }
}
