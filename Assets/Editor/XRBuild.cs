using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Six builds: three scenes on two vendors.
    ///
    /// PICO and Quest cannot share one APK — PICO's own validator
    /// (PICOFeature.GetValidationChecks) fails the build when any non-PICO interaction profile
    /// is enabled: "Only the PICO Touch Interaction Profile is supported right now." OpenXR
    /// would be fine with both; PICO's validator is not.
    ///
    ///   Rig      no worlds at all — proves the rig, tracking and controllers work
    ///   Worlds   Skylar's 8 at full res (~29.8M splats). Expected to struggle: World Labs'
    ///            own docs say 2M+ crashes standalone VR. Built to measure how badly.
    ///   Samples  World Labs' 5 free worlds at 500k (~2.5M splats). The realistic option.
    ///
    /// The two vendors cannot be built in one batch: they differ by scripting define, and writing
    /// defines queues a domain reload that tears down whatever is driving the batch. The sequence
    /// is Switch To > PICO, wait for the recompile, Build > PICO > All Three, then the same for
    /// Quest. Within one vendor nothing changes the defines, so the three builds run uninterrupted.
    /// </summary>
    public static class XRBuild
    {
        const string BuildDir = "Builds";

        // PICO Unity SDK 6.1.1 gates ALL of its OpenXR code (121 files) behind this. Without it
        // the PICOFeature/interaction-profile classes do not exist and the PICO profile is empty.
        // The standalone PICO Unity OpenXR SDK 1.4.1 does NOT use it — harmless there either way.
        const string PicoSdkDefine = "ENABLE_PICO_OPENXR_SDK";
        const string PicoDefine = "MUSEXR_PICO";
        const string QuestDefine = "MUSEXR_QUEST";

        // OpenXRExtensions is not optional: without it PICO's validator reports
        // "No PICO OpenXR Features selected" and refuses to build. The message never names it.
        static readonly string[] PicoFeatures =
        {
            "PICOFeature", "OpenXRExtensions",
            "PICO4ControllerProfile", "PICO4UltraControllerProfile", "PICONeo3ControllerProfile",
            "FoveationFeature", "DisplayRefreshRateFeature",
        };

        /// <summary>
        /// The same PICO profile with the two features the emulator cannot run.
        ///
        /// This is why "MuseXR does not run on the emulator" was true, and it is a named cause
        /// rather than an inherent limit. PICO documents foveation and refresh-rate as unsupported
        /// under the emulator, and the MusePico build that died with SIGABRT inside Houdini ~0.4 s
        /// after Unity created its surface had exactly these two enabled; the same build with them
        /// off ran indefinitely. MuseXR's PICO profile has carried both all along, so it has never
        /// had a fair chance on the emulator.
        ///
        /// Everything else is identical, including the define, so the build menu and its profile
        /// guard work unchanged — only the feature set differs.
        /// </summary>
        static readonly string[] PicoEmulatorFeatures =
        {
            "PICOFeature", "OpenXRExtensions",
            "PICO4ControllerProfile", "PICO4UltraControllerProfile", "PICONeo3ControllerProfile",
        };

        static readonly string[] QuestFeatures =
        {
            "MetaQuestFeature",
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile",
            "MetaQuestTouchProControllerProfile",
        };

        public enum Vendor { Pico, Quest }

        const string DevPrefKey = "MuseXR.DevelopmentBuilds";

        /// <summary>
        /// Whether to produce a development build (profiler + script debugging).
        ///
        /// <b>OFF by default, and that is not a preference — a development build does not run on
        /// PICO 4.</b> Measured 18 Sep 2026 on a PICO 4 (OS 5.13.3, Android 10, Adreno kona
        /// driver): every Development|AllowDebugging build died ~2 s after launch with
        /// SIGSEGV inside <c>vulkan.kona.so</c>, called from
        /// <c>VKGpuProgram::Prepare(GpuProgramParameters&amp;, int)+2692</c> — fault address 0x2,
        /// a null dereference in the driver while compiling a shader. Six builds reproduced it at
        /// byte-identical offsets: the full gallery, the rig-only scene, and a four-object hello
        /// world. It is independent of scene content, of the Gaussian splat shaders, and of every
        /// Unity Vulkan workaround (pre-transform, late-acquire, swapchain count, GPU skinning,
        /// HDR). The SAME hello world as a release build runs.
        ///
        /// This matches a known report against PICO 4 — crash before the splash screen, fixed by
        /// disabling either Vulkan or the development build. Disabling Vulkan is not an option
        /// here: the splat sort needs wave intrinsics that GLES3 does not have.
        ///
        /// So: PICO builds ship as release. The profiler and script debugging stay available in
        /// the Editor and on the emulator, just not on PICO hardware.
        /// </summary>
        public static bool DevelopmentBuilds
        {
            get => EditorPrefs.GetBool(DevPrefKey, false);
            set => EditorPrefs.SetBool(DevPrefKey, value);
        }

        [MenuItem("MuseXR/Development Builds", priority = 10)]
        static void ToggleDevelopmentBuilds() => DevelopmentBuilds = !DevelopmentBuilds;

        [MenuItem("MuseXR/Development Builds", validate = true)]
        static bool ToggleDevelopmentBuildsValidate()
        {
            Menu.SetChecked("MuseXR/Development Builds", DevelopmentBuilds);
            return true;
        }

        class Target
        {
            public string Name;             // Rig / Worlds / Samples
            public string Scene;
            public string AddressableGroup; // null = no worlds in this build
        }

        static readonly Target[] Targets =
        {
            new Target { Name = "Rig",     Scene = "Assets/Scenes/Tests/XRRig.unity",        AddressableGroup = null },
            new Target { Name = "Samples", Scene = "Assets/Scenes/Tests/SampleWorlds.unity", AddressableGroup = AddressableWorldSetup.SampleGroup },

            // Ported from MusePico 14 Sep 2026, converted to Addressables 16 Sep 2026. The two
            // that carry a world now name its group; Gallery/Generate/Salon hold Tripo meshes, not
            // splats, so they stay at null.
            new Target { Name = "SplatLoop", Scene = "Assets/Scenes/SplatLoop.unity", AddressableGroup = AddressableWorldSetup.SampleGroup },
            new Target { Name = "Walk",      Scene = "Assets/Scenes/WalkTest.unity",  AddressableGroup = AddressableWorldSetup.SmallGroup },
            new Target { Name = "Gallery",   Scene = "Assets/Scenes/TripoGallery.unity", AddressableGroup = null },
            new Target { Name = "Generate",  Scene = "Assets/Scenes/TripoRuntime.unity", AddressableGroup = null },
            new Target { Name = "Salon",     Scene = "Assets/Scenes/MuseumSalon.unity",  AddressableGroup = null },
            // The ten-stage journey. It streams the SmallWorlds group: the threshold conservatory
            // behind stages 00-03, and the eight chapters of the exhibition spine after that.
            new Target { Name = "Journey",   Scene = "Assets/Scenes/Museum.unity", AddressableGroup = AddressableWorldSetup.SmallGroup },

            // First world generated by us rather than ported: Marble 1.1, exported at the 500k
            // tier with Coordinate system = OpenGL and Plane level = Ground level.
            new Target { Name = "Sunlit",    Scene = "Assets/Scenes/Tests/SunlitGallery.unity", AddressableGroup = AddressableWorldSetup.MarbleGroup },

            // Skylar's eight captures re-exported from Marble at 500k, cycled one at a time.
            // Its own group, not MarbleGroup: sharing that one made the single-world Sunlit build
            // carry all nine.
            new Target { Name = "Small",     Scene = "Assets/Scenes/Tests/SmallWorlds.unity",   AddressableGroup = AddressableWorldSetup.SmallGroup },

            // The visual-quality benchmark: several versions of the same environment, labelled,
            // with the frame rate on the panel. See QualityBenchCatalog.
            new Target { Name = "Bench",     Scene = "Assets/Scenes/Tests/QualityBench.unity",  AddressableGroup = AddressableWorldSetup.BenchGroup },

            // Every artwork in a ring round the visitor, each one grabbable and two-hand scalable.
            // No worlds, so no Addressables group.
            new Target { Name = "Grab",      Scene = "Assets/Scenes/Tests/GrabPaintings.unity", AddressableGroup = null },

            // The splat portal test: Buddha Hall -> door -> Van Gogh Gallery. Both worlds are
            // direct references in the scene (both must be resident at once), so no group.
            new Target { Name = "Portal",    Scene = "Assets/Scenes/Tests/SplatPortal.unity",   AddressableGroup = null },

            // A world generated on the headset from the DepthRoom depth pano: nothing shipped but the
            // pano in StreamingAssets, so no group. Needs WORLDLABS_API_KEY (KeyInjectionBuildStep).
            new Target { Name = "Dynamic",   Scene = "Assets/Scenes/Tests/DynamicWorld.unity",  AddressableGroup = null },
        };

        /// <summary>
        /// The target a menu item builds, by NAME. Every menu item used to index Targets[n], so
        /// removing one silently renumbered nine of them onto the wrong scene — a build that
        /// succeeds and ships the wrong content is the worst shape this bug could take.
        /// </summary>
        static Target T(string name)
        {
            var t = Targets.FirstOrDefault(x => x.Name == name);
            if (t == null) throw new Exception("[XRBuild] no build target named '" + name + "'");
            return t;
        }

        /// <summary>What "All Three" builds. Named, not the first three, for the reason above.
        /// Worlds (Skylar at full resolution) is gone: 29.8M splats and ~1.37 GB is past what a
        /// mobile headset can do, which the project measured rather than assumed, and Small is the
        /// same eight captures at a size that runs. The assets stay on disk for comparison — see
        /// AddressableWorldSetup.ComparisonOnlyGroups.</summary>
        static Target[] CoreTargets => new[] { T("Rig"), T("Samples"), T("Small") };

        // ---- menu ------------------------------------------------------------------

        [MenuItem("MuseXR/Switch To/PICO", priority = 0)]
        public static void SwitchToPico() => ApplyProfile(Vendor.Pico);

        [MenuItem("MuseXR/Switch To/Quest", priority = 1)]
        public static void SwitchToQuest() => ApplyProfile(Vendor.Quest);

        /// <summary>PICO, minus foveation and refresh-rate, so the build survives the emulator.
        /// Same define as PICO, so Build > PICO > … works afterwards without further switching.</summary>
        [MenuItem("MuseXR/Switch To/PICO (Emulator)", priority = 2)]
        public static void SwitchToPicoEmulator()
        {
            SelectFeatures(PicoEmulatorFeatures);
            bool reloading = SetDefines(Vendor.Pico);
            PlayerSettings.productName = "MuseXR";
            AssetDatabase.SaveAssets();
            Debug.Log("[XRBuild] profile = PICO (Emulator): foveation and refresh-rate DISABLED. " +
                      "Build with MuseXR > Build > PICO > …" +
                      (reloading ? " AFTER the queued domain reload finishes." : ""));
        }

        [MenuItem("MuseXR/Build/PICO/Rig", priority = 20)]       static void P0() => BuildOne(Vendor.Pico, T("Rig"));
        [MenuItem("MuseXR/Build/PICO/Samples", priority = 22)]   static void P2() => BuildOne(Vendor.Pico, T("Samples"));
        [MenuItem("MuseXR/Build/PICO/SplatLoop", priority = 40)] static void P3() => BuildOne(Vendor.Pico, T("SplatLoop"));
        [MenuItem("MuseXR/Build/PICO/Walk", priority = 41)]      static void P4() => BuildOne(Vendor.Pico, T("Walk"));
        [MenuItem("MuseXR/Build/PICO/Gallery", priority = 42)]   static void P5() => BuildOne(Vendor.Pico, T("Gallery"));
        [MenuItem("MuseXR/Build/PICO/Generate", priority = 43)]  static void P6() => BuildOne(Vendor.Pico, T("Generate"));
        [MenuItem("MuseXR/Build/PICO/Salon", priority = 44)]     static void P7() => BuildOne(Vendor.Pico, T("Salon"));
        [MenuItem("MuseXR/Build/PICO/Journey", priority = 47)]   static void P10() => BuildOne(Vendor.Pico, T("Journey"));
        [MenuItem("MuseXR/Build/PICO/Sunlit", priority = 45)]    static void P8() => BuildOne(Vendor.Pico, T("Sunlit"));
        [MenuItem("MuseXR/Build/PICO/Small", priority = 46)]     static void P9() => BuildOne(Vendor.Pico, T("Small"));
        [MenuItem("MuseXR/Build/PICO/Bench", priority = 48)]     static void P11() => BuildOne(Vendor.Pico, T("Bench"));
        [MenuItem("MuseXR/Build/PICO/Grab", priority = 49)]      static void P12() => BuildOne(Vendor.Pico, T("Grab"));

        [MenuItem("MuseXR/Build/Quest/Rig", priority = 20)]       static void Q0() => BuildOne(Vendor.Quest, T("Rig"));
        [MenuItem("MuseXR/Build/Quest/Samples", priority = 22)]   static void Q2() => BuildOne(Vendor.Quest, T("Samples"));
        [MenuItem("MuseXR/Build/Quest/SplatLoop", priority = 40)] static void Q3() => BuildOne(Vendor.Quest, T("SplatLoop"));
        [MenuItem("MuseXR/Build/Quest/Walk", priority = 41)]      static void Q4() => BuildOne(Vendor.Quest, T("Walk"));
        [MenuItem("MuseXR/Build/Quest/Gallery", priority = 42)]   static void Q5() => BuildOne(Vendor.Quest, T("Gallery"));
        [MenuItem("MuseXR/Build/Quest/Generate", priority = 43)]  static void Q6() => BuildOne(Vendor.Quest, T("Generate"));
        [MenuItem("MuseXR/Build/Quest/Salon", priority = 44)]     static void Q7() => BuildOne(Vendor.Quest, T("Salon"));
        [MenuItem("MuseXR/Build/Quest/Journey", priority = 47)]   static void Q10() => BuildOne(Vendor.Quest, T("Journey"));
        [MenuItem("MuseXR/Build/Quest/Sunlit", priority = 45)]    static void Q8() => BuildOne(Vendor.Quest, T("Sunlit"));
        [MenuItem("MuseXR/Build/Quest/Small", priority = 46)]     static void Q9() => BuildOne(Vendor.Quest, T("Small"));
        [MenuItem("MuseXR/Build/Quest/Bench", priority = 48)]     static void Q11() => BuildOne(Vendor.Quest, T("Bench"));
        [MenuItem("MuseXR/Build/Quest/Grab", priority = 49)]      static void Q12() => BuildOne(Vendor.Quest, T("Grab"));
        [MenuItem("MuseXR/Build/PICO/Portal", priority = 50)]    static void P13() => BuildOne(Vendor.Pico, T("Portal"));
        [MenuItem("MuseXR/Build/Quest/Portal", priority = 50)]   static void Q13() => BuildOne(Vendor.Quest, T("Portal"));
        [MenuItem("MuseXR/Build/PICO/Dynamic", priority = 51)]   static void P14() => BuildOne(Vendor.Pico, T("Dynamic"));
        [MenuItem("MuseXR/Build/Quest/Dynamic", priority = 51)]  static void Q14() => BuildOne(Vendor.Quest, T("Dynamic"));

        [MenuItem("MuseXR/Build/PICO/All Three", priority = 25)]
        public static void BuildAllPico() => BuildAllFor(Vendor.Pico);

        [MenuItem("MuseXR/Build/Quest/All Three", priority = 25)]
        public static void BuildAllQuest() => BuildAllFor(Vendor.Quest);

        /// <summary>Refuses to build under the wrong vendor profile rather than switching to it.
        /// Switching writes the scripting defines, which queues a domain reload that aborts the
        /// call before BuildPlayer is ever reached — leaving no APK and no error, which is how
        /// this went unnoticed the first time.</summary>
        static bool GuardProfile(Vendor vendor)
        {
            if (ProfileIsCurrent(vendor)) return true;
            Debug.LogError($"[XRBuild] profile is not {vendor}. Run MuseXR > Switch To > {vendor} " +
                           "first, wait for the recompile to finish, then build. Building now would " +
                           "change the scripting defines and reload the domain mid-build.");
            return false;
        }

        /// <summary>
        /// Addressables' content build calls <c>BuildUtility.CheckModifiedScenesAndAskToSave</c>,
        /// which raises a modal "Unsaved Scenes — Save and Continue" dialog if ANY open scene is
        /// flagged dirty. Driven from the MCP bridge with nobody at the Editor, the build then waits
        /// forever with no log line (measured twice, 25-26 Sep 2026).
        ///
        /// The flag is often spurious: Museum.unity was flagged dirty minutes after a save, and a
        /// save-as-copy of it diffed to ZERO lines against the file on disk. So: a dirty scene whose
        /// serialized content matches disk is saved (writes nothing new, clears the flag); a dirty
        /// scene with real changes stops the build with an error instead of saving unknown edits.
        /// </summary>
        static bool GuardScenes()
        {
            var realChanges = new List<string>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;
                if (string.IsNullOrEmpty(scene.path)) { realChanges.Add("(untitled scene)"); continue; }

                string copy = Path.Combine("Temp", "XRBuild_scenecheck.unity");
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, copy, true);
                bool same = NormalisedText(copy) == NormalisedText(scene.path);
                File.Delete(copy);

                if (same)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                    Debug.Log($"[XRBuild] {scene.path} was flagged modified but matches disk; saved to clear the flag");
                }
                else realChanges.Add(scene.path);
            }
            if (realChanges.Count == 0) return true;
            Debug.LogError("[XRBuild] unsaved changes in " + string.Join(", ", realChanges) + ". Save or revert " +
                           "them first. Refusing to build: Addressables would block on a modal save dialog, and " +
                           "this build will not save edits nobody has looked at.");
            return false;
        }

        static string NormalisedText(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        static void BuildOne(Vendor vendor, Target target)
        {
            if (!GuardProfile(vendor) || !GuardScenes()) return;
            Build(vendor, target);
        }

        /// <summary>All three content sets for one vendor. Deliberately NOT "all six": the two
        /// vendors differ by scripting define, and changing that mid-batch queues a domain reload
        /// that kills the batch. Switch vendor, let the reload finish, then run this.</summary>
        static void BuildAllFor(Vendor vendor)
        {
            if (!GuardProfile(vendor) || !GuardScenes()) return;

            var results = new List<string>();
            foreach (var t in CoreTargets)
            {
                try { results.Add(Build(vendor, t)); }
                catch (Exception e) { results.Add($"{vendor}-{t.Name}: FAILED — {e.Message}"); }
            }
            Debug.Log($"[XRBuild] {vendor} complete:\n  " + string.Join("\n  ", results));
        }

        // ---- profile ---------------------------------------------------------------

        public static void ApplyProfile(Vendor vendor)
        {
            SelectFeatures(vendor == Vendor.Pico ? PicoFeatures : QuestFeatures);
            bool reloading = SetDefines(vendor);
            PlayerSettings.productName = vendor == Vendor.Pico ? "MuseXR" : "MuseXR (Quest)";
            AssetDatabase.SaveAssets();
            Debug.Log($"[XRBuild] profile = {vendor}" +
                      (reloading ? " — defines changed, domain reload queued; build AFTER it finishes." : ""));
        }

        /// <summary>Enables exactly the named features and disables every other one, so the two
        /// vendor profiles cannot drift into each other over repeated switches.</summary>
        public static void SelectFeatures(IEnumerable<string> wanted)
        {
            var want = new HashSet<string>(wanted);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) throw new Exception("No OpenXR settings for Android.");

            foreach (var feature in settings.GetFeatures())
            {
                if (feature == null) continue;
                bool on = want.Contains(feature.GetType().Name);
                if (feature.enabled == on) continue;
                var so = new SerializedObject(feature);
                var p = so.FindProperty("m_enabled");
                if (p == null) continue;
                p.boolValue = on;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(feature);
            }
        }

        /// <summary>Swaps MUSEXR_PICO / MUSEXR_QUEST for #if branching, leaving others alone.
        ///
        /// Writes only when the value actually changes. Assigning defines queues a domain reload,
        /// and a reload part-way through a batch tears down whatever is driving it (the MCP bridge
        /// loses its session, a menu item never returns). Switching vendor therefore has to be its
        /// own step; the builds that follow must find the defines already correct and touch
        /// nothing. Returns true if a reload was queued.</summary>
        public static bool SetDefines(Vendor vendor)
        {
            var current = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);

            var defines = current
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d != PicoDefine && d != QuestDefine)
                .ToList();

            if (!defines.Contains(PicoSdkDefine)) defines.Add(PicoSdkDefine);
            defines.Add(vendor == Vendor.Pico ? PicoDefine : QuestDefine);

            var wanted = string.Join(";", defines);
            if (SameDefines(current, wanted)) return false;

            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, wanted);
            return true;
        }

        static bool SameDefines(string a, string b)
        {
            var sa = new HashSet<string>(a.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()));
            var sb = new HashSet<string>(b.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()));
            return sa.SetEquals(sb);
        }

        /// <summary>True when the defines already match this vendor, so a build can proceed
        /// without queueing a reload.</summary>
        public static bool ProfileIsCurrent(Vendor vendor)
        {
            var d = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);
            var want = vendor == Vendor.Pico ? PicoDefine : QuestDefine;
            var other = vendor == Vendor.Pico ? QuestDefine : PicoDefine;
            var set = new HashSet<string>(d.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            return set.Contains(want) && !set.Contains(other);
        }

        // ---- build -----------------------------------------------------------------

        static string Build(Vendor vendor, Target target)
        {
            ApplyProfile(vendor);

            // Only this build's worlds go into the content, so a Samples APK does not quietly
            // carry 1.37 GB of Skylar's. BuildContent cleans first, so a content build that fails
            // leaves an empty folder rather than the previous target's bundles. Rebuilt for every
            // APK even though the bundles are vendor-independent: a cache shared across runs would
            // trade a long build for a silent staleness bug, which is the worse failure.
            AddressableWorldSetup.SetIncludedGroup(target.AddressableGroup);
            AddressableWorldSetup.BuildContent();

            ClearGradlePackaging();

            Directory.CreateDirectory(BuildDir);
            string apk = $"MuseXR-{(vendor == Vendor.Pico ? "PICO" : "Quest")}-{target.Name}.apk";
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { target.Scene },
                locationPathName = Path.Combine(BuildDir, apk),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                // See DevelopmentBuilds: a development build crashes PICO 4 in the Vulkan driver
                // before anything renders. Release is the default for that reason.
                options = DevelopmentBuilds
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;

            // summary.totalSize is not the APK size — it reported 1694 MB for a 105 MB APK.
            // Measure the file that actually gets sideloaded.
            var path = Path.Combine(BuildDir, apk);
            var mb = File.Exists(path) ? new FileInfo(path).Length / 1048576.0 : 0;
            string line = $"{apk}: {s.result}, {mb:F1} MB, {s.totalErrors} errors, {s.totalTime}";
            Debug.Log("[XRBuild] " + line);
            if (s.result != BuildResult.Succeeded) throw new Exception(line);
            return line;
        }

        /// <summary>
        /// Deletes Gradle's packaging caches so each APK is written fresh.
        ///
        /// Without this, a build whose content SHRANK relative to the previous one reuses the
        /// previous archive's layout: Gradle overwrites the entries that changed and leaves the
        /// freed space as unreferenced holes rather than compacting. Measured here — building
        /// Samples (5 worlds) straight after Worlds (8 worlds) produced a 614 MB APK containing a
        /// single 399.7 MB gap where Skylar's bundles had been. The archive was valid and its
        /// contents correct, just 3x the necessary size; clearing these caches gave 209 MB.
        ///
        /// Note this is under Library/Bee, NOT Temp/gradleOut — Unity 6 moved the Gradle project,
        /// and clearing Temp/ looks like it works while changing nothing.
        /// </summary>
        static void ClearGradlePackaging()
        {
            const string gradle = "Library/Bee/Android/Prj/IL2CPP/Gradle/launcher/build";
            foreach (var sub in new[] { "outputs", "intermediates" })
            {
                var dir = Path.Combine(gradle, sub);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        /// <summary>Headless: -executeMethod MuseXR.EditorTools.XRBuild.CI, MUSEXR_TARGET=pico|quest.
        /// One vendor per invocation — see BuildAllFor for why the two cannot share a run.</summary>
        public static void CI()
        {
            var which = Environment.GetEnvironmentVariable("MUSEXR_TARGET") ?? "pico";
            var vendor = which.Equals("quest", StringComparison.OrdinalIgnoreCase) ? Vendor.Quest : Vendor.Pico;

            // MUSEXR_SCENES: unset -> the three core content sets (what this always built);
            // "all" -> every target including the ported scenes; or a comma-separated list of
            // target names, e.g. "Rig,Walk". Defaulting to the core three keeps an existing CI
            // invocation building exactly what it built before the ported scenes were added.
            var pick = Environment.GetEnvironmentVariable("MUSEXR_SCENES");
            Target[] chosen;
            if (string.IsNullOrWhiteSpace(pick)) chosen = CoreTargets;
            else if (pick.Equals("all", StringComparison.OrdinalIgnoreCase)) chosen = Targets;
            else
            {
                var want = new HashSet<string>(
                    pick.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                chosen = Targets.Where(t => want.Contains(t.Name)).ToArray();
                var unknown = want.Where(w => !Targets.Any(t => t.Name.Equals(w, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (unknown.Length > 0)
                    throw new Exception($"[XRBuild] MUSEXR_SCENES names no such target: {string.Join(", ", unknown)}. " +
                                        $"Known: {string.Join(", ", Targets.Select(t => t.Name))}");
            }

            ApplyProfile(vendor);
            foreach (var t in chosen) Build(vendor, t);
        }
    }
}
