using System.Collections;
using System.Collections.Generic;
using System.IO;
using GaussianSplatting.Runtime;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Where the frame time goes, measured on the headset by taking things away one at a time. Dormant: it starts only
    /// when a file named <c>perfprobe</c> appears in the app's files folder (put there over adb), then steps through
    /// <see cref="Steps"/> for <see cref="Seconds"/> each, logging "[PerfProbe] step N name", and puts everything back.
    /// The headset's own counters (VrApi "App=" GPU ms) are read beside the log; the probe measures nothing itself.
    ///
    ///   adb shell touch /sdcard/Android/data/com.musexr.impossiblemuseum/files/perfprobe
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        public const float Seconds = 10f;
        public static readonly string[] Steps =
        {
            "baseline", "lantern lights off", "masters hidden", "lanterns hidden", "ui hidden",
            "splat scale 0.8", "splat scale 0.6", "splats off", "baseline again",
        };

        string _trigger;
        bool _running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<PerfProbe>() != null) return;
            var go = new GameObject("Perf Probe");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfProbe>();
        }

        void Start()
        {
            _trigger = Path.Combine(Application.persistentDataPath, "perfprobe");
            InvokeRepeating(nameof(Poll), 2f, 1f);
        }

        void Poll()
        {
            if (_running || !File.Exists(_trigger)) return;
            try { File.Delete(_trigger); } catch { }
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            _running = true;
            Debug.Log("[PerfProbe] start  " + Steps.Length + " steps x " + Seconds + " s");
            for (var i = 0; i < Steps.Length; i++)
            {
                var undo = Apply(Steps[i]);
                Debug.Log("[PerfProbe] step " + i + " " + Steps[i]);
                yield return new WaitForSecondsRealtime(Seconds);
                undo?.Invoke();
                yield return new WaitForSecondsRealtime(1f);   // a second to settle between steps; not measured
            }
            Debug.Log("[PerfProbe] done");
            _running = false;
        }

        static System.Action Apply(string step)
        {
            switch (step)
            {
                case "lantern lights off":
                {
                    var off = new List<Light>();
                    foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.enabled && l.type != LightType.Directional) { l.enabled = false; off.Add(l); }
                    return () => { foreach (var l in off) if (l != null) l.enabled = true; };
                }
                case "masters hidden":
                {
                    var off = new List<GameObject>();
                    foreach (var g in FindObjectsByType<CompanionGroup>(FindObjectsSortMode.None))
                        foreach (var f in g.Figures.Values)
                            if (f != null && f.gameObject.activeSelf) { f.gameObject.SetActive(false); off.Add(f.gameObject); }
                    return () => { foreach (var o in off) if (o != null) o.SetActive(true); };
                }
                case "lanterns hidden":
                {
                    var off = new List<GameObject>();
                    foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                        if (t.name.StartsWith("Lantern ") && t.gameObject.activeSelf) { t.gameObject.SetActive(false); off.Add(t.gameObject); }
                    return () => { foreach (var o in off) if (o != null) o.SetActive(true); };
                }
                case "ui hidden":
                {
                    var off = new List<Canvas>();
                    foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                        if (c.enabled && c.isRootCanvas) { c.enabled = false; off.Add(c); }
                    var tmps = new List<TMPro.TextMeshPro>();
                    foreach (var t in FindObjectsByType<TMPro.TextMeshPro>(FindObjectsSortMode.None))
                        if (t.enabled) { t.enabled = false; tmps.Add(t); }
                    return () => { foreach (var c in off) if (c != null) c.enabled = true; foreach (var t in tmps) if (t != null) t.enabled = true; };
                }
                case "splat scale 0.8":
                case "splat scale 0.6":
                {
                    var was = GaussianSplatSettings.ResolutionScale;
                    GaussianSplatSettings.ResolutionScale = step.EndsWith("0.8") ? 0.8f : 0.6f;
                    return () => GaussianSplatSettings.ResolutionScale = was;
                }
                case "splats off":
                {
                    var off = new List<GaussianSplatRenderer>();
                    foreach (var r in FindObjectsByType<GaussianSplatRenderer>(FindObjectsSortMode.None))
                        if (r.enabled) { r.enabled = false; off.Add(r); }
                    return () => { foreach (var r in off) if (r != null) r.enabled = true; };
                }
                default: return null;
            }
        }
    }
}
