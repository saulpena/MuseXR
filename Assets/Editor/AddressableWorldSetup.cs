using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Marks the converted splat assets as Addressables so they can ship in a build and be
    /// loaded and released one at a time.
    ///
    /// Why Addressables at all: the splat converter is editor-only, so worlds must be baked
    /// into the build as assets. Resources would force every world into the APK permanently
    /// (~1.4 GB for Skylar's eight). Addressables loads and unloads on demand, and can later be
    /// hosted remotely so the APK stays small.
    ///
    /// Re-run after converting new worlds — a converted asset is inert until it has an address.
    /// </summary>
    public static class AddressableWorldSetup
    {
        public const string SkylarGroup = "SkylarWorlds";
        public const string SampleGroup = "SampleWorlds";
        /// <summary>Worlds we generated ourselves through Marble, as opposed to Skylar's release
        /// or World Labs' CDN samples. Separate group so a one-world build does not drag in
        /// 1.37 GB, which is what happens if these sit in the SkylarWorlds sweep.</summary>
        public const string MarbleGroup = "MarbleWorlds";

        /// <summary>
        /// Skylar's captures re-exported from Marble at 500k. They live in the same folder as the
        /// worlds we generated ourselves and are told apart by WorldCatalog.SmallSuffix, because
        /// they are a content SET rather than a provenance: a build wants all eight of them or
        /// none. Sharing MarbleGroup with the one-world Sunlit build made that build carry all
        /// nine, which is exactly the bloat MarbleGroup was split off to avoid.
        /// </summary>
        public const string SmallGroup = "SmallWorlds";

        /// <summary>
        /// The quality benchmark's versions (QualityBenchCatalog): the same environment several
        /// ways, converted side by side under bench- keys. Its own group so the benchmark build
        /// carries only them, and so no shipped build ever carries a full-resolution reference.
        /// </summary>
        public const string BenchGroup = "BenchWorlds";

        /// <summary>Bench versions the headset cannot render (QualityBenchCatalog.ReferenceFolder):
        /// Editor comparison only, never in a player build.</summary>
        public const string BenchReferenceGroup = "BenchReference";

        const string SkylarFolder = "Assets/Worlds";
        const string SampleFolder = "Assets/Worlds/Samples";
        const string MarbleFolder = "Assets/Worlds/Marble";
        const string BenchFolder = MuseXR.Worlds.QualityBenchCatalog.Folder;
        const string BenchReferenceFolder = MuseXR.Worlds.QualityBenchCatalog.ReferenceFolder;

        /// <summary>Every group this setup owns, so include/exclude never misses one.</summary>
        public static readonly string[] AllGroups = { SkylarGroup, SampleGroup, MarbleGroup, SmallGroup, BenchGroup, BenchReferenceGroup };

        /// <summary>
        /// Groups that stay on disk but must never reach a player build.
        ///
        /// SkylarWorlds is the eight captures at FULL resolution: 29.8M splats, ~1.37 GB converted.
        /// World Labs' own docs say 2M+ splats crash standalone VR, and this project measured the
        /// rest of the way — that set exists to be compared against, not shipped. SmallWorlds is
        /// the same eight captures at 500k and is what builds.
        ///
        /// Excluded rather than deleted, and still marked Addressable, because Addressables
        /// resolves from the AssetDatabase in the Editor (FastMode). So Play Mode still loads the
        /// full-resolution worlds for a side-by-side, while no APK can carry them.
        /// </summary>
        public static readonly string[] ComparisonOnlyGroups = { SkylarGroup, BenchReferenceGroup };

        public static bool IsComparisonOnly(string groupName) =>
            groupName != null && System.Array.IndexOf(ComparisonOnlyGroups, groupName) >= 0;

        [MenuItem("MuseXR/Addressables/Mark Worlds Addressable")]
        public static void MarkAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null) { Debug.LogError("[Addressables] could not create settings"); return; }

            // Subfolders first, then the root sweep with those subfolders excluded — FindAssets
            // recurses, so without the exclusions Assets/Worlds would claim everything.
            int samples = Mark(settings, SampleFolder, SampleGroup, excludeNested: false);
            // The Marble folder holds two sets. Split them by suffix so a build takes one or the
            // other: CreateOrMoveEntry moves an entry between groups, so re-running this after the
            // split relocates the eight rather than leaving them where they were.
            int small = Mark(settings, MarbleFolder, SmallGroup, excludeNested: false,
                             requireSuffix: MuseXR.Worlds.WorldCatalog.SmallSuffix);
            int marble = Mark(settings, MarbleFolder, MarbleGroup, excludeNested: false,
                              rejectSuffix: MuseXR.Worlds.WorldCatalog.SmallSuffix);
            int bench = Mark(settings, BenchFolder, BenchGroup, excludeNested: false, skipPathPart: "/Reference/");
            int benchRef = Mark(settings, BenchReferenceFolder, BenchReferenceGroup, excludeNested: false);
            int skylar = Mark(settings, SkylarFolder, SkylarGroup, excludeNested: true);

            // Re-assert on every sweep: a newly created group defaults to IncludeInBuild = true,
            // so without this a re-mark would quietly make the full-resolution set shippable again.
            foreach (var g in ComparisonOnlyGroups) SetIncludeInBuild(settings, g, false);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Addressables] {SampleGroup}: {samples} worlds, {MarbleGroup}: {marble} worlds, " +
                      $"{SmallGroup}: {small} worlds, {BenchGroup}: {bench} worlds, {BenchReferenceGroup}: {benchRef} (comparison only), {SkylarGroup}: {skylar} worlds " +
                      $"(comparison only — excluded from every build)");
        }

        static int Mark(AddressableAssetSettings settings, string folder, string groupName,
                        bool excludeNested, string requireSuffix = null, string rejectSuffix = null,
                        string skipPathPart = null)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            var group = EnsureGroup(settings, groupName);

            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:GaussianSplatAsset", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (excludeNested && (path.Contains("/Samples/") || path.Contains("/Marble/") || path.Contains("/Bench/"))) continue;

                if (skipPathPart != null && path.Contains(skipPathPart)) continue;

                var name = Path.GetFileNameWithoutExtension(path);
                if (requireSuffix != null && !name.EndsWith(requireSuffix, System.StringComparison.Ordinal)) continue;
                if (rejectSuffix != null && name.EndsWith(rejectSuffix, System.StringComparison.Ordinal)) continue;

                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                // Address is the world key, so runtime code can ask for a world by name.
                entry.address = Path.GetFileNameWithoutExtension(path);
                n++;
            }
            return n;
        }

        static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string name)
        {
            var group = settings.FindGroup(name);
            if (group == null)
            {
                group = settings.CreateGroup(name, false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            }

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema != null)
            {
                // One bundle per world. Packing together would mean loading every world's data
                // to show one of them, which defeats the point of cycling.
                schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
                schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
                EditorUtility.SetDirty(schema);
            }
            return group;
        }

        /// <summary>Which groups go into the next content build. Addressables has no per-group
        /// include flag, so a group is excluded by emptying it — this moves entries between the
        /// live groups and parked ones instead.</summary>
        [MenuItem("MuseXR/Addressables/Include Only Skylar Worlds")]
        public static void OnlySkylar() => SetIncludedGroup(SkylarGroup);

        [MenuItem("MuseXR/Addressables/Include Only Sample Worlds")]
        public static void OnlySamples() => SetIncludedGroup(SampleGroup);

        [MenuItem("MuseXR/Addressables/Include Only Small Worlds")]
        public static void OnlySmall() => SetIncludedGroup(SmallGroup);

        [MenuItem("MuseXR/Addressables/Include Only Marble Worlds")]
        public static void OnlyMarble() => SetIncludedGroup(MarbleGroup);

        [MenuItem("MuseXR/Addressables/Include None (rig-only build)")]
        public static void IncludeNone() => SetIncludedGroup(null);

        public static void SetIncludedGroup(string keepGroup)
        {
            // Refuse rather than quietly comply. This is the one funnel every build goes through,
            // so it is the only place that can guarantee the full-resolution set never ships —
            // a check on the menu items alone would miss CI and any future caller.
            if (IsComparisonOnly(keepGroup))
                throw new System.InvalidOperationException(
                    $"[Addressables] '{keepGroup}' is comparison-only and cannot be built into a " +
                    "player. It is Skylar's eight captures at full resolution (~1.37 GB, 29.8M " +
                    "splats) and is past what a mobile headset can run. Build 'Small' instead — " +
                    "the same eight captures at 500k. The full-resolution assets stay on disk and " +
                    "still load in Play Mode, which is where the comparison belongs.");

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            foreach (var name in AllGroups)
            {
                var group = settings.FindGroup(name);
                var schema = group?.GetSchema<BundledAssetGroupSchema>();
                if (schema == null) continue;
                bool include = keepGroup != null && name == keepGroup && !IsComparisonOnly(name);
                schema.IncludeInBuild = include;
                EditorUtility.SetDirty(schema);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Addressables] included group: {keepGroup ?? "none"}");
        }

        static void SetIncludeInBuild(AddressableAssetSettings settings, string groupName, bool include)
        {
            var schema = settings.FindGroup(groupName)?.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) return;
            schema.IncludeInBuild = include;
            EditorUtility.SetDirty(schema);
        }

        /// <summary>Builds the Addressables content. Must run before the player build, or the
        /// APK ships with a catalog pointing at bundles that do not exist.</summary>
        public static void BuildContent()
        {
            AddressableAssetSettings.CleanPlayerContent();
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (!string.IsNullOrEmpty(result.Error))
                Debug.LogError($"[Addressables] content build failed: {result.Error}");
            else
                Debug.Log($"[Addressables] content built in {result.Duration:F1}s → {result.OutputPath}");
        }

        [MenuItem("MuseXR/Addressables/Build Content")]
        static void BuildContentMenu() => BuildContent();
    }
}
