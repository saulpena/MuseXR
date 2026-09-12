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
    /// right now"), so the two vendors' controller profiles cannot ship together.
    ///
    /// Rather than toggle a dozen checkboxes by hand before every build, this flips the
    /// feature set and builds in one step.
    /// </summary>
    public static class XRBuild
    {
        const string BuildDir = "Builds";
        const string Scene = "Assets/Scenes/Tests/XRRig.unity";

        // Enabled for a PICO build. OpenXRExtensions is required: without it PICO's validator
        // reports "No PICO OpenXR Features selected" and refuses to build.
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

        [MenuItem("MuseXR/Build/PICO APK")]
        public static void BuildPico() { Build(PicoFeatures, "MuseXR-PICO.apk"); }

        [MenuItem("MuseXR/Build/Quest APK")]
        public static void BuildQuest() { Build(QuestFeatures, "MuseXR-Quest.apk"); }

        [MenuItem("MuseXR/Build/Both")]
        public static void BuildBoth() { BuildPico(); BuildQuest(); }

        /// <summary>Enables exactly the named features for Android, disabling every other one.</summary>
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
            AssetDatabase.SaveAssets();
        }

        static void Build(string[] features, string apkName)
        {
            SelectFeatures(features);

            Directory.CreateDirectory(BuildDir);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Path.Combine(BuildDir, apkName),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[XRBuild] {apkName}: {s.result} — {s.totalSize / 1048576} MB, " +
                      $"{s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalTime}");
            if (s.result != BuildResult.Succeeded)
                throw new Exception($"{apkName} build {s.result}");
        }

        /// <summary>Entry point for headless builds: Unity -batchmode -executeMethod MuseXR.EditorTools.XRBuild.CI</summary>
        public static void CI()
        {
            var which = Environment.GetEnvironmentVariable("MUSEXR_TARGET") ?? "pico";
            if (which.Equals("quest", StringComparison.OrdinalIgnoreCase)) BuildQuest();
            else BuildPico();
        }
    }
}
