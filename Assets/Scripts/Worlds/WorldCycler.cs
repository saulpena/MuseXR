using System.Collections;
using GaussianSplatting.Runtime;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using MusePico.Worlds;   // PhysicsBounds — shared with the ported cyclers

namespace MuseXR.Worlds
{
    /// <summary>
    /// Loads one Marble world at a time through Addressables, holds it, then releases it and
    /// moves to the next — placing the player at that world's spawn.
    ///
    /// Addressables rather than direct references because the splat converter is editor-only,
    /// so worlds have to be baked in as assets; and because loading them all at once is not an
    /// option (Skylar's eight are ~1.37 GB). One bundle per world, loaded and released around
    /// each swap.
    /// </summary>
    public class WorldCycler : MonoBehaviour
    {
        [Header("What to show")]
        public WorldSet worldSet = WorldSet.Samples;

        [Header("Timing")]
        public float secondsPerWorld = 15f;
        public bool autoAdvance = true;

        [Header("Scene references")]
        public XROrigin xrOrigin;
        public TextMesh label;

        [Header("Collision")]
        [Tooltip("A splat cannot be collided with. Without this box the rig's CharacterController " +
                 "has nothing to stand on and gravity drops the visitor out of the world.")]
        public bool buildFallbackBounds = true;

        [Tooltip("How far inside the capture's own bounds the walls stand, metres.")]
        public float boundsInset = 0.5f;

        [Tooltip("Height of the boundary walls above the floor, metres.")]
        public float wallHeight = 4f;

        /// <summary>
        /// GaussianSplatRenderer gates ALL drawing on these five being non-null
        /// (its `resourcesAreSetUp`), and a renderer created with AddComponent at runtime gets
        /// none of them — it logs "Resource references are missing" once and then silently draws
        /// nothing. They are serialized here rather than found by name so the shaders are also
        /// pulled into the player build; Shader.Find would return null in a build after stripping.
        /// </summary>
        [Header("Splat renderer resources (required — from the gaussian-splatting package)")]
        public Shader shaderSplats;
        public Shader shaderComposite;
        public Shader shaderDebugPoints;
        public Shader shaderDebugBoxes;
        public ComputeShader csSplatUtilities;

        /// <summary>
        /// True when something else decides which world is showing — the journey runner, walking
        /// the exhibition spine. The cycler then loads only what it is told to and never advances
        /// on its own, because a world swapping itself out mid-conversation pulls the ground from
        /// under the visitor.
        /// </summary>
        public bool drivenExternally;

        /// <summary>
        /// Optional: the height the visitor lands at in a world with a measured spawn, instead of its
        /// flat groundY. Null (the default, and every scene but WorldDoors) keeps groundY. A scene that
        /// knows the capture's real floor sets this; returning null for a world keeps groundY there.
        /// The fallback walking floor is built at the same height, so the visitor can walk where they
        /// stand. Not serialized: a scene cannot carry a stale copy of it.
        /// </summary>
        [System.NonSerialized] public System.Func<WorldDefinition, float?> landingFloor;

        /// <summary>The world currently loaded, or null before the first one arrives.</summary>
        public WorldDefinition Current { get; private set; }

        /// <summary>
        /// How long the last world took from request to standing in it. Measured rather than
        /// guessed, because "there is a delay at startup" needs a number before it needs a fix.
        /// </summary>
        public float LastLoadSeconds { get; private set; }

        /// <summary>Raised once a world is loaded and the visitor has been placed in it.</summary>
        public event System.Action<WorldDefinition> WorldChanged;

        int _index = -1;
        GameObject _current;
        GameObject _bounds;

        /// <summary>The current world's physics floor and walls (PhysicsBounds), or null.</summary>
        public GameObject CurrentBounds => _bounds;
        Camera _camera;
        AsyncOperationHandle<GaussianSplatAsset> _handle;
        bool _hasHandle;

        void Start()
        {
            if (xrOrigin == null) xrOrigin = FindObjectOfType<XROrigin>();
            _camera = xrOrigin != null ? xrOrigin.Camera : Camera.main;

            if (shaderSplats == null || shaderComposite == null || shaderDebugPoints == null ||
                shaderDebugBoxes == null || csSplatUtilities == null)
            {
                Debug.LogError("[WorldCycler] splat renderer resources are not assigned — worlds " +
                               "will load and report their splat count but render NOTHING. " +
                               "Assign the four shaders and SplatUtilities.compute from " +
                               "Packages/org.nesnausk.gaussian-splatting/Shaders on this component.");
            }

            if (!drivenExternally) StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            for (;;)
            {
                yield return ShowNext();
                if (!autoAdvance) yield break;
                yield return new WaitForSeconds(secondsPerWorld);
            }
        }

        IEnumerator ShowNext()
        {
            var list = WorldCatalog.Get(worldSet);
            if (list.Count == 0) { Debug.LogError("[WorldCycler] catalog empty"); yield break; }
            _index = (_index + 1) % list.Count;
            yield return ShowWorld(list[_index]);
        }

        /// <summary>
        /// Load one named world and stand the visitor in it. The spine addresses worlds by key, so
        /// this is the entry the journey runner uses. An unknown key is reported rather than
        /// silently leaving the last world up, which reads as the chapter failing to change.
        /// </summary>
        public IEnumerator ShowWorldByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) yield break;
            foreach (var candidate in WorldCatalog.Get(worldSet))
                if (candidate.key == key) { yield return ShowWorld(candidate); yield break; }

            Debug.LogError($"[WorldCycler] no world '{key}' in {worldSet}. The chapter cannot open.");
        }

        /// <summary>Load a world, place the visitor in it, and announce it.</summary>
        public IEnumerator ShowWorld(WorldDefinition world)
        {
            if (world == null) yield break;
            var startedAt = Time.realtimeSinceStartup;

            SetLabel($"{world.displayName}\nloading…");
            CancelBeside();
            Unload();

            var handle = Addressables.LoadAssetAsync<GaussianSplatAsset>(world.Address);
            _handle = handle; _hasHandle = true;
            yield return handle;

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Debug.LogError($"[WorldCycler] failed to load '{world.Address}'. " +
                               "Has Addressables content been built for this player?");
                SetLabel($"{world.displayName}\nLOAD FAILED");
                yield break;
            }

            var asset = handle.Result;
            _current = CreateWorldObject(world, asset, null);
            var renderer = _current.GetComponent<GaussianSplatRenderer>();

            Place(world, asset);
            Current = world;
            LastLoadSeconds = Time.realtimeSinceStartup - startedAt;
            SetLabel($"{world.displayName}\n{asset.splatCount:N0} splats");
            Debug.Log($"[WorldCycler] {world.displayName}: {asset.splatCount:N0} splats, " +
                      $"ready in {LastLoadSeconds:F2}s, sortNthPass={renderer.m_SortNthFrame} " +
                      $"shOrder={renderer.m_SHOrder}");
            WorldChanged?.Invoke(world);
        }

        /// <summary>A world's splat object, ready to draw, under <paramref name="parent"/> (null: the root).</summary>
        GameObject CreateWorldObject(WorldDefinition world, GaussianSplatAsset asset, Transform parent)
        {
            var go = new GameObject($"World_{world.key}");
            go.transform.SetParent(parent, false);
            go.transform.localScale = world.SplatScale;

            var renderer = go.AddComponent<GaussianSplatRenderer>();
            renderer.m_Asset = asset;
            renderer.m_ShaderSplats = shaderSplats;
            renderer.m_ShaderComposite = shaderComposite;
            renderer.m_ShaderDebugPoints = shaderDebugPoints;
            renderer.m_ShaderDebugBoxes = shaderDebugBoxes;
            renderer.m_CSSplatUtilities = csSplatUtilities;
            SplatRenderTuning.Apply(renderer);
            // Resources are built in OnEnable, which ran before the asset and shaders were
            // assigned — without this toggle the renderer draws nothing at all, silently.
            renderer.enabled = false;
            renderer.enabled = true;
            return go;
        }

        // ---- a world beside the current one: the next chapter, behind a door ----------------

        GameObject _beside;
        AsyncOperationHandle<GaussianSplatAsset> _besideHandle;
        bool _hasBeside;
        WorldDefinition _besideWorld;

        /// <summary>The current world's splat renderer, or null.</summary>
        public GaussianSplatRenderer CurrentRenderer => _current != null ? _current.GetComponent<GaussianSplatRenderer>() : null;

        /// <summary>The world waiting beside the current one, or null.</summary>
        public WorldDefinition Beside => _hasBeside ? _besideWorld : null;

        /// <summary>
        /// Load <paramref name="world"/> as well as the current one, under <paramref name="parent"/>
        /// (a door's pivot), without touching the current world or moving the visitor. The renderer
        /// is handed to <paramref name="done"/>, or null if the load failed. One at a time: a second
        /// call cancels the first.
        /// </summary>
        public IEnumerator LoadBeside(WorldDefinition world, Transform parent, System.Action<GaussianSplatRenderer> done)
        {
            CancelBeside();
            if (world == null) { done?.Invoke(null); yield break; }
            var handle = Addressables.LoadAssetAsync<GaussianSplatAsset>(world.Address);
            _besideHandle = handle; _hasBeside = true; _besideWorld = world;
            yield return handle;

            if (!_hasBeside || !_besideHandle.Equals(handle)) yield break;   // cancelled meanwhile
            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Debug.LogError($"[WorldCycler] failed to load '{world.Address}' beside the current world.");
                CancelBeside();
                done?.Invoke(null);
                yield break;
            }
            _beside = CreateWorldObject(world, handle.Result, parent);
            Debug.Log($"[WorldCycler] {world.displayName} loaded beside {(Current != null ? Current.displayName : "nothing")}");
            done?.Invoke(_beside.GetComponent<GaussianSplatRenderer>());
        }

        /// <summary>Drop the world waiting beside the current one.</summary>
        public void CancelBeside()
        {
            if (_beside != null) { if (Application.isPlaying) Destroy(_beside); else DestroyImmediate(_beside); }
            _beside = null;
            if (_hasBeside) Addressables.Release(_besideHandle);
            _hasBeside = false;
            _besideWorld = null;
        }

        /// <summary>
        /// The visitor has walked through a door into the world loaded beside, and that world now
        /// stands at the origin (the door system moved it and the visitor back together). Make it
        /// the current world: release the old one, keep <paramref name="bounds"/> as its walking
        /// floor, and announce it. The visitor is not moved — they are already standing in it.
        /// </summary>
        public void AdoptBeside(GameObject bounds, float floorY)
        {
            if (!_hasBeside || _beside == null) { Debug.LogError("[WorldCycler] nothing beside to adopt"); return; }
            Unload();   // the old splat may already be gone (the door destroys it); its handle and floor are ours

            _beside.transform.SetParent(null, true);
            _current = _beside; _beside = null;
            _handle = _besideHandle; _hasHandle = true; _hasBeside = false;
            if (bounds != null) bounds.transform.SetParent(null, true);
            _bounds = bounds;

            Current = _besideWorld; _besideWorld = null;
            FloorY = floorY;
            if (_camera != null) _camera.farClipPlane = Current.cameraFar;
            Debug.Log($"[WorldCycler] walked into {Current.displayName}");
            WorldChanged?.Invoke(Current);
        }

        /// <summary>Where the visitor's floor is in the current world: the landed floor, or groundY.</summary>
        public float FloorY { get; private set; }

        /// <summary>In an XR rig the headset owns the camera pose, so the ORIGIN moves.</summary>
        void Place(WorldDefinition world, GaussianSplatAsset asset)
        {
            Vector3 pos;
            Quaternion rot;

            float? landed = null;
            if (world.hasMeasuredSpawn)
            {
                pos = world.ScaledSpawn;
                rot = world.SpawnRotation;
                landed = landingFloor != null ? landingFloor(world) : null;
                if (landed.HasValue) pos.y = landed.Value;
            }
            else
            {
                // No measured spawn — the CDN samples carry no semantics_metadata, unlike
                // Skylar's worlds whose spawns were playtested. Standing at the exact centre of
                // the bounds put the camera INSIDE furniture and walls (verified in Play Mode:
                // a black screen with a few out-of-focus blobs), so back off along -Z to the edge
                // of the cloud and look in towards the middle. Still a heuristic; generating
                // these worlds through the Marble API would supply a real ground plane and scale.
                var min = asset.boundsMin * world.worldScale;
                var max = asset.boundsMax * world.worldScale;
                var centre = (min + max) * 0.5f;
                var size = max - min;

                // Eye level at 55% of the world's height reads as standing rather than crouching,
                // and clears floor-level clutter that the 25% guess sat inside of.
                float eyeY = min.y + size.y * 0.55f;
                float back = Mathf.Max(size.x, size.z) * 0.45f;

                pos = new Vector3(centre.x, eyeY, centre.z - back);
                var lookAt = new Vector3(centre.x, eyeY, centre.z);
                rot = pos == lookAt ? Quaternion.identity
                                    : Quaternion.LookRotation(lookAt - pos, Vector3.up);
            }

            // A splat is not geometry, so without this the CharacterController on the rig has
            // nothing to stand on and gravity drops the visitor out of the world on frame one.
            // For Skylar's worlds her groundY is the real floor; for the samples the placement
            // heuristic's own Y is the best guess there is.
            if (buildFallbackBounds)
            {
                float floorY = world.hasMeasuredSpawn ? (landed ?? world.groundY * world.worldScale) : pos.y;
                FloorY = floorY;
                _bounds = PhysicsBounds.BuildFromSplatBounds(
                    "World Bounds (physics only)", asset.boundsMin, asset.boundsMax,
                    world.worldScale, floorY, boundsInset, wallHeight);
            }

            // A CharacterController caches its own position and ignores the transform being
            // written underneath it until it is toggled.
            var character = xrOrigin != null ? xrOrigin.GetComponent<CharacterController>() : null;
            bool had = character != null && character.enabled;
            if (had) character.enabled = false;

            if (xrOrigin != null) xrOrigin.transform.SetPositionAndRotation(pos, rot);
            else if (_camera != null) _camera.transform.SetPositionAndRotation(pos + Vector3.up * 1.6f, rot);

            if (had) character.enabled = true;
            Physics.SyncTransforms();

            if (_camera != null)
            {
                _camera.farClipPlane = world.cameraFar;
                _camera.nearClipPlane = 0.1f;
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
            if (_current != null)
            {
                if (Application.isPlaying) Destroy(_current); else DestroyImmediate(_current);
                _current = null;
            }
            if (_hasHandle)
            {
                Addressables.Release(_handle);
                _hasHandle = false;
            }
        }

        void OnDestroy() { CancelBeside(); Unload(); }

        void SetLabel(string text) { if (label != null) label.text = text; }
    }
}
