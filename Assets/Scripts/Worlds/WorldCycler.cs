using System.Collections;
using System.Collections.Generic;
using GaussianSplatting.Runtime;
using Unity.XR.CoreUtils;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MuseXR.Worlds
{
    /// <summary>
    /// Loads one Marble world at a time, holds it for a few seconds, then swaps to the next —
    /// placing the player at that world's measured spawn and facing.
    ///
    /// The point is to see all eight in one sitting and judge whether the ported spawn data
    /// actually puts you somewhere sensible, rather than opening eight scenes by hand.
    ///
    /// LOADING, and its limitation: worlds are loaded by asset path through AssetDatabase, which
    /// is EDITOR ONLY. In Play Mode this works and loads on demand. In a built player it does
    /// not, and a build would need Addressables (or Resources, which would force all ~1.4 GB of
    /// converted splats into the APK). Addressables is the right answer and is deliberately not
    /// done yet — this scene exists to verify the spawn data first.
    /// </summary>
    public class WorldCycler : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds to stay in each world before swapping.")]
        public float secondsPerWorld = 15f;

        [Tooltip("Start cycling immediately. Off means it shows the first world and waits.")]
        public bool autoAdvance = true;

        [Header("Scene references")]
        [Tooltip("The XR Origin to reposition. Found automatically if left empty.")]
        public XROrigin xrOrigin;

        [Tooltip("Optional label showing the current world.")]
        public TextMesh label;

        int _index = -1;
        GameObject _current;
        Camera _camera;

        public WorldDefinition Current =>
            _index >= 0 && _index < WorldCatalog.All.Count ? WorldCatalog.All[_index] : null;

        void Start()
        {
            if (xrOrigin == null) xrOrigin = FindObjectOfType<XROrigin>();
            if (xrOrigin != null) _camera = xrOrigin.Camera;
            if (_camera == null) _camera = Camera.main;

            Next();
            if (autoAdvance) StartCoroutine(CycleForever());
        }

        IEnumerator CycleForever()
        {
            while (true)
            {
                yield return new WaitForSeconds(secondsPerWorld);
                Next();
            }
        }

        /// <summary>Unload the current world and show the next one.</summary>
        public void Next()
        {
            _index = (_index + 1) % WorldCatalog.All.Count;
            Show(WorldCatalog.All[_index]);
        }

        public void Show(WorldDefinition world)
        {
            // Keep the index in step even when called directly rather than through Next(),
            // otherwise the on-screen label reports the wrong world.
            int found = WorldCatalog.IndexOf(world);
            if (found >= 0) _index = found;

            Unload();

            var asset = LoadSplatAsset(world.AssetPath);
            if (asset == null)
            {
                Debug.LogError($"[WorldCycler] could not load {world.AssetPath}. " +
                               "Converted splat assets are gitignored — reconvert from the .spz.");
                return;
            }

            _current = new GameObject($"World_{world.key}");
            _current.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _current.transform.localScale = world.SplatScale;

            var renderer = _current.AddComponent<GaussianSplatRenderer>();
            renderer.m_Asset = asset;

            // The renderer builds its GPU resources in OnEnable, which has already run by the
            // time the asset is assigned — so it must be re-enabled or it draws nothing at all,
            // silently and with no error.
            renderer.enabled = false;
            renderer.enabled = true;

            PlacePlayer(world);

            if (label != null)
                label.text = $"{_index + 1}/{WorldCatalog.All.Count}  {world.displayName}";
            Debug.Log($"[WorldCycler] {world.displayName} — spawn {world.ScaledSpawn}, " +
                      $"yaw {world.yawDegrees}°, scale {world.worldScale}");
        }

        /// <summary>Moves the rig to the world's measured spawn and facing. In an XR rig the
        /// headset owns the camera pose, so the ORIGIN moves — never the camera.</summary>
        void PlacePlayer(WorldDefinition world)
        {
            if (xrOrigin != null)
            {
                xrOrigin.transform.SetPositionAndRotation(world.ScaledSpawn, world.SpawnRotation);
            }
            else if (_camera != null)
            {
                _camera.transform.SetPositionAndRotation(
                    world.ScaledSpawn + Vector3.up * 1.6f, world.SpawnRotation);
            }

            if (_camera != null)
            {
                _camera.farClipPlane = world.cameraFar;
                _camera.nearClipPlane = 0.1f;
                // Marble bakes final radiance into the splats, so no sky and no grade —
                // a lit background washes the world out.
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.black;
            }
        }

        void Unload()
        {
            if (_current == null) return;
            if (Application.isPlaying) Destroy(_current); else DestroyImmediate(_current);
            _current = null;
            // ~175 MB per world; without this the swaps accumulate until the device gives up.
            Resources.UnloadUnusedAssets();
        }

        static GaussianSplatAsset LoadSplatAsset(string path)
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(path);
#else
            Debug.LogError("[WorldCycler] runtime world loading needs Addressables; " +
                           "AssetDatabase is editor-only.");
            return null;
#endif
        }
    }
}
