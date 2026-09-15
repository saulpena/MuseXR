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

        const string SkylarFolder = "Assets/Worlds";
        const string SampleFolder = "Assets/Worlds/Samples";

        [MenuItem("MuseXR/Addressables/Mark Worlds Addressable")]
        public static void MarkAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null) { Debug.LogError("[Addressables] could not create settings"); return; }

            int samples = Mark(settings, SampleFolder, SampleGroup, excludeSamples: false);
            int skylar = Mark(settings, SkylarFolder, SkylarGroup, excludeSamples: true);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Addressables] {SampleGroup}: {samples} worlds, {SkylarGroup}: {skylar} worlds");
        }

        static int Mark(AddressableAssetSettings settings, string folder, string groupName, bool excludeSamples)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            var group = EnsureGroup(settings, groupName);

            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:GaussianSplatAsset", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (excludeSamples && path.Contains("/Samples/")) continue;

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

        [MenuItem("MuseXR/Addressables/Include None (rig-only build)")]
        public static void IncludeNone() => SetIncludedGroup(null);

        public static void SetIncludedGroup(string keepGroup)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            foreach (var name in new[] { SkylarGroup, SampleGroup })
            {
                var group = settings.FindGroup(name);
                var schema = group?.GetSchema<BundledAssetGroupSchema>();
                if (schema == null) continue;
                bool include = keepGroup != null && name == keepGroup;
                schema.IncludeInBuild = include;
                EditorUtility.SetDirty(schema);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Addressables] included group: {keepGroup ?? "none"}");
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
