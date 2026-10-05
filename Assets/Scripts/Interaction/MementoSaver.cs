using System;
using System.IO;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her memento's Save (4.5; plan Q7 (a)): the card rendered on its own, crisp, to <c>memento.png</c>. Hers exports
    /// a PNG from a 2D page; in the headset the card is drawn by a camera that sees only it, onto a paper-coloured
    /// ground, and written where the visitor can find it - Pictures/MUSE on the headset (registered with the media
    /// store, so it appears in the file browser), the user's Pictures/MUSE in the Editor.
    /// </summary>
    public static class MementoSaver
    {
        /// <summary>A layer nothing else in the journey uses: the capture camera sees the card and nothing more.</summary>
        public const int CaptureLayer = 31;
        public const int WidthPx = 1600;

        /// <summary>The folder a memento is written to on this platform.</summary>
        public static string Folder()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return "/storage/emulated/0/Pictures/MUSE";
#else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MUSE");
#endif
        }

        /// <summary>The file name for a memento saved now: one per save, never overwriting the last.</summary>
        public static string FileName(DateTime when) => "memento-" + when.ToString("yyyyMMdd-HHmmss") + ".png";

        /// <summary>Render <paramref name="card"/> (a world-space canvas's rect) and save it. Returns the path, or null.</summary>
        public static string Save(RectTransform card, Color paper)
        {
            if (card == null) return null;
            var corners = new Vector3[4];
            card.GetWorldCorners(corners);
            var centre = (corners[0] + corners[2]) * 0.5f;
            var width = Vector3.Distance(corners[0], corners[3]);
            var height = Vector3.Distance(corners[0], corners[1]);
            if (width < 1e-4f || height < 1e-4f) return null;

            // The card alone, on the capture layer for one render.
            var layers = new System.Collections.Generic.List<(GameObject go, int layer)>();
            foreach (var t in card.GetComponentsInChildren<Transform>(true)) { layers.Add((t.gameObject, t.gameObject.layer)); t.gameObject.layer = CaptureLayer; }
            var groups = card.GetComponentsInParent<CanvasGroup>(true);
            var alphas = new float[groups.Length];
            for (var i = 0; i < groups.Length; i++) { alphas[i] = groups[i].alpha; groups[i].alpha = 1f; }

            var margin = 1.08f;
            var hPx = Mathf.RoundToInt(WidthPx * height / width);
            var rt = new RenderTexture(WidthPx, hPx, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var camGo = new GameObject("Memento Capture");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = height * 0.5f * margin;
            cam.aspect = width / height;
            cam.cullingMask = 1 << CaptureLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = paper;
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 4f;
            var forward = card.forward;   // +Z away from the viewer: the camera looks the way a reader does
            cam.transform.SetPositionAndRotation(centre - forward * 1f, Quaternion.LookRotation(forward, card.up));
            cam.targetTexture = rt;
            string path = null;
            try
            {
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(WidthPx, hPx, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, WidthPx, hPx), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var folder = Folder();
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, FileName(DateTime.Now));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                Announce(path);
                Debug.Log("[Memento] saved " + path);
            }
            catch (Exception ex) { Debug.LogWarning("[Memento] could not save: " + ex.Message); path = null; }
            finally
            {
                cam.targetTexture = null;
                UnityEngine.Object.Destroy(camGo);
                rt.Release(); UnityEngine.Object.Destroy(rt);
                foreach (var (go, layer) in layers) if (go != null) go.layer = layer;
                for (var i = 0; i < groups.Length; i++) if (groups[i] != null) groups[i].alpha = alphas[i];
            }
            return path;
        }

        /// <summary>On the headset: tell the media store, so the file shows in the gallery and the file browser at once.</summary>
        static void Announce(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var scanner = new AndroidJavaClass("android.media.MediaScannerConnection"))
                    scanner.CallStatic("scanFile", activity, new[] { path }, new[] { "image/png" }, null);
            }
            catch (Exception ex) { Debug.LogWarning("[Memento] media scan: " + ex.Message); }
#endif
        }
    }
}
