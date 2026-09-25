using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Sets the splat layer's resolution at startup and lets the left X button (F9 in the Editor)
    /// cycle it live, so two resolutions can be compared in the headset in the same moment and
    /// pose, and measured from the desk. Every change is logged as [SplatScale] for logcat.
    ///
    /// Created by itself after the first scene loads, so no scene carries it. X is free: the
    /// trigger, grip and B/Y are bound by XrButtons, and the right stick is XRI's snap-turn.
    /// </summary>
    public sealed class SplatScaleCycler : MonoBehaviour
    {
        InputAction _cycle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            GaussianSplatSettings.ResolutionScale = SplatRenderTuning.SplatResolutionScale;
            Log("start");
            var go = new GameObject(nameof(SplatScaleCycler));
            DontDestroyOnLoad(go);
            go.AddComponent<SplatScaleCycler>();
        }

        void OnEnable()
        {
            _cycle = new InputAction("splat-scale-cycle", InputActionType.Button);
            _cycle.AddBinding("<XRController>{LeftHand}/primaryButton");
            _cycle.AddBinding("<Keyboard>/f9");
            _cycle.Enable();
        }

        void OnDisable() { _cycle?.Disable(); _cycle?.Dispose(); _cycle = null; }

        void Update()
        {
            if (_cycle == null || !_cycle.WasPressedThisFrame()) return;
            GaussianSplatSettings.ResolutionScale = SplatRenderTuning.NextSplatScale(GaussianSplatSettings.ResolutionScale);
            Log("cycled");
        }

        static void Log(string why) =>
            Debug.Log($"[SplatScale] {why}: splat layer {GaussianSplatSettings.ResolutionScale:0.00} of the camera " +
                      "target (text and meshes unaffected)");
    }
}
