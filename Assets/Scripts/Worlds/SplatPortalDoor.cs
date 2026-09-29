using System.Collections.Generic;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseXR.Worlds
{
    /// <summary>
    /// A one-way door from the splat world the visitor is in to the next one, set into something
    /// in the first world (in the test scene: the back of the Buddha). The next world stands where
    /// it will be walked into, behind the door, and is drawn only through the door's shape (see
    /// <see cref="SplatPortal"/>).
    ///
    /// The sequence, all driven by <see cref="PortalSequence"/>:
    ///   1. Nothing is there until the visitor comes round and looks toward the door's place.
    ///   2. The door materialises: it rises out of the floor glowing gold, and settles.
    ///   3. It opens toward the visitor; the first world's music fades out and the next one's in;
    ///      the next world seeps out round the doorway in a ragged, growing patch.
    ///   4. Walking through swaps the worlds; the door shuts behind the visitor; once shut the
    ///      previous world and its props are destroyed and the door disappears. No way back.
    ///
    /// Solid geometry the door is set into (the Buddha) is erased inside the shape by the splat
    /// feature, so it needs no special material; it is hidden outright once the visitor crosses,
    /// because from inside the next world it would stand in the middle of it.
    ///
    /// The aperture transform defines door space: origin at the centre of the opening, +Z INTO the
    /// next world. Everything of the door itself must stay at door-space z &lt; 0 (on the
    /// visitor's side), or it would be erased with the geometry behind it.
    /// </summary>
    public sealed class SplatPortalDoor : MonoBehaviour, ITransitionStep
    {
        public bool IsDone => _sequence.Phase == PortalPhase.Done;
        public bool HasCrossed => _sequence.Phase >= PortalPhase.Closing;

        [Header("Worlds")]
        [Tooltip("The world the visitor starts in. Destroyed once the door has shut behind them.")]
        public GaussianSplatRenderer currentWorld;
        [Tooltip("Meshes that belong to the current world (the Buddha). Hidden on crossing, destroyed with it.")]
        public GameObject[] currentWorldProps;
        [Tooltip("The world beyond the door. Set Progressive Upload on it so its door-visible splats go up first.")]
        public GaussianSplatRenderer nextWorld;

        [Header("Door")]
        [Tooltip("Centre of the opening; +Z points into the next world.")]
        public Transform aperture;
        [Tooltip("Width and height of the opening, metres.")]
        public Vector2 apertureSize = new(2.4f, 3.6f);
        public Transform leftLeaf;
        public Transform rightLeaf;
        [Range(0f, 130f)] public float openAngle = 100f;
        [Tooltip("Everything that appears and disappears with the door (frame, leaves). Pivot at floor level.")]
        public Transform doorVisual;
        [Tooltip("Colour of the light the door materialises in.")]
        [ColorUsage(false, true)] public Color appearGlow = new(2.2f, 1.5f, 0.35f);
        [Tooltip("A little light of their own, so the door does not read as a dark hole among unlit splats.")]
        [Range(0f, 1f)] public float selfIllumination = 0.25f;

        [Header("Next world seeping out")]
        [Tooltip("Radius of the seep patch at its fullest, metres from the door's centre.")]
        public float seepRadius = 2.8f;

        [Header("Timing")]
        public float appearSeconds = 2.5f;
        public float openSeconds = 3.5f;
        public float closeSeconds = 2.5f;
        public float seepSeconds = 6f;
        [Tooltip("The door appears when the visitor is this close, in front of it, looking at it.")]
        public float triggerDistance = 7f;
        [Tooltip("How far off the door's centre the visitor may look and still count as looking at it.")]
        public float lookDegrees = 35f;

        [Header("Music")]
        public AudioSource currentMusic;
        public AudioSource nextMusic;
        [Range(0f, 1f)] public float musicVolume = 0.32f;

        [Header("Mask")]
        [Tooltip("Hidden/Gaussian Splatting/Portal Mask. Copied at runtime; the asset is not modified.")]
        public Material maskMaterial;
        [Tooltip("Unity's Quad mesh (1 x 1 in XY).")]
        public Mesh maskMesh;

        [Tooltip("The eye. Defaults to Camera.main.")]
        public Transform head;

        readonly PortalSequence _sequence = new();
        readonly List<Material> _doorMaterials = new();
        readonly List<Color> _doorBaseColors = new();
        Material _mask;
        Vector3 _prevEyeDoor;
        bool _hasPrevEye;
        bool _propsHidden;

        static readonly int DoorHalf = Shader.PropertyToID("_DoorHalf");
        static readonly int MaskHalf = Shader.PropertyToID("_MaskHalf");
        static readonly int DoorOn = Shader.PropertyToID("_DoorOn");
        static readonly int Seep = Shader.PropertyToID("_Seep");
        static readonly int SeepRadius = Shader.PropertyToID("_SeepRadius");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public PortalPhase Phase => _sequence.Phase;
        public PortalSequence Sequence => _sequence;

        void OnEnable()
        {
            _sequence.AppearSeconds = appearSeconds;
            _sequence.OpenSeconds = openSeconds;
            _sequence.CloseSeconds = closeSeconds;
            _sequence.SeepSeconds = seepSeconds;
            _sequence.TriggerDistance = triggerDistance;

            _mask = maskMaterial != null ? new Material(maskMaterial) { name = "Portal Mask (runtime)" } : null;
            SplatPortal.MaskMaterial = _mask;
            SplatPortal.MaskMesh = maskMesh;
            SplatPortal.MaskHalfSize = apertureSize * 0.5f + new Vector2(seepRadius, seepRadius);
            // Splats of the next world are culled whole beyond this distance outside the door: it
            // has to reach the edge of the seep patch.
            SplatPortal.CullMargin = seepRadius + 0.5f;
            SplatPortal.EraseMeshes = true;

            if (currentWorld != null) currentWorld.m_PortalRole = SplatPortalRole.Outside;
            if (nextWorld != null)
            {
                nextWorld.m_PortalRole = SplatPortalRole.Through;
                nextWorld.m_DrawLimit = 0;          // loaded, not drawn, until the door opens
            }

            PrepareDoorMaterials();
            if (doorVisual != null) doorVisual.gameObject.SetActive(false);

            if (currentMusic != null) { currentMusic.volume = musicVolume; if (!currentMusic.isPlaying) currentMusic.Play(); }
            if (nextMusic != null) { nextMusic.volume = 0f; if (!nextMusic.isPlaying) nextMusic.Play(); }

            PushDoorToPortal();
            ApplyVisuals();
            SplatPortal.Active = true;
        }

        void OnDisable()
        {
            SplatPortal.Active = false;
            SplatPortal.EraseMeshes = false;
        }

        void OnDestroy()
        {
            if (_mask != null) Destroy(_mask);
            foreach (var m in _doorMaterials) if (m != null) Destroy(m);
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.oKey.wasPressedThisFrame)
                _sequence.RequestOpen();
        }

        void LateUpdate()
        {
            if (_sequence.Phase == PortalPhase.Done) return;
            if (head == null) { var cam = Camera.main; if (cam == null) return; head = cam.transform; }

            PushDoorToPortal();
            var eyeDoor = SplatPortal.ToDoor(head.position);
            var forwardDoor = SplatPortal.WorldToDoor.MultiplyVector(head.forward);
            if (!_hasPrevEye) { _prevEyeDoor = eyeDoor; _hasPrevEye = true; }

            bool looking = PortalSequence.IsLookingAt(eyeDoor, forwardDoor, lookDegrees);
            switch (_sequence.Step(Time.deltaTime, _prevEyeDoor, eyeDoor, apertureSize * 0.5f, looking))
            {
                case PortalEvent.StartedAppearing:
                    if (doorVisual != null) doorVisual.gameObject.SetActive(true);
                    break;

                case PortalEvent.StartedOpening:
                    // The cheap preview: only the splats baked as seen-through-the-door.
                    if (nextWorld != null)
                        nextWorld.m_DrawLimit = nextWorld.asset != null && nextWorld.asset.priorityCount > 0
                            ? nextWorld.asset.priorityCount : -1;
                    break;

                case PortalEvent.Crossed:
                    // The visitor now stands in the next world; the old one is what the door shows.
                    if (nextWorld != null)
                    {
                        nextWorld.m_DrawLimit = -1;
                        nextWorld.m_PortalRole = SplatPortalRole.Outside;
                    }
                    if (currentWorld != null) currentWorld.m_PortalRole = SplatPortalRole.Through;
                    SplatPortal.EraseMeshes = false;   // beyond the door now is the door's own side
                    SetPropsHidden(true);
                    break;

                case PortalEvent.Closed:
                    Finish();
                    return;
            }

            // Stepping into the doorway before the crossing registers: the erase cannot follow the
            // eye into the plane, so the Buddha would fill the view for a frame. Hide it there.
            if (_sequence.Phase is PortalPhase.Opening or PortalPhase.Open)
                SetPropsHidden(PortalGeometry.InCrossingZone(eyeDoor, apertureSize * 0.5f, 0.4f));

            _prevEyeDoor = eyeDoor;
            ApplyVisuals();
        }

        void Finish()
        {
            SplatPortal.Active = false;
            SplatPortal.EraseMeshes = false;
            if (nextWorld != null) nextWorld.m_PortalRole = SplatPortalRole.None;
            if (currentWorld != null)
            {
                Destroy(currentWorld.gameObject);   // releases its GPU buffers
                currentWorld = null;
            }
            if (currentWorldProps != null)
                foreach (var p in currentWorldProps) if (p != null) Destroy(p);
            currentWorldProps = null;
            if (currentMusic != null) currentMusic.Stop();
            if (nextMusic != null) nextMusic.volume = musicVolume;
            if (doorVisual != null) doorVisual.gameObject.SetActive(false);
        }

        void SetPropsHidden(bool hidden)
        {
            if (hidden == _propsHidden || currentWorldProps == null) return;
            _propsHidden = hidden;
            foreach (var p in currentWorldProps) if (p != null) p.SetActive(!hidden);
        }

        void PushDoorToPortal()
        {
            var door = aperture != null ? aperture : transform;
            var doorToWorld = Matrix4x4.TRS(door.position, door.rotation, Vector3.one);
            SplatPortal.DoorToWorld = doorToWorld;
            SplatPortal.WorldToDoor = doorToWorld.inverse;
            SplatPortal.HalfSize = apertureSize * 0.5f;
        }

        void PrepareDoorMaterials()
        {
            if (doorVisual == null || _doorMaterials.Count > 0) return;
            // One runtime copy per distinct material, so the project's materials are never edited.
            var copies = new Dictionary<Material, Material>();
            foreach (var r in doorVisual.GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; ++i)
                {
                    if (shared[i] == null) continue;
                    if (!copies.TryGetValue(shared[i], out var copy))
                    {
                        copy = new Material(shared[i]) { name = shared[i].name + " (portal)" };
                        copy.EnableKeyword("_EMISSION");
                        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        copies[shared[i]] = copy;
                        _doorMaterials.Add(copy);
                        _doorBaseColors.Add(copy.HasProperty(BaseColor) ? copy.GetColor(BaseColor) : Color.white);
                    }
                    shared[i] = copy;
                }
                r.sharedMaterials = shared;
            }
        }

        void ApplyVisuals()
        {
            float appear = PortalSequence.Ease(_sequence.Appear);
            float opening = PortalSequence.Ease(_sequence.Opening);

            // Rises out of the floor (the floor's splats, nearer the eye, hide the part still below
            // ground), and the glow it arrives in dies down as it settles.
            if (doorVisual != null)
                doorVisual.localPosition = new Vector3(0f, -(apertureSize.y + 0.6f) * (1f - appear), 0f);
            float glow = 1f - appear;
            for (int i = 0; i < _doorMaterials.Count; ++i)
                _doorMaterials[i].SetColor(EmissionColor, _doorBaseColors[i] * selfIllumination + appearGlow * (glow * glow));

            // Toward the visitor (-Z): the left leaf, hinged on -X, turns positively about Y.
            float angle = openAngle * opening;
            if (leftLeaf != null) leftLeaf.localRotation = Quaternion.Euler(0f, angle, 0f);
            if (rightLeaf != null) rightLeaf.localRotation = Quaternion.Euler(0f, -angle, 0f);

            if (_mask != null)
            {
                var half = apertureSize * 0.5f;
                _mask.SetVector(DoorHalf, half);
                _mask.SetVector(MaskHalf, SplatPortal.MaskHalfSize);
                _mask.SetFloat(DoorOn, _sequence.Phase >= PortalPhase.Opening ? 1f : 0f);
                _mask.SetFloat(Seep, PortalSequence.Ease(_sequence.Seep));
                _mask.SetFloat(SeepRadius, seepRadius);
            }

            float blend = _sequence.MusicBlend;
            if (currentMusic != null) currentMusic.volume = musicVolume * (1f - blend);
            if (nextMusic != null) nextMusic.volume = musicVolume * blend;
        }
    }
}
