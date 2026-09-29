using System.IO;
using MuseXR.Worlds;
using UnityEditor;
using UnityEngine;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Bakes the depth panorama for Marble's <c>pano:depth_to_rgb</c> from the colliders in the open
    /// scene (Assets/Scenes/Tests/DepthRoom.unity): one ray per pixel from the capture point, exact
    /// radial distance, no render targets. Writes the PNG and a JSON sidecar carrying z_min / z_max,
    /// which the API needs to decode a PNG, to Tools/marble/depthpano/.
    ///
    /// Only colliders count, so the EditorOnly reference splats in the scene are invisible to it.
    /// </summary>
    static class DepthPanoBake
    {
        public const string CaptureName = "Pano Camera (capture point)";
        public const string OutputFolder = "Tools/marble/depthpano";

        [MenuItem("MuseXR/Dynamic Worlds/Bake Depth Pano")]
        static void BakeMenu()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var capture = GameObject.Find(CaptureName);
            if (capture == null) { Debug.LogError($"[DepthPano] no '{CaptureName}' in {scene.name}"); return; }
            Bake(capture.transform, Path.Combine(OutputFolder, scene.name.ToLowerInvariant() + "-depth"));
        }

        /// <summary>Writes &lt;basePath&gt;.png and &lt;basePath&gt;.json; returns the measurement.</summary>
        public static DepthPano.Result Bake(Transform capture, string basePath,
                                            int width = DepthPano.Width, int height = DepthPano.Height)
        {
            Physics.SyncTransforms();
            Vector3 origin = capture.position;
            Quaternion rot = capture.rotation;

            var r = DepthPano.Measure(dir =>
                Physics.Raycast(origin, rot * dir, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.distance : 0f, width, height);

            var gray = DepthPano.EncodeGray(r);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var px = new Color32[width * height];
            // Texture rows run bottom-up; the panorama's row 0 is the top.
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte g = gray[y * width + x];
                px[(height - 1 - y) * width + x] = new Color32(g, g, g, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(basePath));
            File.WriteAllBytes(basePath + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            var info = new Sidecar
            {
                z_min = r.zMin, z_max = r.zMax, width = width, height = height, misses = r.misses,
                capture_point = origin, capture_yaw = rot.eulerAngles.y,
                scene = capture.gameObject.scene.path,
                encoding = "world-labs log: 1 - (ln d - ln z_min)/(ln z_max - ln z_min); near white, miss black; centre = +Z",
            };
            File.WriteAllText(basePath + ".json", JsonUtility.ToJson(info, true));
            Debug.Log($"[DepthPano] {basePath}.png  {width}x{height}  z_min {r.zMin:F3}  z_max {r.zMax:F3}  misses {r.misses}");
            return r;
        }

        [System.Serializable]
        class Sidecar
        {
            public float z_min, z_max;
            public int width, height, misses;
            public Vector3 capture_point;
            public float capture_yaw;
            public string scene, encoding;
        }
    }
}
