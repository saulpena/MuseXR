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
        public const float Intensity = 2.6f;
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

        /// <summary>Let the lamp reach this renderer. It keeps its other layers, so the room's own light still falls on it.</summary>
        public static void MarkRelief(Renderer r) => r.renderingLayerMask |= ReliefMask;
    }
}
