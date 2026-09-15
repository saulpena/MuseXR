using Unity.XR.CoreUtils;
using UnityEngine;
using GaussianSplatting.Runtime;
using TMPro;
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
        public GaussianSplatAsset world;

        /// <summary>The matching <c>*-collider.glb</c>, dragged in as the imported prefab.</summary>
        public GameObject colliderModel;

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
        readonly List<float> _hits = new List<float>();
        readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

        void Start()
        {
            _origin = FindAnyObjectByType<XROrigin>();
            _camera = _origin != null ? _origin.Camera : Camera.main;
            _character = _origin != null ? _origin.GetComponent<CharacterController>() : null;

            BuildWorld();
            BuildCollider();
            BuildFallbackBounds();
            Place();
        }

        void BuildWorld()
        {
            if (world == null) { Debug.LogError("[WalkRig] no world asset assigned."); return; }
            _splatObject = new GameObject("Splat_" + world.name);
            _splatObject.transform.localScale = Vector3.one * worldScale;
            var renderer = _splatObject.AddComponent<GaussianSplatRenderer>();
            renderer.m_Asset = world;
            renderer.m_ShaderSplats = shaderSplats;
            renderer.m_ShaderComposite = shaderComposite;
            renderer.m_ShaderDebugPoints = shaderDebugPoints;
            renderer.m_ShaderDebugBoxes = shaderDebugBoxes;
            renderer.m_CSSplatUtilities = csSplatUtilities;
            // Resources are built in OnEnable, which ran before the asset was assigned.
            renderer.enabled = false;
            renderer.enabled = true;
            Debug.Log($"[WalkRig] splat {world.name}: {world.splatCount:N0} splats " +
                      $"validAsset={renderer.HasValidAsset} validRenderSetup={renderer.HasValidRenderSetup} " +
                      $"bounds={world.boundsMin:F2}..{world.boundsMax:F2}");
        }

        void BuildCollider()
        {
            if (colliderModel == null) { Debug.LogWarning("[WalkRig] no collider model — only the fallback box will exist."); return; }
            // X negated to undo glTFast's handedness flip; see the class summary.
            _colliderObject = new GameObject("Collider_" + colliderModel.name);
            _colliderObject.transform.localScale = new Vector3(-worldScale, worldScale, worldScale);
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
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            Physics.SyncTransforms();
            Debug.Log($"[WalkRig] collider: {meshes} mesh(es) mirrored on X, renderers off.");
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
