using System.Collections.Generic;
using System.IO;
using GaussianSplatting.Runtime;
using MusePico.Worlds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Bakes every <see cref="SplatWalkArea"/> in the open scene from its world's splats: reads the
    /// splat centres back from the GPU (the same export the package's "Export PLY" uses), runs
    /// <see cref="WalkMask"/>, and saves the walkable floor as a mesh asset beside the scene.
    ///
    /// Re-run after replacing or moving a world. Existing mesh assets are overwritten in place, so
    /// scene references survive.
    /// </summary>
    static class WalkAreaBake
    {
        const int FloatsPerSplat = 62;          // GaussianSplatting.Editor.Utils.InputSplatData
        const int OpacityFloat = 54;

        [MenuItem("MuseXR/Painters/Bake Walk Areas")]
        static void BakeMenu()
        {
            foreach (var line in BakeAll()) Debug.Log("[WalkAreaBake] " + line);
        }

        /// <summary>Bake every walk area in the open scenes; one report line each.</summary>
        public static List<string> BakeAll(WalkMask.Settings settings = null)
        {
            var report = new List<string>();
            foreach (var area in Object.FindObjectsByType<SplatWalkArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                report.Add(Bake(area, settings ?? new WalkMask.Settings()));
            if (report.Count == 0) report.Add("no SplatWalkArea in the open scene.");
            return report;
        }

        static string Bake(SplatWalkArea area, WalkMask.Settings settings)
        {
            var world = area.GetComponentInParent<GaussianSplatRenderer>(true);
            if (world == null) return $"{area.name}: no GaussianSplatRenderer above it.";

            // Worlds upload progressively, and in the Editor that only advances while Unity renders,
            // so an unfocused Editor can sit at a tenth of a world indefinitely. Re-create the
            // renderer's resources with progressive upload off to get every splat, then restore it.
            if (world.asset != null && world.splatCount < world.asset.splatCount && world.m_ProgressiveUpload)
            {
                world.m_ProgressiveUpload = false;
                world.enabled = false;
                world.enabled = true;
                world.m_ProgressiveUpload = true;
            }

            int n = world.splatCount;
            if (n <= 0) return $"{world.name}: no splats loaded.";
            // Worlds upload progressively after a load or recompile; the export sees only what has
            // arrived. Found 29 Sep 2026: a bake straight after a recompile read 50,000 of 500,000
            // splats and gave the garden no floor at all.
            if (world.asset != null && n < world.asset.splatCount)
                return $"{world.name}: only {n:N0} of {world.asset.splatCount:N0} splats uploaded yet - not baked. Wait a moment and bake again.";
            var data = new float[n * FloatsPerSplat];
            using (var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, FloatsPerSplat * sizeof(float)))
            {
                if (!world.EditExportData(buffer, false)) return $"{world.name}: export failed (renderer not ready).";
                buffer.GetData(data);
            }

            var toWorld = world.transform.localToWorldMatrix;
            var points = new List<Vector3>(n / 2);
            for (int i = 0; i < n; i++)
            {
                int o = i * FloatsPerSplat;
                if (WalkMask.Opacity(data[o + OpacityFloat]) < settings.minOpacity) continue;
                points.Add(toWorld.MultiplyPoint3x4(new Vector3(data[o], data[o + 1], data[o + 2])));
            }

            float floorY = world.transform.position.y;
            var seeds = new List<Vector2> { Flat(world.transform.position) };
            foreach (var s in area.extraSeeds) seeds.Add(Flat(area.transform.TransformPoint(s)));

            var grid = WalkMask.Build(points, floorY, seeds, settings);
            var mesh = SplatWalkArea.BuildMesh(grid, floorY, area.transform.worldToLocalMatrix, "WalkArea-" + world.name);

            var scenePath = area.gameObject.scene.path;
            var folder = Path.Combine(Path.GetDirectoryName(scenePath) ?? "Assets", Path.GetFileNameWithoutExtension(scenePath)).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            var path = $"{folder}/WalkArea-{Sanitise(world.name)}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();

            var filter = area.GetComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            float m2 = grid.WalkableCount * grid.cell * grid.cell;
            area.bakeReport = $"{System.DateTime.Now:yyyy-MM-dd HH:mm} - {points.Count:N0} of {n:N0} splats opaque; " +
                              $"{m2:F0} m2 walkable ({grid.WalkableCount} cells of {grid.cell} m); {mesh.triangles.Length / 3} triangles.";
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(area);
            EditorSceneManager.MarkSceneDirty(area.gameObject.scene);
            return $"{world.name}: {area.bakeReport} -> {path}";
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static string Sanitise(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            return s.Replace(' ', '-').Replace("(", "").Replace(")", "");
        }
    }
}
