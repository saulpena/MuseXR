using System.IO;
using GaussianSplatting.Editor;
using GaussianSplatting.Runtime;
using MuseXR.Worlds;
using UnityEditor;
using UnityEngine;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Conversion for the splat portal test (Assets/Scenes/Tests/SplatPortal.unity).
    ///
    /// Convert: the two hand-off worlds, at the project's Medium quality, unchanged — Marble's own
    /// 500k, nothing cut. Bake: reconvert the world BEYOND the door with the splats seen through
    /// the door from the approach stored first, and their count on the asset. Same splats, same
    /// data; only the order changes, so every other scene using the asset renders exactly as before.
    /// </summary>
    static class SplatPortalBake
    {
        const string HandoffFolder = "Tools/marble/handoff";
        const string OutputFolder = "Assets/Worlds/Marble";

        // Where the visitor can stand while looking through the door, in door space.
        const float ApproachDepth = 9f;
        const float ApproachHalfWidth = 3.5f;
        static readonly float[] EyeHeights = { 1.2f, 1.6f, 1.9f };
        // Grown on every edge of the aperture: the next world also shows in the seep patch round
        // the door (SplatPortalDoor.seepRadius, 2.8 m), plus a splat half-seen past its edge.
        const float Margin = 3.3f;

        [MenuItem("MuseXR/Portal/Convert Hand-off Worlds")]
        static void ConvertHandoff()
        {
            foreach (var name in new[] { "buddha-hall-chisel-500k", "vangogh-gallery-chisel-500k" })
            {
                var asset = GaussianSplatAssetCreator.CreateMedium(Source(name), OutputFolder, null, out var error);
                if (asset == null) { Debug.LogError($"[SplatPortal] {name}: {error}"); continue; }
                Debug.Log($"[SplatPortal] converted {name}: {asset.splatCount} splats, bounds {asset.boundsMin} .. {asset.boundsMax}");
            }
        }

        [MenuItem("MuseXR/Portal/Bake Through-Door Order")]
        static void BakeThroughDoorOrder()
        {
            var door = Object.FindFirstObjectByType<SplatPortalDoor>();
            if (door == null || door.nextWorld == null || door.nextWorld.asset == null || door.aperture == null)
            {
                Debug.LogError("[SplatPortal] Open the portal scene: it needs a SplatPortalDoor with a next world and an aperture.");
                return;
            }

            var next = door.nextWorld;
            string name = next.asset.name;
            var half = door.apertureSize * 0.5f;
            var doorToWorld = Matrix4x4.TRS(door.aperture.position, door.aperture.rotation, Vector3.one);
            // The asset's own object space -> door space.
            var objectToDoor = doorToWorld.inverse * next.transform.localToWorldMatrix;
            var eyes = PortalGeometry.ApproachEyes(half, ApproachDepth, ApproachHalfWidth, EyeHeights, 9, 7);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var asset = GaussianSplatAssetCreator.CreateMedium(Source(name), OutputFolder,
                p => PortalGeometry.VisibleFromAny(eyes, objectToDoor.MultiplyPoint3x4(p), half, Margin),
                out var error);
            if (asset == null) { Debug.LogError($"[SplatPortal] {name}: {error}"); return; }

            Debug.Log($"[SplatPortal] baked {name}: {asset.priorityCount} of {asset.splatCount} splats " +
                      $"({100.0 * asset.priorityCount / asset.splatCount:F1}%) seen through the door from " +
                      $"{eyes.Length} approach eyes, {watch.Elapsed.TotalSeconds:F1} s");
        }

        static string Source(string name)
        {
            var path = Path.GetFullPath(Path.Combine(HandoffFolder, name + ".spz"));
            if (!File.Exists(path)) Debug.LogError($"[SplatPortal] missing source {path}");
            return path;
        }
    }
}
