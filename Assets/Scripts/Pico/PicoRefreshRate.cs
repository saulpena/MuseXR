using System.Collections;
using UnityEngine;

namespace MuseXR.Pico
{
    /// <summary>
    /// Test M4 (6 Oct): the PICO display at 72 Hz instead of 90. At 25-30 FPS the app is far from 90, and PICO's docs
    /// list a refresh rate the app cannot keep up with among the causes of tearing. The project setting
    /// (PICOProjectSetting.displayFrequency) never applied in a build, so the rate is asked for here once the OpenXR
    /// session is running - the way musexr-b-3e's probe verified it on the headset (PxrMetric /90 -> /72).
    /// PICO builds only; the Quest build compiles none of it.
    /// </summary>
    public sealed class PicoRefreshRate : MonoBehaviour
    {
        public const float TargetHz = 72f;

#if MUSEXR_PICO && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject(nameof(PicoRefreshRate));
            DontDestroyOnLoad(go);
            go.AddComponent<PicoRefreshRate>();
        }

        IEnumerator Start()
        {
            yield return new WaitForSeconds(1.5f);
            float was = 0f;
            var read = ByteDance.PICO.OpenXR.DisplayRefreshRateFeature.GetDisplayRefreshRate(ref was);
            var set = ByteDance.PICO.OpenXR.DisplayRefreshRateFeature.SetDisplayRefreshRate(TargetHz);
            yield return new WaitForSeconds(1f);
            float now = 0f;
            ByteDance.PICO.OpenXR.DisplayRefreshRateFeature.GetDisplayRefreshRate(ref now);
            Debug.Log("[PicoRefreshRate] asked for " + TargetHz + " Hz: was " + (read ? was.ToString("0") : "?") + ", set " + set + ", now " + now.ToString("0"));
        }
#endif
    }
}
