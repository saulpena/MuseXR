using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MuseXR.Interaction
{
    /// <summary>
    /// PICO only (6 Oct, as in musexr-b-3e's T7): SMAA on High on every camera. Post-process anti-aliasing works with the splat layer;
    /// MSAA does not (the splat pass renders into a 1-sample target and its composite breaks on a multisampled camera).
    /// musexr-b-3e's T7 ran this on the PICO 4 Ultra at 29-30 FPS. The Quest build compiles none of it.
    /// </summary>
    public static class PicoAntiAliasing
    {
#if MUSEXR_PICO && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            ApplyAll();
            SceneManager.sceneLoaded += (_, __) => ApplyAll();
        }

        static void ApplyAll()
        {
            var n = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data == null) continue;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                n++;
            }
            Debug.Log("[PicoAA] SMAA High on " + n + " camera(s)");
        }
#endif
    }
}
