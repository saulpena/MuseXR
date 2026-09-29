using GaussianSplatting.Runtime;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Walk into a painting (Skylar: "tear open a painting and physically step into it"; "grab an
    /// artifact, scale it up, and enter it"). The painting is an ordinary grabbable canvas
    /// (<c>MusePico.Grab.Grabbable</c>): take it off the wall, pull it between two hands until it is
    /// taller than you, hold it upright, and the canvas tears open onto the world it depicts. Walk
    /// through and that world is where you stand; the tear closes behind you and the room you came
    /// from, the painting with it, is gone.
    ///
    /// The world inside follows the painting while it is carried — its position on the floor and its
    /// heading, never its tilt or its scale (<see cref="PaintingPortalRules.FollowPose"/>) — so
    /// stepping in always lands the visitor at that world's spawn, upright, however the canvas was
    /// held. It stops following the moment the visitor crosses.
    ///
    /// Everything the next world carries along (the one after it, its triggers) should be a child
    /// of <see cref="nextWorldRig"/>, so it moves with it.
    /// </summary>
    public sealed class PaintingPortal : MonoBehaviour, ITransitionStep
    {
        [Header("Painting")]
        [Tooltip("The whole painting (the grabbable root: canvas, back, frame). Hidden until the visitor " +
                 "has crossed into the world it hangs in — it is a mesh, so it would otherwise stand in the " +
                 "worlds before that too — and destroyed at the end.")]
        public GameObject paintingRoot;
        [Tooltip("The canvas: a Unity Quad, picture on its -Z side. Its size is the tear's.")]
        public Transform canvas;
        [Tooltip("What the tear removes and the far side must not see: the picture and the canvas back. " +
                 "Not the frame, which stays round the opening.")]
        public Renderer[] surfaces;

        [Header("Worlds")]
        [Tooltip("The world the painting hangs in. Destroyed once the tear has closed behind the visitor.")]
        public GaussianSplatRenderer currentWorld;
        [Tooltip("The world the painting depicts.")]
        public GaussianSplatRenderer nextWorld;
        [Tooltip("Moved to follow the painting; the next world (and anything travelling with it) sits under it.")]
        public Transform nextWorldRig;
        [Tooltip("Wait for this step to finish first. Optional.")]
        public MonoBehaviour after;

        [Header("When it may open")]
        public float minHeight = 1.8f;
        [Range(0f, 90f)] public float maxTiltDegrees = 25f;
        public float maxViewerDistance = 5f;

        [Header("Following")]
        [Tooltip("How far past the canvas the next world's spawn stands, metres.")]
        public float behind = 0.4f;
        public float floorY;
        [Tooltip("Higher follows the hand more tightly; lower smooths out its jitter.")]
        public float followSharpness = 6f;

        [Header("Timing")]
        public float tearSeconds = 3f;
        public float healSeconds = 1.5f;
        public float closeSeconds = 2f;

        [Header("Mask")]
        [Tooltip("Hidden/Gaussian Splatting/Portal Mask. Copied at runtime; the asset is not modified.")]
        public Material maskMaterial;
        [Tooltip("Unity's Quad mesh (1 x 1 in XY).")]
        public Mesh maskMesh;

        [Tooltip("The eye. Defaults to Camera.main.")]
        public Transform head;

        readonly TearSequence _tear = new();
        Material _mask;
        Vector3 _prevEyeDoor;
        bool _hasPrevEye;
        bool _placed;
        bool _canvasHidden;
        int _sortNthBefore = 4;

        static readonly int DoorHalf = Shader.PropertyToID("_DoorHalf");
        static readonly int MaskHalf = Shader.PropertyToID("_MaskHalf");
        static readonly int DoorOn = Shader.PropertyToID("_DoorOn");
        static readonly int Seep = Shader.PropertyToID("_Seep");
        static readonly int SeepRadius = Shader.PropertyToID("_SeepRadius");
        static readonly int ClipSeepToDoor = Shader.PropertyToID("_ClipSeepToDoor");

        public TearSequence Sequence => _tear;
        public bool IsDone => _tear.Phase == TearPhase.Done;
        public bool HasCrossed => _tear.Phase != TearPhase.Waiting;

        bool Ready => after == null || !(after is ITransitionStep step) || step.IsDone;
        bool AfterCrossed => after == null || !(after is ITransitionStep step) || step.HasCrossed;
        bool _paintingShown;

        void OnEnable()
        {
            _tear.TearSeconds = tearSeconds;
            _tear.HealSeconds = healSeconds;
            _tear.CloseSeconds = closeSeconds;
            _mask = maskMaterial != null ? new Material(maskMaterial) { name = "Painting Mask (runtime)" } : null;
            if (nextWorld != null)
            {
                nextWorld.m_DrawLimit = 0;                 // loaded, not drawn, until the canvas tears
                _sortNthBefore = nextWorld.m_SortNthFrame;
            }
            _paintingShown = AfterCrossed;
            if (paintingRoot != null) paintingRoot.SetActive(_paintingShown);
        }

        void OnDestroy()
        {
            if (_mask != null) Destroy(_mask);
        }

        void LateUpdate()
        {
            // The painting belongs to the world it hangs in: it appears once the visitor is there.
            if (!_paintingShown && AfterCrossed)
            {
                _paintingShown = true;
                if (paintingRoot != null) paintingRoot.SetActive(true);
            }
            if (_tear.Phase == TearPhase.Done || !Ready || canvas == null) return;
            if (head == null) { var cam = Camera.main; if (cam == null) return; head = cam.transform; }

            var size = new Vector2(Mathf.Abs(canvas.lossyScale.x), Mathf.Abs(canvas.lossyScale.y));
            var half = size * 0.5f;
            bool crossed = _tear.Phase != TearPhase.Waiting;

            // The aperture sits a centimetre in front of the paint, so the canvas itself lies beyond
            // it and is erased where the tear is.
            var doorToWorld = Matrix4x4.TRS(canvas.position - canvas.forward * 0.01f, canvas.rotation, Vector3.one);
            var worldToDoor = doorToWorld.inverse;
            var eyeDoor = worldToDoor.MultiplyPoint3x4(head.position);
            if (!_hasPrevEye) { _prevEyeDoor = eyeDoor; _hasPrevEye = true; }

            bool mayOpen = !crossed && PaintingPortalRules.MayOpen(head.position, canvas.position, canvas.forward,
                canvas.up, size.y, minHeight, maxTiltDegrees, maxViewerDistance);

            if (!crossed) Follow();

            switch (_tear.Step(Time.deltaTime, mayOpen, _prevEyeDoor, eyeDoor, half))
            {
                case TearEvent.Crossed:
                    if (nextWorld != null)
                    {
                        nextWorld.m_PortalRole = SplatPortalRole.Outside;
                        nextWorld.m_SortNthFrame = _sortNthBefore;
                    }
                    if (currentWorld != null) currentWorld.m_PortalRole = SplatPortalRole.Through;
                    SplatPortal.EraseMeshes = false;
                    SetCanvasHidden(true);
                    break;

                case TearEvent.Closed:
                    Finish();
                    return;
            }
            crossed = _tear.Phase != TearPhase.Waiting;   // this very frame may have been the crossing

            bool showing = _tear.Tear > 0f || _tear.Phase == TearPhase.Closing;
            if (showing)
            {
                SplatPortal.MaskMaterial = _mask;
                SplatPortal.MaskMesh = maskMesh;
                SplatPortal.DoorToWorld = doorToWorld;
                SplatPortal.WorldToDoor = worldToDoor;
                SplatPortal.HalfSize = half;
                SplatPortal.MaskHalfSize = half;
                SplatPortal.CullMargin = 0.5f;
                if (!crossed)
                {
                    SplatPortal.EraseMeshes = true;
                    if (currentWorld != null) currentWorld.m_PortalRole = SplatPortalRole.Outside;
                    if (nextWorld != null)
                    {
                        nextWorld.m_PortalRole = SplatPortalRole.Through;
                        nextWorld.m_DrawLimit = -1;
                        nextWorld.m_SortNthFrame = 1;   // it moves with the painting: keep its order fresh
                    }
                    // Stepping into the canvas before the crossing registers: the paint would fill
                    // the view for a frame (the erase cannot follow the eye into the plane).
                    SetCanvasHidden(PortalGeometry.InCrossingZone(eyeDoor, half, 0.3f));
                }
                if (_mask != null)
                {
                    _mask.SetVector(DoorHalf, half);
                    _mask.SetVector(MaskHalf, half);
                    _mask.SetFloat(DoorOn, 0f);
                    _mask.SetFloat(ClipSeepToDoor, 1f);
                    _mask.SetFloat(SeepRadius, half.magnitude * 1.15f);
                    _mask.SetFloat(Seep, PortalSequence.Ease(_tear.Tear));
                }
            }
            else if (nextWorld != null)
            {
                nextWorld.m_DrawLimit = 0;                  // healed: stop paying for it
            }
            SplatPortal.Active = showing;

            _prevEyeDoor = eyeDoor;
        }

        void Follow()
        {
            if (nextWorldRig == null) return;
            if (!PaintingPortalRules.Heading(canvas.forward, out _)) return;   // lying flat: keep the last pose
            PaintingPortalRules.FollowPose(canvas.position, canvas.forward, behind, floorY, out var pos, out var yaw);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (!_placed)
            {
                nextWorldRig.SetPositionAndRotation(pos, rot);
                _placed = true;
                return;
            }
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            nextWorldRig.SetPositionAndRotation(
                Vector3.Lerp(nextWorldRig.position, pos, k),
                Quaternion.Slerp(nextWorldRig.rotation, rot, k));
        }

        void SetCanvasHidden(bool hidden)
        {
            if (hidden == _canvasHidden) return;
            _canvasHidden = hidden;
            if (surfaces != null && surfaces.Length > 0)
                foreach (var r in surfaces) { if (r != null) r.enabled = !hidden; }
            else if (canvas != null)
                foreach (var r in canvas.GetComponentsInChildren<Renderer>(true)) r.enabled = !hidden;
        }

        void Finish()
        {
            SplatPortal.Active = false;
            SplatPortal.EraseMeshes = false;
            if (nextWorld != null) nextWorld.m_PortalRole = SplatPortalRole.None;
            if (currentWorld != null) { Destroy(currentWorld.gameObject); currentWorld = null; }
            var painting = paintingRoot != null ? paintingRoot : canvas != null ? canvas.gameObject : null;
            if (painting != null) Destroy(painting);
            canvas = null;
        }
    }
}
