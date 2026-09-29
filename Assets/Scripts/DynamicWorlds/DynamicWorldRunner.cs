using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using MusePico.Generation;
using MuseXR.Worlds;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MuseXR.DynamicWorlds
{
    /// <summary>
    /// The on-headset test of dynamic world generation (Assets/Scenes/Tests/DynamicWorld.unity).
    ///
    /// When a menu plate is pressed it runs the whole chain: the DepthRoom depth panorama shipped in
    /// StreamingAssets plus that plate's prompt → World Labs
    /// paints a panorama (shown as the sky while the world builds) → World Labs builds the world →
    /// the 500k .spz is downloaded, converted ON THE HEADSET, and placed on the DepthRoom frame.
    /// Every step is timed; grep logcat for [DynamicWorld].
    ///
    /// The prompts and model are constants, not Inspector fields: a serialized field's scene
    /// copy silently beats the code (the MuseumDialogue.dialogueModel trap).
    /// </summary>
    public sealed class DynamicWorldRunner : MonoBehaviour
    {
        // Measured: standard 5-8 min and 1,500 credits; Draft ~20 s and 150 credits, same shape, ~2x blurrier.
        const string ModelStandard = "marble-1.1";
        const string ModelDraft = "marble-1.0-draft";
        bool _draft;
        string Model => _draft ? ModelDraft : ModelStandard;

        // Every prompt describes SURFACES on the DepthRoom's fixed shape (the geometry beats the
        // words), asks for a matte floor (a mirror floor grows a ghost room) and no lettering.
        const string Tail = " Matte, unpolished floor with no reflections and no shine. Soft, even light. " +
                            "No people, no text or lettering, nothing standing in the middle of the room.";

        /// <summary>Index 0 is the first test's prompt (kept for reference); 1-3 are the menu plates, in order.</summary>
        static readonly (string name, string prompt)[] Presets =
        {
            ("Rainbows and clouds",
             "A calm, airy gallery hall above the clouds. Four slender columns stand along each long side wall, " +
             "painted with soft pastel rainbows arcing over billowing white clouds. Between the columns the walls " +
             "are painted with dreamy sunlit cloudscapes in pale blue, peach and lavender. The wall straight ahead " +
             "is a huge painted mural of a double rainbow over a sea of clouds. The wall behind has a plain white " +
             "arched doorway. Pale stone floor. High coffered ceiling painted sky blue with small white clouds." + Tail),
            ("Underwater coral palace",
             "A deep-sea palace hall at the bottom of the ocean. Four columns along each long side wall are made of " +
             "living pink and orange coral. Between them the walls are covered in blue-green seaweed, shells and " +
             "starfish. The wall straight ahead is a vast mural of a whale swimming through shafts of blue light. " +
             "The wall behind has a round barnacled porthole doorway. Pale sand floor with ripples. The high ceiling " +
             "glows with hanging jellyfish lanterns in soft cyan. Everything bathed in deep turquoise underwater light." + Tail),
            ("Volcanic forge hall",
             "A dwarven forge hall carved inside a volcano. Four massive black iron columns along each long side wall, " +
             "banded with glowing orange rivets. Between them the walls are rough black basalt split by glowing seams " +
             "of molten lava. The wall straight ahead is a huge carved stone relief of hammers and anvils lit red from " +
             "below. The wall behind has a heavy iron gate. Dark cracked stone floor. The high ceiling is soot-black " +
             "rock with hanging chains and ember light." + Tail),
            ("Ice cathedral",
             "A frozen cathedral hall of ice in the far north. Four tall translucent ice columns along each long side " +
             "wall, frosted white and pale blue. Between them the walls are carved snow and blue glacier ice with " +
             "frost patterns. The wall straight ahead is a great window of clear ice showing a green and violet " +
             "aurora. The wall behind has a snow-covered arched doorway. Fresh white snow floor. The high vaulted " +
             "ceiling shimmers with the colours of the northern lights." + Tail),
        };
        const string DepthPanoFile = "DynamicWorlds/depthroom-depth.png";
        const string KeyFile = "worldlabs.key";
        const float PanoPollSeconds = 2f, WorldPollSeconds = 5f;
#if UNITY_EDITOR
        const bool EditorStopsAfterPanorama = true;
#endif

        [Header("Scene")]
        [SerializeField] TMP_Text status;
        [Tooltip("Optional. Plates for presets 1, 2, 3 in order. With none, preset 0 starts on its own after a delay.")]
        [SerializeField] XRSimpleInteractable[] menuChoices;
        [SerializeField] GameObject menuRoot;
        [Tooltip("Optional. Toggles Standard / Draft before a world is chosen.")]
        [SerializeField] XRSimpleInteractable qualityToggle;
        [SerializeField] TMP_Text qualityLabel;
        [Tooltip("The grey cube room: shown until the painted panorama arrives.")]
        [SerializeField] GameObject placeholder;
        [Tooltip("Skybox/Panoramic material the painted panorama is shown on while the world builds.")]
        [SerializeField] Material panoSky;
        [SerializeField] Camera viewCamera;

        [Header("Splat renderer resources (as WorldCycler)")]
        [SerializeField] Shader shaderSplats;
        [SerializeField] Shader shaderComposite;
        [SerializeField] Shader shaderDebugPoints;
        [SerializeField] Shader shaderDebugBoxes;
        [SerializeField] ComputeShader csSplatUtilities;

        readonly List<string> _lines = new List<string>();
        StepClock _clock;

        static double Now => Time.realtimeSinceStartupAsDouble;

        bool _started;

        void Start()
        {
            _clock = new StepClock(0);   // realtimeSinceStartup counts from launch
            // Nothing starts on its own: every generation is a paid world, so it waits for a click.
            if (menuChoices == null || menuChoices.Length == 0)
            {
                SetStatusHeader("No menu in this scene");
                Log(_clock.Note("no menu plates assigned; nothing will start", Now));
                return;
            }
            for (int i = 0; i < menuChoices.Length; i++)
            {
                int preset = i + 1;
                menuChoices[i].selectEntered.AddListener(_ => Go(preset, 0f));
            }
            if (qualityToggle != null) qualityToggle.selectEntered.AddListener(_ => ToggleQuality());
            ShowQuality();
            SetStatusHeader("Point at a world and pull the trigger");
            Log(_clock.Note($"scene loaded; waiting for a choice. model={Model}", Now));
        }

        void ToggleQuality()
        {
            if (_started) return;
            _draft = !_draft;
            ShowQuality();
            Log(_clock.Note("quality: " + (_draft ? "DRAFT" : "STANDARD") + " (" + Model + ")", Now));
        }

        void ShowQuality()
        {
            if (qualityLabel == null) return;
            qualityLabel.text = _draft
                ? "<b>QUALITY: DRAFT</b>\n<size=60%>fast (~20 s), blurrier, $0.18 - tap to switch</size>"
                : "<b>QUALITY: STANDARD</b>\n<size=60%>sharp, ~6 min, $1.26 - tap to switch</size>";
        }

        async void Go(int preset, float delay)
        {
            if (_started) return;   // one generation per launch: each is a paid world
            _started = true;
            if (menuRoot != null) menuRoot.SetActive(false);
            Log(_clock.Note($"chosen: {Presets[preset].name}, model {Model}", Now));
            try { await Run(Presets[preset].prompt, delay); }
            catch (Exception e)
            {
                if (_clock.Current != null) Log(_clock.End(Now, "FAILED"));
                Log($"{StepClock.Tag} ERROR {e.Message}");
                Debug.LogException(e);
                Log(_clock.Summary(Now));
            }
        }

        async Task Run(string prompt, float delay)
        {
            double startAt = Now + delay;
            while (Now < startAt)
            {
                SetStatusHeader($"Starting in {Mathf.CeilToInt((float)(startAt - Now))} s");
                await Task.Yield();
            }
            SetStatusHeader("Generating");

            Log(_clock.Begin("1 key + depth pano", Now));
            string key = await new FallbackKeySource(new EnvironmentKeySource("WORLDLABS_API_KEY"),
                                                     new StreamingAssetsKeySource(KeyFile)).GetKeyAsync();
#if UNITY_EDITOR && UNITY_EDITOR_WIN
            // An Editor started before the key was set cannot see it in its own environment.
            if (string.IsNullOrEmpty(key))
                key = Environment.GetEnvironmentVariable("WORLDLABS_API_KEY", EnvironmentVariableTarget.User);
#endif
            if (string.IsNullOrEmpty(key)) throw new Exception("no World Labs key (StreamingAssets/" + KeyFile + " missing?)");
            byte[] depthPng = await ReadStreaming(DepthPanoFile);
            var zRange = JsonUtility.FromJson<ZRange>(System.Text.Encoding.UTF8.GetString(
                await ReadStreaming(Path.ChangeExtension(DepthPanoFile, ".json"))));
            Log(_clock.End(Now, $"key {key.Length} chars, depth {depthPng.Length / 1024} KB, z {zRange.z_min:0.00}..{zRange.z_max:0.00}"));

            var api = new MarbleWebClient(key);

            // --- 2. painted panorama
            Log(_clock.Begin("2 paint panorama (submit)", Now));
            var op = MarbleWire.ParseOperation(await api.PostJsonAsync("/pano:depth_to_rgb",
                MarbleWire.DepthToRgbBody(Convert.ToBase64String(depthPng), zRange.z_min, zRange.z_max, prompt)));
            Log(_clock.End(Now, "operation " + op.operation_id));

            Log(_clock.Begin("3 paint panorama (wait)", Now));
            string opJson = await PollUntilDone(api, op.operation_id, PanoPollSeconds);
            string panoUrl = MarbleWire.PanoUrl(opJson) ?? throw new Exception("panorama finished with no URL");
            Log(_clock.End(Now, CostOf(opJson)));

            Log(_clock.Begin("4 download panorama", Now));
            byte[] panoPng = await api.DownloadAsync(panoUrl);
            Log(_clock.End(Now, $"{panoPng.Length / 1024} KB"));
            ShowPanoramaAsSky(panoPng);

#if UNITY_EDITOR
            // A Play Mode check of everything up to the painted panorama costs 80 credits; the world
            // after it costs 1,500 more. Flip this to run the whole chain in the Editor.
            if (EditorStopsAfterPanorama)
            {
                SetStatusHeader("Editor: stopped after the panorama");
                Log(_clock.Note("Editor: stopping after the panorama (EditorStopsAfterPanorama)", Now));
                Log(_clock.Summary(Now));
                return;
            }
#endif

            // --- 3. world
            Log(_clock.Begin("5 build world (submit)", Now));
            var worldOp = MarbleWire.ParseOperation(await api.PostJsonAsync("/worlds:generate",
                MarbleWire.PanoWorldBody(Convert.ToBase64String(panoPng), Model, "MuseXR dynamic " + DateTime.Now.ToString("MMdd-HHmm"))));
            Log(_clock.End(Now, "operation " + worldOp.operation_id));

            Log(_clock.Begin("6 build world (wait)", Now));
            string worldOpJson = await PollUntilDone(api, worldOp.operation_id, WorldPollSeconds);
            string worldId = MarbleWire.WorldId(MarbleWire.ParseOperation(worldOpJson)) ?? throw new Exception("world finished with no id");
            Log(_clock.End(Now, CostOf(worldOpJson) + " world " + worldId));

            Log(_clock.Begin("7 fetch world record", Now));
            string worldJson = await api.GetJsonAsync("/worlds/" + worldId);
            string spzUrl = MarbleWire.SpzUrl(worldJson, "500k") ?? throw new Exception("world has no 500k splats");
            var semantics = MarbleWire.ParseSemantics(worldJson);
            Log(_clock.End(Now, semantics == null ? "no metric data" : $"metric {semantics.metric_scale_factor:0.0000} ground {semantics.ground_plane_offset:0.0000}"));

            Log(_clock.Begin("8 download splats", Now));
            byte[] spz = await api.DownloadAsync(spzUrl);
            Log(_clock.End(Now, $"{spz.Length / 1048576f:0.0} MB"));

            // --- 4. on the headset
            Log(_clock.Begin("9 decode splats", Now));
            await Task.Yield();   // let the panel draw before the main thread is busy
            NativeArray<RuntimeSplatAssetBuilder.Splat> splats = RuntimeSplatAssetBuilder.DecodeSpz(spz);
            float lift;
            string liftNote;
            try
            {
                var rawY = new float[splats.Length];
                for (int i = 0; i < rawY.Length; i++) rawY[i] = splats[i].pos.y;
                float floorRaw = DynamicWorldPlacement.FloorRawY(rawY);
                float fromFloor = DynamicWorldPlacement.LiftForFloor(floorRaw);
                lift = semantics != null ? DynamicWorldPlacement.LiftFromSemantics(semantics.ground_plane_offset, semantics.metric_scale_factor) : fromFloor;
                liftNote = $"{splats.Length:N0} splats; lift {lift:0.000} m ({(semantics != null ? "Marble ground" : "floor band")}; floor band gives {fromFloor:0.000})";
                Log(_clock.End(Now, liftNote));

                Log(_clock.Begin("10 convert to asset", Now));
                await Task.Yield();
                var asset = RuntimeSplatAssetBuilder.Build(splats, "dynamic-" + worldId);
                Log(_clock.End(Now, $"bounds {asset.boundsMin} .. {asset.boundsMax}"));

                Log(_clock.Begin("11 first frame", Now));
                ShowWorld(asset, lift);
                await Task.Yield();
                await Task.Yield();
                Log(_clock.End(Now));
            }
            finally { splats.Dispose(); }

            SetStatusHeader("World ready");
            Log(_clock.Summary(Now));
        }

        async Task<string> PollUntilDone(MarbleWebClient api, string operationId, float everySeconds)
        {
            string last = null;
            double lastNote = Now;
            for (;;)
            {
                await Delay(everySeconds);
                string json = await api.GetJsonAsync("/operations/" + operationId);
                var op = MarbleWire.ParseOperation(json);
                string state = op?.metadata?.progress?.status ?? "PENDING";
                if (state != last || Now - lastNote > 30)
                {
                    Log(_clock.Note($"  {_clock.Current}: {state}", Now));
                    last = state;
                    lastNote = Now;
                }
                if (op != null && op.done)
                {
                    if (op.error != null && !string.IsNullOrEmpty(op.error.message))
                        throw new Exception($"operation failed: {op.error.code} {op.error.message}");
                    return json;
                }
            }
        }

        static async Task Delay(float seconds)
        {
            double until = Now + seconds;
            while (Now < until) await Task.Yield();
        }

        static string CostOf(string opJson)
        {
            var op = MarbleWire.ParseOperation(opJson);
            return op?.cost != null && op.cost.total_credits > 0 ? $"{op.cost.total_credits:0} credits" : "";
        }

        static async Task<byte[]> ReadStreaming(string relative)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relative);
            if (!path.Contains("://")) return File.ReadAllBytes(path);
            using var req = UnityWebRequest.Get(path);
            var done = new TaskCompletionSource<bool>();
            req.SendWebRequest().completed += _ => done.TrySetResult(true);
            await done.Task;
            if (req.result != UnityWebRequest.Result.Success) throw new Exception($"StreamingAssets/{relative}: {req.error}");
            return req.downloadHandler.data;
        }

        void ShowPanoramaAsSky(byte[] png)
        {
            if (panoSky == null) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(png)) { Log(_clock.Note("panorama did not decode as an image", Now)); return; }
            tex.wrapModeU = TextureWrapMode.Repeat;
            panoSky.SetTexture("_MainTex", tex);
            RenderSettings.skybox = panoSky;
            if (viewCamera != null) viewCamera.clearFlags = CameraClearFlags.Skybox;
            HidePlaceholder();
            Log(_clock.Note($"showing the painted panorama as the sky ({tex.width}x{tex.height})", Now));
        }

        void ShowWorld(GaussianSplatAsset asset, float lift)
        {
            var go = new GameObject("World_dynamic");
            DynamicWorldPlacement.Apply(go.transform, lift);
            var r = go.AddComponent<GaussianSplatRenderer>();
            r.m_Asset = asset;
            r.m_ShaderSplats = shaderSplats;
            r.m_ShaderComposite = shaderComposite;
            r.m_ShaderDebugPoints = shaderDebugPoints;
            r.m_ShaderDebugBoxes = shaderDebugBoxes;
            r.m_CSSplatUtilities = csSplatUtilities;
            SplatRenderTuning.Apply(r);
            // Resources are built in OnEnable, before the asset was assigned (see WorldCycler).
            r.enabled = false;
            r.enabled = true;
            HidePlaceholder();
        }

        /// <summary>
        /// Hides the grey cubes but keeps their colliders: they are the room the world is painted
        /// onto, so they stop a walking visitor at the generated walls instead of letting them
        /// wander out through the splats.
        /// </summary>
        void HidePlaceholder()
        {
            if (placeholder == null) return;
            foreach (var r in placeholder.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        void Log(string line)
        {
            Debug.Log(line);
            foreach (var l in line.Split('\n'))
            {
                _lines.Add(l.Replace(StepClock.Tag + " ", ""));
                if (_lines.Count > 16) _lines.RemoveAt(0);
            }
            if (status != null) status.text = _header + "\n" + string.Join("\n", _lines);
        }

        string _header = "<b>Dynamic world test</b>";
        string _headerRaw;
        void SetStatusHeader(string header)
        {
            if (header == _headerRaw) return;
            _headerRaw = header;
            _header = "<b>" + header + "</b>";
            if (status != null) status.text = _header + "\n" + string.Join("\n", _lines);
        }

        [Serializable] class ZRange { public float z_min; public float z_max; }
    }
}
