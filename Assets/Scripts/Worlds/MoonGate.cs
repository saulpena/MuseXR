using System;
using System.Collections.Generic;
using GaussianSplatting.Runtime;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Her moon-gate transition (chatplan Q2: portal doors, the next world showing through, the
    /// visitor walks or teleports through - no fades; Curate/Palace = moon gate, a round mask).
    /// Drop <c>Assets/Prefabs/Moon Gate.prefab</c> into a scene and place it:
    ///
    ///   position   the centre of the gate's threshold, ON THE FLOOR;
    ///   rotation   +Z pointing OUT through the gate, into the next world (the blue arrow).
    ///
    /// Give it the next world (<see cref="nextWorldAsset"/> plus its <see cref="nextWorldKey"/> in
    /// <see cref="WorldCatalog.Small"/>), then call <see cref="Open"/> with the world the visitor is
    /// in. The next world is put down behind the gate so its playtested spawn lies just through it,
    /// facing on (<see cref="WorldDoorLayout.NextWorldFrame"/>); <c>Portal Door.prefab</c>'s
    /// SplatPortalDoor runs unchanged with a keyhole mask and no door leaves, so a capture's own
    /// stone ring is the frame. When the visitor looks toward it the next world reveals inside the
    /// keyhole; crossing it raises <see cref="Crossed"/>, hides <c>currentWorldProps</c>, and once
    /// the door has shut the old world and those props are destroyed (<see cref="Arrived"/>).
    ///
    /// The keyhole is the palace capture's moon gate, measured 3 Oct 2026 by triangulating two
    /// Editor captures: a round opening ~3.4 m across centred ~2.3 m up over a ~2.2 m passage, set a
    /// little inside the gold ring. Used by Tests/PalaceChapter.unity (palace -> grotto).
    /// </summary>
    public sealed class MoonGate : MonoBehaviour
    {
        [Tooltip("Assets/Prefabs/Portal Door.prefab.")]
        public GameObject doorPrefab;

        [Tooltip("The world seen and walked into through the gate (a converted -500k GaussianSplatAsset).")]
        public GaussianSplatAsset nextWorldAsset;

        [Tooltip("Its key in WorldCatalog.Small, e.g. grotto-hall-of-time-500k: gives its scale, mirror and spawn.")]
        public string nextWorldKey;

        /// <summary>The palace capture's gate: the defaults below.</summary>
        public const float Radius = 1.6f, CentreHeight = 2.3f, PassageWidth = 2.1f;

        [Header("Keyhole (metres)")]
        [Tooltip("Radius of the round opening.")]
        public float radius = Radius;
        [Tooltip("Height of the round opening's centre above the threshold.")]
        public float centreHeight = CentreHeight;
        [Tooltip("Width of the passage below the circle, down to the floor. 0: round opening only (a gate with a solid lower wall).")]
        public float passageWidth = PassageWidth;

        /// <summary>How far past the threshold the next world's spawn lies (JourneyDoors' value).</summary>
        public const float ArrivalPastDoor = 1.2f;

        /// <summary>From this far the gate starts to reveal when looked at: ~7.5 m from her court.</summary>
        public const float TriggerDistance = 10f;

        public SplatPortalDoor Door { get; private set; }
        public GaussianSplatRenderer NextWorld { get; private set; }
        public WorldDefinition NextDefinition { get; private set; }
        public bool IsOpen => Door != null;

        /// <summary>The visitor has stepped through: they are in the next world.</summary>
        public event Action Crossed;
        /// <summary>The door has shut behind them and the old world is gone.</summary>
        public event Action Arrived;

        bool _crossed, _arrived;
        Transform _pads;

        /// <summary>
        /// The threshold step: an invisible teleport pad on the floor just before the gate. Locomotion
        /// is teleport only, and a capture's own collider usually walls the door off (in the grotto the
        /// arch's back panel stopped every teleport ray), so teleporting onto this step while the
        /// gate is open carries the visitor through - the same as walking through.
        /// </summary>
        public const float StepDepth = 1.2f;
        /// <summary>The floor laid on the far side at threshold height, to arrive and stand on.</summary>
        public const float LandingSize = 6f;
        /// <summary>The "Teleport" interaction layer the rig's teleport rays select on (bit 31).</summary>
        const int TeleportLayer = 1 << 31;

        /// <summary>
        /// Open the gate onto the next world. <paramref name="currentWorld"/> is the splat the visitor
        /// stands in; <paramref name="currentWorldProps"/> are hidden on crossing and destroyed with
        /// it (move anything that should come along out of them first). Returns false, with a log
        /// saying what is missing, when the gate cannot open.
        /// </summary>
        public bool Open(GaussianSplatRenderer currentWorld, IEnumerable<GameObject> currentWorldProps, Transform head = null)
        {
            if (IsOpen) return true;
            NextDefinition = Find(nextWorldKey);
            if (doorPrefab == null || nextWorldAsset == null || NextDefinition == null || currentWorld == null)
            {
                Debug.LogError($"[MoonGate] cannot open: door prefab {doorPrefab != null}, next asset {nextWorldAsset != null}, " +
                               $"'{nextWorldKey}' in WorldCatalog.Small {NextDefinition != null}, current world {currentWorld != null}");
                return false;
            }

            var flat = transform.forward; flat.y = 0f;
            var pose = new DoorPose { position = transform.position, rotation = Quaternion.LookRotation(flat.normalized, Vector3.up) };
            var spawn = NextDefinition.ScaledSpawn; spawn.y = 0f;
            WorldDoorLayout.NextWorldFrame(pose, spawn, NextDefinition.SpawnRotation, ArrivalPastDoor, transform.position.y,
                                           out var framePos, out var frameRot);
            var pivot = new GameObject("Next World (behind the moon gate): " + NextDefinition.key).transform;
            pivot.SetPositionAndRotation(framePos, frameRot);
            NextWorld = MakeRenderer(NextDefinition, nextWorldAsset, currentWorld, pivot);

            var go = Instantiate(doorPrefab);
            go.name = "Moon Gate Door to " + NextDefinition.displayName;
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            Door = go.GetComponent<SplatPortalDoor>();
            Door.currentWorld = currentWorld;
            Door.nextWorld = NextWorld;
            Door.currentWorldProps = currentWorldProps != null ? new List<GameObject>(currentWorldProps).ToArray() : new GameObject[0];
            Door.triggerDistance = TriggerDistance;
            float top = centreHeight + radius;
            Door.apertureSize = new Vector2(2f * radius, top);
            if (Door.aperture != null) Door.aperture.localPosition = new Vector3(0f, top * 0.5f, 0f);
            Door.maskMesh = KeyholeMesh(radius, centreHeight, passageWidth);
            if (Door.doorVisual != null) Door.doorVisual.gameObject.SetActive(false);
            Door.doorVisual = null; Door.leftLeaf = null; Door.rightLeaf = null;   // the capture's ring is the frame
            var eye = head != null ? head : (Camera.main != null ? Camera.main.transform : null);
            if (eye != null)
            {
                Door.head = eye;
                var cam = eye.GetComponent<Camera>();
                // Seen through the gate, the next world's horizon must not be cut off by this one's far plane.
                if (cam != null) cam.farClipPlane = Mathf.Max(cam.farClipPlane, NextDefinition.cameraFar);
            }
            Door.enabled = true;
            BuildPads();
            Debug.Log($"[MoonGate] open at {pose.position:F2} onto {NextDefinition.displayName}");
            return true;
        }

        void BuildPads()
        {
            _pads = new GameObject("Moon Gate Pads").transform;
            _pads.SetParent(transform, false);
            float w = Mathf.Max(passageWidth, 1.2f);
            Pad("Threshold Step", new Vector3(0f, -0.05f, -StepDepth * 0.5f - 0.05f), new Vector3(w, 0.1f, StepDepth));
            Pad("Landing", new Vector3(0f, -0.05f, LandingSize * 0.5f + 0.1f), new Vector3(LandingSize, 0.1f, LandingSize));
        }

        void Pad(string name, Vector3 local, Vector3 size)
        {
            var go = new GameObject(name);
            go.SetActive(false);   // XRI registers an area once, with its colliders set
            go.transform.SetParent(_pads, false);
            go.transform.localPosition = local;
            go.AddComponent<BoxCollider>().size = size;
            var area = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            area.interactionLayers = TeleportLayer;
            area.filterSelectionByHitNormal = true;
            go.SetActive(true);
        }

        /// <summary>On the threshold step with the gate open: carry the visitor through to the far side.</summary>
        void StepThrough()
        {
            if (Door == null || _crossed || (Door.Phase != PortalPhase.Open && Door.Phase != PortalPhase.Opening)) return;
            var head = Door.head != null ? Door.head : (Camera.main != null ? Camera.main.transform : null);
            if (head == null) return;
            var local = transform.InverseTransformPoint(head.position);
            float w = Mathf.Max(passageWidth, 1.2f) * 0.5f;
            if (local.z > -0.05f || local.z < -StepDepth - 0.1f || Mathf.Abs(local.x) > w) return;
            var origin = head.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            var body = origin.GetComponent<CharacterController>();
            bool had = body != null && body.enabled;
            if (had) body.enabled = false;
            var target = transform.TransformPoint(new Vector3(local.x, local.y, ArrivalPastDoor));
            origin.MoveCameraToWorldLocation(target);
            Physics.SyncTransforms();
            if (had) body.enabled = true;
        }

        void Update()
        {
            StepThrough();
            if (Door == null) { if (_crossed && !_arrived) { _arrived = true; Arrived?.Invoke(); } return; }
            if (!_crossed && Door.HasCrossed) { _crossed = true; Crossed?.Invoke(); }
            if (_crossed && !_arrived && Door.IsDone) { _arrived = true; Arrived?.Invoke(); }
        }

        /// <summary>A splat renderer for <paramref name="world"/> under <paramref name="parent"/>,
        /// with the shaders of a renderer already drawing (WorldCycler.CreateWorldObject's recipe).</summary>
        static GaussianSplatRenderer MakeRenderer(WorldDefinition world, GaussianSplatAsset asset, GaussianSplatRenderer like, Transform parent)
        {
            var go = new GameObject("World_" + world.key);
            go.transform.SetParent(parent, false);
            go.transform.localScale = world.SplatScale;
            var r = go.AddComponent<GaussianSplatRenderer>();
            r.m_Asset = asset;
            r.m_ShaderSplats = like.m_ShaderSplats; r.m_ShaderComposite = like.m_ShaderComposite;
            r.m_ShaderDebugPoints = like.m_ShaderDebugPoints; r.m_ShaderDebugBoxes = like.m_ShaderDebugBoxes;
            r.m_CSSplatUtilities = like.m_CSSplatUtilities;
            SplatRenderTuning.Apply(r);
            r.enabled = false; r.enabled = true;   // resources are built in OnEnable
            return r;
        }

        /// <summary>The keyhole outline and the way through, so the gate can be placed by eye in the Scene view.</summary>
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= 48; i++)
            {
                float a = 2f * Mathf.PI * i / 48f;
                var p = new Vector3(Mathf.Cos(a) * radius, centreHeight + Mathf.Sin(a) * radius, 0f);
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
            float hw = passageWidth * 0.5f;
            if (hw > 0f)
            {
                Gizmos.DrawLine(new Vector3(-hw, 0f, 0f), new Vector3(hw, 0f, 0f));
                Gizmos.DrawLine(new Vector3(-hw, 0f, 0f), new Vector3(-hw, centreHeight, 0f));
                Gizmos.DrawLine(new Vector3(hw, 0f, 0f), new Vector3(hw, centreHeight, 0f));
            }
            Gizmos.color = Color.cyan;   // out through the gate, into the next world
            Gizmos.DrawLine(new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.05f, ArrivalPastDoor));
        }

        static WorldDefinition Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var w in WorldCatalog.Small) if (w.key == key) return w;
            return null;
        }

        /// <summary>
        /// The keyhole as a mask mesh in the portal's unit quad (-0.5..0.5 in XY, scaled by the
        /// aperture: width 2r, height centre + r, origin at mid-height): a disc of radius
        /// <paramref name="r"/> centred <paramref name="centre"/> metres up, on a passage
        /// <paramref name="passage"/> wide down to the floor.
        /// </summary>
        public static Mesh KeyholeMesh(float r, float centre, float passage)
        {
            float w = 2f * r, h = centre + r;
            Vector3 U(float x, float y) => new Vector3(x / w, y / h - 0.5f, 0f);   // metres -> unit quad
            var v = new List<Vector3>(); var tris = new List<int>();
            const int n = 48;
            v.Add(U(0f, centre));
            for (var i = 0; i <= n; i++) { var a = 2f * Mathf.PI * i / n; v.Add(U(Mathf.Cos(a) * r, centre + Mathf.Sin(a) * r)); }
            for (var i = 1; i <= n; i++) { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
            int q = v.Count;
            float hw = Mathf.Min(passage, w) * 0.5f;
            v.Add(U(-hw, 0f)); v.Add(U(hw, 0f)); v.Add(U(hw, centre)); v.Add(U(-hw, centre));
            tris.AddRange(new[] { q, q + 2, q + 1, q, q + 3, q + 2 });
            var m = new Mesh { name = "Moon Gate Keyhole" };
            m.SetVertices(v); m.SetTriangles(tris, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>True when a point (metres: x from the gate's axis, y up from the floor) is in the keyhole.</summary>
        public static bool InKeyhole(float x, float y, float r, float centre, float passage)
        {
            var dy = y - centre;
            return x * x + dy * dy <= r * r || (Mathf.Abs(x) <= passage * 0.5f && y >= 0f && y <= centre);
        }
    }
}
