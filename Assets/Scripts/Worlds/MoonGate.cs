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

        [Tooltip("Optional: the gate's own frame (a model, kept inactive). It rises out of the floor when the gate opens. Leave empty when the capture's own ring is the frame.")]
        public Transform frame;

        /// <summary>How long the frame takes to rise, and the next world to open in it, when shown at once.</summary>
        public const float RiseSeconds = 2.2f;

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
        /// <summary>How far risen the gate is before walking through it counts (0..1).</summary>
        public const float PassableOpening = 0.35f;
        /// <summary>The door shutting behind the visitor; the next chapter wakes when it has.</summary>
        public const float CloseBehindSeconds = 0.4f;

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
        public bool Open(GaussianSplatRenderer currentWorld, IEnumerable<GameObject> currentWorldProps, Transform head = null, bool showNow = false)
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
            CutArrivalFloaters(pose, pivot);

            var go = Instantiate(doorPrefab);
            go.name = "Moon Gate Door to " + NextDefinition.displayName;
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            Door = go.GetComponent<SplatPortalDoor>();
            Door.currentWorld = currentWorld;
            Door.nextWorld = NextWorld;
            Door.currentWorldProps = currentWorldProps != null ? new List<GameObject>(currentWorldProps).ToArray() : new GameObject[0];
            Door.triggerDistance = TriggerDistance;
            float top = centreHeight + radius;
            Door.apertureSize = new Vector2(Mathf.Max(2f * radius, passageWidth), top);
            if (Door.aperture != null) Door.aperture.localPosition = new Vector3(0f, top * 0.5f, 0f);
            _mask = KeyholeMesh(radius, centreHeight, passageWidth);
            Door.maskMesh = _mask;
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
            // Saul, 4 Oct: through the moment it opens, and the next chapter ready at once. Walkable when
            // a third risen (it was 60%); the door shuts behind in 0.4 s, not 2.5, since the next chapter
            // wakes only once it has (ChapterLink) - and there is no way back to look at anyway.
            // Set after enabling: SplatPortalDoor.OnEnable copies its own timings into the sequence.
            Door.Sequence.PassableOpening = PassableOpening;
            Door.Sequence.CloseSeconds = CloseBehindSeconds;
            BuildPads();
            if (frame != null) { frame.gameObject.SetActive(true); _rise = 0f; Rise(); }
            if (showNow)
            {
                // Saul: a gate shows its next world at once - no look-to-trigger, no appear step. Set
                // after enabling: SplatPortalDoor.OnEnable copies its own timings into the sequence.
                Door.Sequence.AppearSeconds = 0.01f;
                Door.Sequence.OpenSeconds = RiseSeconds;
                Door.Sequence.RequestOpen();
            }
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
        [Tooltip("Carry a visitor through when they stand on the threshold step (for teleport locomotion only).")]
        public bool stepCarriesThrough = false;

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

        float _rise = -1f;
        Mesh _mask;

        /// <summary>
        /// The frame comes up out of the floor while the gate opens, and the opening the next world
        /// shows through comes up WITH it: the keyhole is shifted down by the frame's depth below the
        /// floor and cut off at floor level, so the world is only ever seen inside the part of the arch
        /// already above ground. (A fixed opening showed the next world floating where the arch would
        /// be, over a frame still rising - Saul, 3 Oct 2026.)
        /// </summary>
        void Rise()
        {
            if (frame == null || _rise < 0f) return;
            _rise = Mathf.Min(1f, _rise + Time.deltaTime / RiseSeconds);
            float e = 1f - (1f - _rise) * (1f - _rise);
            float below = (centreHeight + radius + 0.3f) * (1f - e);
            var p = frame.localPosition;
            frame.localPosition = new Vector3(p.x, -below, p.z);
            if (_mask != null) FillKeyhole(_mask, radius, centreHeight, passageWidth, below);
            if (_rise >= 1f) _rise = -1f;
        }

        void Update()
        {
            Rise();
            // Smooth locomotion now (Saul, 3 Oct): walking through the plane is the crossing, so the step
            // no longer carries anyone - it jumped visitors the last metre as they walked up.
            if (stepCarriesThrough) StepThrough();
            if (Door == null) { if (_crossed && !_arrived) { _arrived = true; Arrived?.Invoke(); } return; }
            if (!_crossed && Door.HasCrossed) { _crossed = true; Crossed?.Invoke(); }
            if (_crossed && !_arrived && Door.IsDone) { _arrived = true; Arrived?.Invoke(); }
        }

        /// <summary>
        /// A capture has big blurred floaters round its spawn: invisible from the spawn, but seen
        /// through the gate they sit right in the opening (the Gate walk saw the Palace's as a
        /// red-brown smear over the lower two thirds of the moon gate; musexr-bb, 818b02d). An inverted
        /// box from the gate plane to just past the arrival, above the floor, removes them. Parented to
        /// the next world's frame, so it moves with it.
        /// </summary>
        void CutArrivalFloaters(DoorPose pose, Transform frame)
        {
            if (NextWorld == null) return;
            var cut = new GameObject("Arrival floaters (splat cutout)").AddComponent<GaussianCutout>();
            cut.transform.SetParent(frame, true);
            var half = new Vector3(Mathf.Max(radius, passageWidth * 0.5f) + 0.5f, 1.6f, 0.85f);   // the box spans -1..1 locally
            cut.transform.SetPositionAndRotation(pose.position + pose.rotation * Vector3.forward * 0.8f + Vector3.up * (0.12f + half.y), pose.rotation);
            cut.transform.localScale = half;
            cut.m_Type = GaussianCutout.Type.Box;
            cut.m_Invert = true;   // inside the box is removed, everything else stays
            var list = new List<GaussianCutout>();
            if (NextWorld.m_Cutouts != null) list.AddRange(NextWorld.m_Cutouts);
            list.Add(cut);
            NextWorld.m_Cutouts = list.ToArray();
            // Put them back once the visitor has arrived: the cut is for the view THROUGH the gate. Left in,
            // it was a hole in the world right behind anyone who turned round (Saul, 4 Oct 2026).
            var world = NextWorld;
            Arrived += () => RestoreCut(world, cut);
        }

        /// <summary>Take <paramref name="cut"/> out of <paramref name="world"/>'s cutouts and destroy it.</summary>
        public static void RestoreCut(GaussianSplatRenderer world, GaussianCutout cut)
        {
            if (world != null && world.m_Cutouts != null)
            {
                var keep = new List<GaussianCutout>();
                foreach (var c in world.m_Cutouts) if (c != null && c != cut) keep.Add(c);
                world.m_Cutouts = keep.ToArray();
            }
            if (cut != null) Destroy(cut.gameObject);
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
            var m = new Mesh { name = "Moon Gate Keyhole" };
            m.MarkDynamic();
            FillKeyhole(m, r, centre, passage, 0f);
            return m;
        }

        /// <summary>
        /// The keyhole shifted down by <paramref name="below"/> metres and cut off at the floor (y 0):
        /// a disc and a passage, each a convex polygon clipped to y >= 0 and fanned.
        /// </summary>
        public static void FillKeyhole(Mesh m, float r, float centre, float passage, float below)
        {
            // radius 0: a plain rectangle, passage wide and centre tall (a side door with a flat lintel)
            float w = Mathf.Max(2f * r, passage), h = centre + r;
            Vector3 U(Vector2 p) => new Vector3(p.x / w, p.y / h - 0.5f, 0f);   // metres -> unit quad
            var v = new List<Vector3>(); var tris = new List<int>();
            void Add(List<Vector2> poly)
            {
                var c = ClipAboveFloor(poly);
                if (c.Count < 3) return;
                int b = v.Count;
                foreach (var p in c) v.Add(U(p));
                for (int i = 1; i < c.Count - 1; i++) { tris.Add(b); tris.Add(b + i + 1); tris.Add(b + i); }
            }
            const int n = 48;
            if (r > 0f)
            {
                var disc = new List<Vector2>();
                for (int i = 0; i < n; i++) { var a = 2f * Mathf.PI * i / n; disc.Add(new Vector2(Mathf.Cos(a) * r, centre - below + Mathf.Sin(a) * r)); }
                Add(disc);
            }
            float hw = Mathf.Min(passage, w) * 0.5f;
            if (hw > 0f)
                Add(new List<Vector2> { new Vector2(-hw, -below), new Vector2(hw, -below), new Vector2(hw, centre - below), new Vector2(-hw, centre - below) });
            m.Clear();
            m.SetVertices(v); m.SetTriangles(tris, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.01f));   // the unit quad, whatever is cut
        }

        /// <summary>A convex polygon clipped to y >= 0 (Sutherland-Hodgman, one edge).</summary>
        static List<Vector2> ClipAboveFloor(List<Vector2> poly)
        {
            var o = new List<Vector2>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                bool ain = a.y >= 0f, bin = b.y >= 0f;
                if (ain) o.Add(a);
                if (ain != bin) { float t = a.y / (a.y - b.y); o.Add(new Vector2(Mathf.Lerp(a.x, b.x, t), 0f)); }
            }
            return o;
        }

        /// <summary>True when a point (metres: x from the gate's axis, y up from the floor) is in the keyhole.</summary>
        public static bool InKeyhole(float x, float y, float r, float centre, float passage)
        {
            var dy = y - centre;
            return x * x + dy * dy <= r * r || (Mathf.Abs(x) <= passage * 0.5f && y >= 0f && y <= centre);
        }
    }
}
