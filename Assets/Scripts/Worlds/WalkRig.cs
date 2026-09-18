using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using GaussianSplatting.Runtime;
using TMPro;
using System.Collections;
using System.Collections.Generic;

namespace MusePico.Worlds
{
    /// <summary>
    /// Stand in one of Skylar's Marble worlds. Builds the world, gives it real collision, and
    /// puts the visitor at her recorded spawn — then gets out of the way.
    ///
    /// <b>This component does not move the visitor.</b> Locomotion, turning, gravity and
    /// collision belong to the XRI rig (<c>Assets/Prefabs/MuseXR Rig.prefab</c>): a
    /// <c>DynamicMoveProvider</c> drives a <c>CharacterController</c>, which is Unity's own
    /// character-versus-world solver and handles walls, slopes and step-up properly. An earlier
    /// version of this class did its own thumbstick reading, wall raycasts and per-frame ground
    /// snapping; that duplicated the engine badly and would now fight the move provider for
    /// control of the same transform every frame.
    ///
    /// What is left here is the part the rig cannot know about:
    ///
    ///   1. <b>Her converted splats.</b> Built at runtime because a GaussianSplatRenderer added
    ///      with AddComponent gets no shader references and then silently draws nothing.
    ///   2. <b>Her collider GLB is the ground.</b> A splat cannot be collided with; the collider
    ///      mesh can. It is instantiated invisible and exists only for physics.
    ///   3. <b>The mirror.</b> glTFast negates X importing glTF into Unity's left-handed space;
    ///      three.js keeps glTF coordinates and our <c>.spz</c> converter matches three.js, so the
    ///      collider arrives mirrored against its own splat and needs X negated back. Measured:
    ///      unmirrored, a downward ray at her recorded spawn hits nothing; mirrored it lands at
    ///      y = -0.011 against her recorded groundY of 0.
    ///   4. <b>A floor and walls of last resort.</b> Her collider is a shell with no furniture,
    ///      and its floor is a patch rather than a full floor — along the gallery only 12 of 28
    ///      sample rays hit anything. With a CharacterController under gravity a hole in the floor
    ///      is a fall with no bottom, so the walk box is closed with an invisible box floor and
    ///      four boundary walls. That is ordinary level blocking, and it is what replaced the old
    ///      per-frame bounds clamp.
    /// </summary>
    public class WalkRig : MonoBehaviour
    {
        [Header("World")]
        /// <summary>
        /// Addressables key for the converted world — which <see cref="MuseXR.EditorTools.AddressableWorldSetup"/>
        /// sets to the asset's file name, so it is the world key ("sunlit-museum-gallery-hallway").
        ///
        /// Deliberately an address rather than a direct <c>GaussianSplatAsset</c>: see
        /// <see cref="WorldAssets"/> for why this project has exactly one loading path. A direct
        /// reference here would also be built into the APK alongside the bundle.
        /// </summary>
        public string worldAddress;

        /// <summary>The matching <c>*-collider.glb</c>, dragged in as the imported prefab.</summary>
        public GameObject colliderModel;

        /// <summary>
        /// Negate X on the collider so it lines up with its own splat.
        ///
        /// TRUE for Skylar's eight: their <c>.spz</c> came through our three.js-matching
        /// converter, and glTFast negates X importing the glTF, so the two disagree by a mirror.
        /// FALSE for a world exported straight out of Marble with <b>Coordinate system =
        /// OpenGL</b>, which is already in Unity's handedness and needs no correction.
        ///
        /// This is per-world and MEASURED, never assumed — an X-symmetric room hides the error
        /// visually while still putting the visitor against the wrong wall.
        /// </summary>
        public bool mirrorColliderX = true;

        /// <summary>Leave the collider's renderers on. Debug only — a Marble collider is a crude
        /// reconstruction and looks like nothing, but drawing it over the splat is the only way
        /// to confirm the two are aligned.</summary>
        public bool showColliderForDebug;

        /// <summary>Immersion multiplier. Deliberately 1: she used 1.7-2 so companions read at a
        /// decent size in a browser viewport, but in a headset metres should be metres — scaling
        /// the world up scales the visitor down, which reads as being a child.</summary>
        public float worldScale = 1f;

        [Header("Placement — muse-infinity/config/worlds.js, pre-scale")]
        public Vector2 spawn = new Vector2(1.79f, 0.30f);
        public float groundY;
        public float yawDegrees;

        /// <summary>Walk box as (minX, maxX, minZ, maxZ), pre-scale. Now used to build the
        /// fallback floor and the boundary walls rather than to clamp a position each frame.</summary>
        public Vector4 bounds = new Vector4(-2.47f, 10.0f, -14.62f, 13.73f);
        public float cameraFar = 200f;

        [Header("Fallback collision")]
        [Tooltip("Close the walk box with an invisible floor and walls. Her collider floor has " +
                 "holes, and under gravity a hole is a bottomless fall.")]
        public bool buildFallbackBounds = true;

        [Tooltip("How high the boundary walls stand above the fallback floor, metres.")]
        public float wallHeight = 4f;

        [Header("Splat performance")]
        /// <summary>
        /// Re-sort the splats every Nth frame instead of every frame.
        ///
        /// The package defaults to 1, which radix-sorts all 500,000 splats EVERY frame — and under
        /// Multi Pass that happens once per eye, so twice. Splats only need re-sorting when the
        /// view direction changes materially, so 2-3 is close to free visually and cuts the sort
        /// cost proportionally. This is the same "sort once and reuse" idea the mobile-VR splat
        /// literature recommends; the package already implements it and nothing was using it.
        ///
        /// Raise it further if sorting still dominates; the artifact to watch for is splats
        /// briefly compositing in the wrong order during fast turns.
        /// </summary>
        [Range(1, 6)] public int sortNthFrame = 3;

        /// <summary>
        /// Spherical-harmonics order to evaluate. The package defaults to 3 (full SH).
        ///
        /// <b>Every World Labs Marble export is shLevel 0</b> — measured from the SPZ header:
        /// <c>shDegree: 0</c>. There are no higher-order coefficients in the data, so evaluating
        /// order 3 spends GPU time on information that does not exist. 0 should be visually
        /// identical on this content and cheaper.
        ///
        /// Raise it only for splat content that genuinely carries spherical harmonics — a capture
        /// from another tool might.
        /// </summary>
        [Range(0, 3)] public int shOrder;

        [Header("Splat renderer resources (see SplatWorldCycler for why these are serialized)")]
        public Shader shaderSplats;
        public Shader shaderComposite;
        public Shader shaderDebugPoints;
        public Shader shaderDebugBoxes;
        public ComputeShader csSplatUtilities;

        [Header("Readout")]
        public TMP_Text hud;

        XROrigin _origin;
        Camera _camera;
        GameObject _splatObject;
        GameObject _colliderObject;
        CharacterController _character;
        AsyncOperationHandle<GaussianSplatAsset> _worldHandle;
        readonly List<float> _hits = new List<float>();
        readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

        /// <summary>True once the world has loaded and the visitor has been placed. A walkability
        /// test has to wait for this — the collider does not exist until the load completes.</summary>
        public bool Ready { get; private set; }

        /// <summary>The loaded world, once <see cref="Ready"/>. Null before that.</summary>
        public GaussianSplatAsset World => WorldAssets.Succeeded(_worldHandle) ? _worldHandle.Result : null;

        IEnumerator Start()
        {
            _origin = FindAnyObjectByType<XROrigin>();
            _camera = _origin != null ? _origin.Camera : Camera.main;
            _character = _origin != null ? _origin.GetComponent<CharacterController>() : null;

            if (string.IsNullOrWhiteSpace(worldAddress))
            {
                Debug.LogError("[WalkRig] worldAddress is empty — nothing to load.");
                yield break;
            }

            _worldHandle = WorldAssets.LoadAsync(worldAddress);
            yield return _worldHandle;

            if (!WorldAssets.Succeeded(_worldHandle))
            {
                Debug.LogError(WorldAssets.DescribeFailure(worldAddress, _worldHandle));
                yield break;
            }

            BuildWorld();
            BuildCollider();
            BuildFallbackBounds();
            Place();
            Ready = true;
        }

        void OnDestroy()
        {
            WorldAssets.Release(ref _worldHandle);
        }

        void BuildWorld()
        {
            var world = _worldHandle.Result;
            _splatObject = new GameObject("Splat_" + world.name);
            _splatObject.transform.localScale = Vector3.one * worldScale;
            var renderer = _splatObject.AddComponent<GaussianSplatRenderer>();
            renderer.m_Asset = world;
            renderer.m_ShaderSplats = shaderSplats;
            renderer.m_ShaderComposite = shaderComposite;
            renderer.m_ShaderDebugPoints = shaderDebugPoints;
            renderer.m_ShaderDebugBoxes = shaderDebugBoxes;
            renderer.m_CSSplatUtilities = csSplatUtilities;
            // Two tunables the package exposes and nothing was setting — see the fields above.
            renderer.m_SortNthFrame = Mathf.Max(1, sortNthFrame);
            renderer.m_SHOrder = Mathf.Clamp(shOrder, 0, 3);
            // Resources are built in OnEnable, which ran before the asset was assigned.
            renderer.enabled = false;
            renderer.enabled = true;
            Debug.Log($"[WalkRig] splat {world.name}: {world.splatCount:N0} splats " +
                      $"validAsset={renderer.HasValidAsset} validRenderSetup={renderer.HasValidRenderSetup} " +
                      $"sortNthFrame={renderer.m_SortNthFrame} shOrder={renderer.m_SHOrder} " +
                      $"bounds={world.boundsMin:F2}..{world.boundsMax:F2}");
        }

        void BuildCollider()
        {
            if (colliderModel == null) { Debug.LogWarning("[WalkRig] no collider model — only the fallback box will exist."); return; }
            // X negated to undo glTFast's handedness flip; see mirrorColliderX for when it applies.
            float sx = mirrorColliderX ? -worldScale : worldScale;
            _colliderObject = new GameObject("Collider_" + colliderModel.name);
            _colliderObject.transform.localScale = new Vector3(sx, worldScale, worldScale);
            var instance = Instantiate(colliderModel, _colliderObject.transform);
            int meshes = 0;
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                meshes++;
            }
            // Physics only. A Marble collider is a crude reconstruction and looks like nothing.
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = showColliderForDebug;
            Physics.SyncTransforms();
            Debug.Log($"[WalkRig] collider: {meshes} mesh(es), mirrorX={mirrorColliderX}, " +
                      $"renderers={(showColliderForDebug ? "ON (debug)" : "off")}.");
        }

        /// <summary>Backs her collider with a closed box, so a gap in its patchy floor is a step
        /// onto solid ground rather than a fall. Uses her authored walk box, not the splat bounds.</summary>
        void BuildFallbackBounds()
        {
            if (!buildFallbackBounds) return;

            float y = groundY * worldScale;
            PhysicsBounds.Build("World Bounds (physics only)",
                                bounds.x * worldScale, bounds.y * worldScale,
                                bounds.z * worldScale, bounds.w * worldScale,
                                y, wallHeight);
            Debug.Log($"[WalkRig] fallback bounds: floor top y={y:F2}, walls {wallHeight:F1} m.");
        }

        void Place()
        {
            if (_origin == null) { Debug.LogError("[WalkRig] no XR Origin in the scene."); return; }

            var flat = new Vector3(spawn.x * worldScale, 0f, spawn.y * worldScale);
            float y = SampleGround(flat.x, flat.z);

            // A CharacterController caches its own position; writing the transform underneath it
            // is ignored until it is toggled. Disable, move, re-enable — the documented way.
            bool had = _character != null && _character.enabled;
            if (had) _character.enabled = false;
            _origin.transform.SetPositionAndRotation(new Vector3(flat.x, y, flat.z),
                                                     Quaternion.Euler(0f, yawDegrees, 0f));
            if (had) _character.enabled = true;
            Physics.SyncTransforms();

            if (_camera != null)
            {
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane = cameraFar * worldScale;
                // Marble bakes final radiance into the splats — a lit sky washes them out.
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.black;
            }
            Debug.Log($"[WalkRig] placed origin at {_origin.transform.position:F3} yaw {yawDegrees}");
        }

        /// <summary>Every downward hit under (x,z), reduced to one height by <see cref="WalkGround"/>.
        /// Used once, to seat the visitor at spawn — per-frame ground is the CharacterController's job.</summary>
        float SampleGround(float x, float z)
        {
            float reference = groundY * worldScale;
            float top = reference + 300f * worldScale;
            int count = Physics.RaycastNonAlloc(new Vector3(x, top, z), Vector3.down,
                                                _hitBuffer, 600f * worldScale);
            _hits.Clear();
            for (int i = 0; i < count; i++) _hits.Add(_hitBuffer[i].point.y);
            return WalkGround.PickGroundHeight(_hits, reference, worldScale);
        }

        void Update()
        {
            if (hud == null || _origin == null) return;
            var head = _camera != null ? _camera.transform.position : _origin.transform.position;
            // origin and head are printed separately on purpose: with room-scale tracking they
            // differ, and seeing both confirms locomotion and tracking compose instead of fighting.
            hud.text = $"origin {_origin.transform.position:F2}\nhead {head:F2}";
        }
    }
}
