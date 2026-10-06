using System.IO;
using GaussianSplatting.Editor;
using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEngine;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Swaps the six journey worlds to their 350k versions, in place. Each world is reconverted over its own asset in
    /// Assets/Worlds/Marble from Tools/marble/smallworlds-350k, which keeps its GUID, its data files' GUIDs and its
    /// Addressable address - so the journey loads the lighter world with no reference changed.
    ///
    /// The 350k files are coverage-ranked cuts of the 500k worlds: every splat ranked by how much of the walk area's
    /// view it covers, the least-seen 30% dropped. Blind-reviewed on PC renders and on headset screenshots from a
    /// walking tour of all six worlds (5-6 Oct 2026): no visible difference; PICO 4 +5 to +7 FPS in every world. The
    /// 250k cut was rejected (black blotches on the Palace steps). Back to 500k: reconvert from
    /// Tools/marble/smallworlds (MuseXR > Worlds > Use 500k Worlds).
    /// </summary>
    public static class Worlds350k
    {
        static readonly string[] Names =
        {
            "grand-conservatory-with-lush-gardens-500k", "palace-court-of-keeping-500k", "grotto-hall-of-time-500k",
            "van-gogh-inspired-gallery-interior-500k", "enchanted-water-garden-sanctuary-500k",
            "fantasy-realm-of-shimmering-spheres-500k",
        };

        /// <summary>
        /// What ships (Saul, 6 Oct 2026, after comparing 500k / 350k / 250k of every world in the headset
        /// in Assets/Scenes/Tests/SplatCompare.unity): the Palace and Van Gogh at 350k, the rest at 250k. The Palace's 250k
        /// cut put black blotches on its steps; 250k on the PICO is ~7 ms a frame cheaper than 350k.
        /// </summary>
        static readonly string[] Ship350k = { "palace-court-of-keeping-500k", "van-gogh-inspired-gallery-interior-500k" };

        [MenuItem("MuseXR/Worlds/Use Shipping Worlds (250k, Palace and Van Gogh 350k)")]
        public static void UseShipping()
        {
            foreach (var name in Names)
            {
                var at350 = System.Array.IndexOf(Ship350k, name) >= 0;
                Convert(at350 ? "Tools/marble/smallworlds-350k" : "Tools/marble/smallworlds-250k", at350 ? 350000 : 250000, name);
            }
        }

        [MenuItem("MuseXR/Worlds/Use 350k Worlds")]
        public static void Use350k() => Convert("Tools/marble/smallworlds-350k", 350000);

        [MenuItem("MuseXR/Worlds/Use 500k Worlds")]
        public static void Use500k() => Convert("Tools/marble/smallworlds", 500000);

        static void Convert(string sourceFolder, int expected, string only = null)
        {
            foreach (var name in Names)
            {
                if (only != null && name != only) continue;
                var source = Path.GetFullPath(Path.Combine(sourceFolder, name + ".spz"));
                if (!File.Exists(source)) { Debug.LogError("[Worlds] missing " + source); continue; }
                var path = "Assets/Worlds/Marble/" + name + ".asset";
                var guid = AssetDatabase.AssetPathToGUID(path);
                var asset = GaussianSplatAssetCreator.CreateMedium(source, "Assets/Worlds/Marble", null, out var error);
                if (asset == null) { Debug.LogError("[Worlds] " + name + ": " + error); continue; }
                var kept = string.IsNullOrEmpty(guid) || guid == AssetDatabase.AssetPathToGUID(path);
                var log = "[Worlds] " + name + ": " + asset.splatCount + " splats" + (kept ? "" : " - GUID CHANGED, Addressables need re-marking");
                if (asset.splatCount != expected || !kept) Debug.LogError(log); else Debug.Log(log);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
