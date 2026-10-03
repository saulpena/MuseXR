using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The grotto lamp's light (chapter B): "its own mesh with a real-time point light, lighting only
    /// the near relief mesh (normal-mapped), never the splat background". Splats are unlit and take
    /// no light at all, so "never the splat" holds by construction. "Only the relief" is a URP light
    /// layer: the light affects rendering layer 6 alone, and only renderers marked with
    /// <see cref="MarkRelief"/> carry it. Everything else in the room stays exactly as it was.
    /// Layer 6 is "Light Layer 6" in this project's TagManager; URP drops undefined layers.
    /// </summary>
    public sealed class LampLight : MonoBehaviour
    {
        public const int ReliefLayer = 6;
        public static uint ReliefMask => 1u << ReliefLayer;

        public const float Range = 2.2f;
        /// <summary>
        /// Measured on the test relief with the lamp 0.43 m off it: 2.6 clipped 83% of the relief to
        /// white (blind review: "destroys the relief detail it is meant to reveal"); 0.35 adds no
        /// clipping over the unlit 1.5% and still moves the relief by a mean 25/255.
        /// </summary>
        public const float Intensity = 0.35f;
        public static readonly Color Warm = new Color(1f, 0.78f, 0.5f);

        public Light Light { get; private set; }

        /// <summary>Give <paramref name="lamp"/> its flame: a point light at <paramref name="flameLocal"/>.</summary>
        public static LampLight Make(GameObject lamp, Vector3 flameLocal)
        {
            var l = lamp.AddComponent<LampLight>();
            var go = new GameObject("Lamp Light");
            go.transform.SetParent(lamp.transform, false);
            go.transform.localPosition = flameLocal;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = Range;
            light.intensity = Intensity;
            light.color = Warm;
            light.shadows = LightShadows.None;           // a shadow pass for one relief is not worth it on Quest
            light.renderMode = LightRenderMode.ForcePixel;
            var data = light.GetUniversalAdditionalLightData();
            data.renderingLayers = ReliefMask;
            light.renderingLayerMask = (int)ReliefMask;
            l.Light = light;
            return l;
        }

        /// <summary>
        /// Let the lamp reach this renderer. With <paramref name="inNiche"/> the lamp is the ONLY light
        /// that reaches it (ambient still does), as in a cave niche the daylight does not enter. That is
        /// what makes "raising the lamp reveals the relief's depth" true, measured on the test relief
        /// (mean high-pass luminance): niche unlit 0.64; daylight 1.49; niche + lamp raking from the side
        /// 1.72. Without the niche the lamp only adds a warm wash over daylight, and a blind review read
        /// the carving as flatter with the lamp than without it.
        /// </summary>
        public static void MarkRelief(Renderer r, bool inNiche = true) =>
            r.renderingLayerMask = inNiche ? ReliefMask : r.renderingLayerMask | ReliefMask;
    }
}
