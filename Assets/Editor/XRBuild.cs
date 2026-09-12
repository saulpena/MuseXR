using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// PICO and Quest need separate APKs, and the reason is not OpenXR — it is PICO's own
    /// build validator. PICOFeature.GetValidationChecks fails the build when any non-PICO
    /// interaction profile is enabled ("Only the PICO Touch Interaction Profile is supported
    /// right now"), so the two vendors' controller profiles cannot ship in one APK.
    ///
    /// This switches the whole project between vendor profiles in one step: OpenXR features,
    /// scripting defines, and the product name. Build, or just switch and press Play.
    ///
    ///   MuseXR > Switch To > PICO / Quest      set the profile, do not build
    ///   MuseXR > Build > PICO APK / Quest APK  set the profile and build
    /// </summary>
    public static class XRBuild
    {
        const string BuildDir = "Builds";
        const string Scene = "Assets/Scenes/Tests/XRRig.unity";

        /// <summary>Required by the PICO package for its OpenXR path to compile at all.
        /// Kept on for both profiles: toggling it forces a full recompile, and it is inert
        /// for Quest once the PICO features are disabled.</summary>
        const string PicoSdkDefine = "ENABLE_PICO_OPENXR_SDK";

        const string PicoDefine = "MUSEXR_PICO";
        const string QuestDefine = "MUSEXR_QUEST";

        // OpenXRExtensions is not optional: without it PICO's validator reports
        // "No PICO OpenXR Features selected" and refuses to build. The message does not name it.
        static readonly string[] PicoFeatures =
        {
            "PICOFeature", "OpenXRExtensions",
            "PICO4ControllerProfile", "PICO4UltraControllerProfile", "PICONeo3ControllerProfile",
            "FoveationFeature", "DisplayRefreshRateFeature",
        };

        static readonly string[] QuestFeatures =
        {
            "MetaQuestFeature",
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile",
            "MetaQuestTouchProControllerProfile",
        };

        public enum Vendor { Pico, Quest }

        // ---- menu -------------------------------------------------------------------

        [MenuItem("MuseXR/Switch To/PICO", priority = 0)]
        public static void SwitchToPico() { ApplyProfile(Vendor.Pico); }

        [MenuItem("MuseXR/Switch To/Quest", priority = 1)]
        public static void SwitchToQuest() { ApplyProfile(Vendor.Quest); }

        [MenuItem("MuseXR/Build/PICO APK", priority = 20)]
        public static void BuildPico() { Build(Vendor.Pico); }

        [MenuItem("MuseXR/Build/Quest APK", priority = 21)]
        public static void BuildQuest() { Build(Vendor.Quest); }

        [MenuItem("MuseXR/Build/Both", priority = 22)]
        public static void BuildBoth() { BuildPico(); BuildQuest(); }

        // ---- profile switching ------------------------------------------------------

        public static void ApplyProfile(Vendor vendor)
        {
            SelectFeatures(vendor == Vendor.Pico ? PicoFeatures : QuestFeatures);
            SetDefines(vendor);
            PlayerSettings.productName = vendor == Vendor.Pico ? "MuseXR" : "MuseXR (Quest)";
            AssetDatabase.SaveAssets();
            Debug.Log($"[XRBuild] profile -> {vendor}. Features and defines updated.");
        }

        /// <summary>Enables exactly the named OpenXR features for Android and disables every
        /// other one, so the two vendor profiles cannot drift into each other over time.</summary>
        public static void SelectFeatures(IEnumerable<string> wanted)
        {
            var want = new HashSet<string>(wanted);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) throw new Exception("No OpenXR settings for Android.");

            foreach (var feature in settings.GetFeatures())
            {
                if (feature == null) continue;
                bool shouldBeOn = want.Contains(feature.GetType().Name);
                if (feature.enabled == shouldBeOn) continue;

                var so = new SerializedObject(feature);
                var prop = so.FindProperty("m_enabled");
                if (prop == null) continue;
                prop.boolValue = shouldBeOn;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(feature);
            }
        }

        /// <summary>Swaps MUSEXR_PICO / MUSEXR_QUEST so gameplay code can branch on the target,
        /// e.g. #if MUSEXR_PICO … #endif. Leaves every unrelated define untouched.</summary>
        public static void SetDefines(Vendor vendor)
        {
            var defines = PlayerSettings
                .GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d != PicoDefine && d != QuestDefine)
                .ToList();

            if (!defines.Contains(PicoSdkDefine)) defines.Add(PicoSdkDefine);
            defines.Add(vendor == Vendor.Pico ? PicoDefine : QuestDefine);

            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                BuildTargetGroup.Android, string.Join(";", defines));
        }

        // ---- build ------------------------------------------------------------------

        static void Build(Vendor vendor)
        {
            ApplyProfile(vendor);

            Directory.CreateDirectory(BuildDir);
            string apk = vendor == Vendor.Pico ? "MuseXR-PICO.apk" : "MuseXR-Quest.apk";
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Path.Combine(BuildDir, apk),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[XRBuild] {apk}: {s.result} — {s.totalSize / 1048576} MB, " +
                      $"{s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalTime}");
            if (s.result != BuildResult.Succeeded)
                throw new Exception($"{apk} build {s.result}");
        }

        /// <summary>Headless entry point:
        /// Unity -batchmode -quit -executeMethod MuseXR.EditorTools.XRBuild.CI
        /// with MUSEXR_TARGET=pico|quest (defaults to pico).</summary>
        public static void CI()
        {
            var which = Environment.GetEnvironmentVariable("MUSEXR_TARGET") ?? "pico";
            Build(which.Equals("quest", StringComparison.OrdinalIgnoreCase) ? Vendor.Quest : Vendor.Pico);
        }
    }
}
