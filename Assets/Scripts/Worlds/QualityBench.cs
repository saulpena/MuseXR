using System.Collections;
using System.Collections.Generic;
using System.IO;
using GaussianSplatting.Runtime;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;

namespace MuseXR.Worlds
{
    /// <summary>
    /// The quality benchmark: shows one version of one environment at a time, labelled, with the
    /// frame rate beside it, so versions can be compared by eye and by number in the same pose.
    ///
    ///   right A / N        next version          right B / P   previous version
    ///   left Y / E         next environment      left X / F9   splat layer resolution (SplatScaleCycler)
    ///   right stick click / H   hide the panel, for clean screenshots
    ///   left stick click / B    hide or show the Buddha, where the environment has one
    ///
    /// Every second it logs one [Bench] line (label, splats, splat resolution, FPS, CPU ms), so a
    /// run can be read back from logcat without anyone reading the panel. Loading goes through the
    /// ordinary WorldCycler, so each version is placed exactly as the app would place it.
    /// </summary>
    public sealed class QualityBench : MonoBehaviour
    {
        public WorldCycler cycler;
        public XROrigin xrOrigin;

        [Tooltip("Head-locked panel. Built at runtime if left empty.")]
        public TextMeshPro panel;

        [Tooltip("The chapter's Buddha model (Assets/Props/Peach/buddha-statue.gltf).")]
        public GameObject buddhaModel;

        [Tooltip("Uniform scale the chapter gives the model: 12.239 makes it 12 m tall (Museum.unity).")]
        public float buddhaScale = 12.239f;

        readonly List<BenchVariant> _available = new List<BenchVariant>();
        readonly List<string> _missing = new List<string>();
        int _index;
        bool _loading;
        int _splats;
        bool _rendering;
        float _fps, _cpuMs;
        int _frames;
        float _windowStart, _cpuSum;
        InputAction _next, _prev, _env, _hide, _buddhaToggle;
        GameObject _buddha;
        bool _buddhaHidden;
        string _csvPath;

        IEnumerator Start()
        {
            if (xrOrigin == null) xrOrigin = FindObjectOfType<XROrigin>();
            if (cycler == null) cycler = FindObjectOfType<WorldCycler>();
            cycler.drivenExternally = true;
            if (panel == null) panel = BuildPanel();
            OpenCsv();

            // A version whose asset has not been converted yet is skipped and named, not an error:
            // the catalog lists what is planned before it is built.
            foreach (var v in QualityBenchCatalog.All)
            {
                var locations = Addressables.LoadResourceLocationsAsync(v.key);
                yield return locations;
                bool found = locations.Result != null && locations.Result.Count > 0;
                Addressables.Release(locations);
                if (found) _available.Add(v);
                else _missing.Add(v.referenceOnly ? $"{v.Label} (Editor reference only: cannot render on the headset)"
                                                  : $"{v.Label} (not converted yet)");
            }
            foreach (var m in _missing) Debug.Log($"[Bench] not in this build, skipped: {m}");
            if (_available.Count == 0)
            {
                SetPanel("QUALITY BENCH\nno benchmark worlds are built into this player");
                yield break;
            }

            var wanted = LaunchOptions.BenchVariant;
            _index = 0;
            for (int i = 0; i < _available.Count; i++) if (_available[i].key == wanted) _index = i;
            yield return Show(_index);
        }

        void OnEnable()
        {
            _next = Button("bench-next", "<XRController>{RightHand}/primaryButton", "<Keyboard>/n");
            _prev = Button("bench-prev", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/p");
            _env = Button("bench-env", "<XRController>{LeftHand}/secondaryButton", "<Keyboard>/e");
            _hide = Button("bench-hide", "<XRController>{RightHand}/primary2DAxisClick", "<Keyboard>/h");
            _buddhaToggle = Button("bench-buddha", "<XRController>{LeftHand}/primary2DAxisClick", "<Keyboard>/b");
        }

        void OnDisable()
        {
            foreach (var a in new[] { _next, _prev, _env, _hide, _buddhaToggle }) { a?.Disable(); a?.Dispose(); }
        }

        static InputAction Button(string name, params string[] bindings)
        {
            var a = new InputAction(name, InputActionType.Button);
            foreach (var b in bindings) a.AddBinding(b);
            a.Enable();
            return a;
        }

        void Update()
        {
            Measure();
            if (_available.Count == 0 || _loading) return;

            if (_next.WasPressedThisFrame()) StartCoroutine(Show((_index + 1) % _available.Count));
            else if (_prev.WasPressedThisFrame()) StartCoroutine(Show((_index - 1 + _available.Count) % _available.Count));
            else if (_env.WasPressedThisFrame()) StartCoroutine(Show(QualityBenchCatalog.NextEnvironmentStart(_available, _index)));
            if (_hide.WasPressedThisFrame() && panel != null) panel.gameObject.SetActive(!panel.gameObject.activeSelf);
            if (_buddhaToggle.WasPressedThisFrame())
            {
                _buddhaHidden = !_buddhaHidden;
                PlaceBuddha(_available[_index]);
                Debug.Log($"[Bench] Buddha {(_buddhaHidden ? "hidden" : "shown")}");
            }
        }

        /// <summary>Stands the chapter's Buddha where this version puts it, or removes it.</summary>
        void PlaceBuddha(BenchVariant v)
        {
            bool want = v.buddhaAt.HasValue && !_buddhaHidden && buddhaModel != null;
            if (!want) { if (_buddha != null) _buddha.SetActive(false); return; }
            if (_buddha == null)
            {
                _buddha = Instantiate(buddhaModel);
                _buddha.name = "Bench Buddha";
                _buddha.transform.localScale = Vector3.one * buddhaScale;
            }
            _buddha.transform.SetPositionAndRotation(v.buddhaAt.Value, Quaternion.identity);
            _buddha.SetActive(true);
        }

        IEnumerator Show(int index)
        {
            _loading = true;
            _index = index;
            var v = _available[index];
            SetPanel($"{v.Label}\nloading…");
            yield return cycler.ShowWorld(v.world);
            PlaceBuddha(v);
            var renderer = FindObjectOfType<GaussianSplatRenderer>();
            _splats = renderer != null && renderer.m_Asset != null ? renderer.m_Asset.splatCount : 0;
            // A renderer whose GPU buffers could not be created draws NOTHING and costs nothing, so
            // the frame rate beside it reads perfect. On a Quest 3S a 4.32M asset did exactly that
            // (a 138 MB buffer against a 128 MB limit) and showed 72 FPS of an empty scene.
            _rendering = renderer != null && renderer.HasValidAsset && renderer.HasValidRenderSetup;
            Debug.Log($"[Bench] showing {v.Label} ({v.key}): {_splats:N0} splats, " +
                      $"loaded in {cycler.LastLoadSeconds:F1}s" + (_rendering ? "" : " — NOT RENDERING") +
                      $" — {v.provenance}");
            _loading = false;
            RestartWindow();
        }

        void Measure()
        {
            _frames++;
            _cpuSum += Time.unscaledDeltaTime;
            float elapsed = Time.realtimeSinceStartup - _windowStart;
            if (elapsed < 1f) return;

            _fps = _frames / elapsed;
            _cpuMs = 1000f * _cpuSum / _frames;
            if (!_loading && _available.Count > 0)
            {
                var v = _available[_index];
                Debug.Log($"[Bench] {v.Label} | {_splats} splats | splat res {GaussianSplatSettings.ResolutionScale:0.00} | " +
                          $"{_fps:0.0} FPS | frame {_cpuMs:0.0} ms" + (_rendering ? "" : " | NOT RENDERING"));
                AppendCsv(v);
                RefreshPanel();
            }
            RestartWindow();
        }

        /// <summary>
        /// Every one-second reading also goes to a CSV on the device. The Quest keeps only 256 KB of
        /// log by default, and a whole headset session of [Bench] lines was overwritten within
        /// minutes (28 Sep 2026); a file cannot be. One file per session, pulled with
        /// adb pull /sdcard/Android/data/com.musexr.impossiblemuseum/files/bench
        /// </summary>
        void OpenCsv()
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "bench");
                Directory.CreateDirectory(dir);
                _csvPath = Path.Combine(dir, $"bench-{System.DateTime.Now:yyyyMMdd-HHmmss}.csv");
                File.WriteAllText(_csvPath, "time,environment,version,key,splats,splat_res,fps,frame_ms,rendering,buddha\n");
                Debug.Log($"[Bench] writing readings to {_csvPath}");
            }
            catch (System.Exception e) { _csvPath = null; Debug.LogWarning($"[Bench] no CSV: {e.Message}"); }
        }

        void AppendCsv(BenchVariant v)
        {
            if (_csvPath == null) return;
            string buddha = !v.buddhaAt.HasValue ? "none" : _buddhaHidden ? "hidden" : "shown";
            var line = string.Join(",", System.DateTime.Now.ToString("HH:mm:ss"), Csv(v.environment), Csv(v.version), v.key,
                                   _splats, GaussianSplatSettings.ResolutionScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                                   _fps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                                   _cpuMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                                   _rendering ? "yes" : "no", buddha);
            try { File.AppendAllText(_csvPath, line + "\n"); } catch { }
        }

        static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

        void RestartWindow() { _frames = 0; _cpuSum = 0f; _windowStart = Time.realtimeSinceStartup; }

        void RefreshPanel()
        {
            var v = _available[_index];
            SetPanel($"<size=140%><b>{v.environment}</b></size>   {QualityBenchCatalog.PositionInEnvironment(_available, _index)}\n" +
                     $"<size=125%>{v.version}</size>\n" +
                     $"{_splats:N0} splats · splat res {GaussianSplatSettings.ResolutionScale:0.0}\n" +
                     (_rendering ? $"<b>{_fps:0} FPS</b> · {_cpuMs:0.0} ms/frame\n"
                                 : "<color=#ff5050><b>NOT RENDERING</b>: the headset could not create its GPU buffers</color>\n") +
                     $"<size=70%>{v.provenance}\nA/B version · Y environment · X splat res · R-stick panel" +
                     (v.buddhaAt.HasValue ? $" · L-stick Buddha ({(_buddhaHidden ? "off" : "on")})" : "") + "</size>");
        }

        void SetPanel(string text) { if (panel != null) panel.text = text; }

        /// <summary>
        /// Below the line of sight so it never sits over what is being judged. Identity rotation as a
        /// camera child: a world-space TMP reads correctly when its +Z points away from the viewer.
        /// </summary>
        TextMeshPro BuildPanel()
        {
            var cam = xrOrigin != null ? xrOrigin.Camera : Camera.main;
            var go = new GameObject("Bench Panel");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.42f, 1.1f);
            go.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = new Vector2(1.2f, 0.25f);
            t.fontSize = 0.3f;
            t.enableWordWrapping = false;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.outlineWidth = 0.2f;
            t.outlineColor = Color.black;
            return t;
        }
    }
}
