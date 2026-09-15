using System.Collections;
using GaussianSplatting.Runtime;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>
    /// Shows one Gaussian-splat world at a time, holds it for a few seconds, then moves to the
    /// next — placing the viewer inside each one.
    ///
    /// Deliberately simpler than MuseXR's WorldCycler, which loads through Addressables. That is
    /// the right design there: Skylar's eight worlds are ~1.37 GB and cannot all be resident.
    /// Here there are five World Labs samples at ~23 MB each, 116 MB in total against the 5.9 GB
    /// the emulator reports, so direct references cost nothing and remove a whole failure surface
    /// — no Addressables package, no content build that has to be remembered before every player
    /// build. If Skylar's worlds ever come to this project, Addressables comes back with them.
    /// </summary>
    public class SplatWorldCycler : MonoBehaviour
    {
        [Header("What to show")]
        [Tooltip("Converted GaussianSplatAssets, shown in this order and then looped.")]
        public GaussianSplatAsset[] worlds;

        [Tooltip("Immersion multiplier. The viewer is a fixed size, so the world scales instead.")]
        public float worldScale = 1f;

        [Header("Collision")]
        [Tooltip("These samples ship no collider mesh, so this box IS the floor. Without it the " +
                 "CharacterController has nothing to stand on and gravity drops you out of the world.")]
        public bool buildFallbackBounds = true;

        [Tooltip("How far inside the capture's own bounds the walls stand, metres. The outer " +
                 "shell of a Marble capture degrades into noise, so do not let the visitor reach it.")]
        public float boundsInset = 0.5f;

        [Tooltip("Height of the boundary walls above the floor, metres.")]
        public float wallHeight = 4f;

        [Header("Timing")]
        public float secondsPerWorld = 8f;
        public bool autoAdvance = true;

        [Header("Scene references")]
        public XROrigin xrOrigin;

        [Tooltip("Optional. Needs TMP Essential Resources imported; the cycler logs to the " +
                 "console either way, so leaving this empty only costs the in-world caption.")]
        public TMP_Text label;

        /// <summary>
        /// GaussianSplatRenderer gates ALL drawing on these five being non-null (its
        /// `resourcesAreSetUp`), and a renderer created with AddComponent at runtime gets none of
        /// them — it logs "Resource references are missing" once and then silently draws nothing.
        /// They are serialized here rather than found by name so the shaders are also pulled into
        /// the player build; Shader.Find would return null in a build after stripping.
        /// </summary>
        [Header("Splat renderer resources (required — from the gaussian-splatting package)")]
        public Shader shaderSplats;
        public Shader shaderComposite;
        public Shader shaderDebugPoints;
        public Shader shaderDebugBoxes;
        public ComputeShader csSplatUtilities;

        int _index = -1;
        GameObject _current;
        GameObject _bounds;
        Camera _camera;

        public int CurrentIndex => _index;

        void Start()
        {
            if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
            _camera = xrOrigin != null ? xrOrigin.Camera : Camera.main;

            if (worlds == null || worlds.Length == 0)
            {
                Debug.LogError("[SplatWorldCycler] no worlds assigned — nothing to show.");
                return;
            }

            if (shaderSplats == null || shaderComposite == null || shaderDebugPoints == null ||
                shaderDebugBoxes == null || csSplatUtilities == null)
            {
                Debug.LogError("[SplatWorldCycler] splat renderer resources are not assigned — " +
                               "worlds will load and report their splat count but render NOTHING. " +
                               "Assign the four shaders and SplatUtilities.compute from " +
                               "Packages/org.nesnausk.gaussian-splatting/Shaders on this component.");
                return;
            }

            LogRenderCapability();
            StartCoroutine(Run());
        }

        /// <summary>
        /// One-shot capability dump. The splat renderer fails SILENTLY when the platform cannot
        /// run its compute kernels — the worlds still load and report their splat count, the GPU
        /// buffers are simply never created, and the only symptom is a black screen plus a Vulkan
        /// warning about unbound compute buffers. SplatUtilities.compute declares
        /// `#pragma require wavebasic waveballot` for its radix sort, so the kernels are the
        /// thing to name explicitly rather than infer.
        /// </summary>
        void LogRenderCapability()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[SplatWorldCycler] --- render capability ---");
            sb.AppendLine("  graphicsDeviceType   = " + SystemInfo.graphicsDeviceType);
            sb.AppendLine("  graphicsDeviceName   = " + SystemInfo.graphicsDeviceName);
            sb.AppendLine("  graphicsDeviceVersion= " + SystemInfo.graphicsDeviceVersion);
            sb.AppendLine("  supportsComputeShaders = " + SystemInfo.supportsComputeShaders);
            sb.AppendLine("  csSplatUtilities     = " + (csSplatUtilities == null ? "NULL" : csSplatUtilities.name));

            if (csSplatUtilities != null)
            {
                // The four radix-sort kernels are the ones that need wave intrinsics; the others
                // do not, so listing both tells us whether it is a wave problem or a total one.
                string[] kernels = {
                    "CSSetIndices", "CSCalcDistances", "CSCalcViewData",
                    "InitDeviceRadixSort", "Upsweep", "Scan", "Downsweep",
                };
                foreach (var k in kernels)
                    sb.AppendLine("  kernel " + k + " = " + (csSplatUtilities.HasKernel(k) ? "present" : "*** MISSING ***"));
            }

            Debug.Log(sb.ToString());
        }

        IEnumerator Run()
        {
            for (;;)
            {
                Show();
                if (!autoAdvance) yield break;
                yield return new WaitForSeconds(secondsPerWorld);
            }
        }

        void Show()
        {
            _index = (_index + 1) % worlds.Length;
            var asset = worlds[_index];
            Unload();

            if (asset == null)
            {
                Debug.LogError($"[SplatWorldCycler] slot {_index} is empty.");
                return;
            }

            _current = new GameObject($"World_{asset.name}");
            _current.transform.localScale = Vector3.one * worldScale;

            var renderer = _current.AddComponent<GaussianSplatRenderer>();
            renderer.m_Asset = asset;
            renderer.m_ShaderSplats = shaderSplats;
            renderer.m_ShaderComposite = shaderComposite;
            renderer.m_ShaderDebugPoints = shaderDebugPoints;
            renderer.m_ShaderDebugBoxes = shaderDebugBoxes;
            renderer.m_CSSplatUtilities = csSplatUtilities;
            // Resources are built in OnEnable, which ran before the asset and shaders were
            // assigned — without this toggle the renderer draws nothing at all, silently.
            renderer.enabled = false;
            renderer.enabled = true;

            Place(asset);

            // HasValidRenderSetup is false when the GPU buffers were never created — the exact
            // failure that renders black while everything else looks healthy.
            Debug.Log($"[SplatWorldCycler] renderer: validAsset={renderer.HasValidAsset} " +
                      $"validRenderSetup={renderer.HasValidRenderSetup} enabled={renderer.enabled} " +
                      $"bounds={asset.boundsMin.ToString("F2")}..{asset.boundsMax.ToString("F2")} " +
                      $"origin={(xrOrigin != null ? xrOrigin.transform.position.ToString("F2") : "n/a")} " +
                      $"eye={(_camera != null ? _camera.transform.position.ToString("F2") : "n/a")}");

            var caption = $"{_index + 1}/{worlds.Length}  {asset.name}\n{asset.splatCount:N0} splats";
            if (label != null) label.text = caption;
            // Logged unconditionally: this is what confirms which world is on screen when the
            // only view of the device is `adb exec-out screencap`.
            Debug.Log("[SplatWorldCycler] showing " + caption.Replace('\n', ' '));
        }

        /// <summary>In an XR rig the headset owns the camera pose, so the ORIGIN moves.</summary>
        void Place(GaussianSplatAsset asset)
        {
            // How high the tracking system is holding the camera above the rig right now. In
            // Floor mode this is the real standing eye height, and it must come out of the
            // origin's Y or it gets added twice.
            float headHeight = 0f;
            if (xrOrigin != null && _camera != null)
                headHeight = _camera.transform.position.y - xrOrigin.transform.position.y;

            SplatPlacement.FromBounds(asset.boundsMin, asset.boundsMax, worldScale, headHeight,
                                      out var pos, out var rot);

            // These samples ship no collider mesh — the CDN export is splats only — so without a
            // synthetic floor the CharacterController has nothing to stand on and gravity drops
            // the visitor out of the world on the first frame. The origin's Y IS the floor under
            // Floor tracking, so the box top goes exactly there.
            if (buildFallbackBounds)
            {
                _bounds = PhysicsBounds.BuildFromSplatBounds(
                    "World Bounds (physics only)", asset.boundsMin, asset.boundsMax,
                    worldScale, pos.y, boundsInset, wallHeight);
            }

            // A CharacterController caches its own position and ignores the transform being
            // written underneath it until it is toggled.
            var character = xrOrigin != null ? xrOrigin.GetComponent<CharacterController>() : null;
            bool had = character != null && character.enabled;
            if (had) character.enabled = false;

            if (xrOrigin != null) xrOrigin.transform.SetPositionAndRotation(pos, rot);
            else if (_camera != null) _camera.transform.SetPositionAndRotation(pos, rot);

            if (had) character.enabled = true;
            Physics.SyncTransforms();

            if (_camera != null)
            {
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane =
                    SplatPlacement.FarClipFor(asset.boundsMin, asset.boundsMax, worldScale);
                // Marble bakes final radiance into the splats — a lit sky washes them out.
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.black;
            }
        }

        void Unload()
        {
            if (_bounds != null)
            {
                if (Application.isPlaying) Destroy(_bounds); else DestroyImmediate(_bounds);
                _bounds = null;
            }
            if (_current == null) return;
            if (Application.isPlaying) Destroy(_current); else DestroyImmediate(_current);
            _current = null;
        }

        void OnDestroy() => Unload();
    }
}
