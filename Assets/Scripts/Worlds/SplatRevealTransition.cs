using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseXR.Worlds
{
    /// <summary>
    /// The door-less transition from one splat world to the next, built for Skylar's Van Gogh to
    /// Monet ("the reflection of the stars gradually becomes water, Van Gogh's brushstrokes slowly
    /// transform into Monet's visual language, night becomes morning").
    ///
    ///   1. The visitor stands in the trigger area and looks up (at the starry ceiling).
    ///   2. Night falls on the room: it darkens to blue while its brightest splats — the stars, the
    ///      lamps — keep shining.
    ///   3. The next world rises out of the floor on a ragged front (see <see cref="SplatReveal"/>):
    ///      below it the new world forms, above it the old one dissolves; either side of the front
    ///      splats swell into soft dabs as they fade in and out. The music crosses over.
    ///   4. The new world starts in dawn light and settles into day. The old world is destroyed.
    ///
    /// The next world is placed so its playtested spawn is the trigger area: the visitor does not
    /// move, the world does. It must be loaded before this starts (it is held undrawn until then).
    /// </summary>
    public sealed class SplatRevealTransition : MonoBehaviour
    {
        [Header("Worlds")]
        public GaussianSplatRenderer leavingWorld;
        public GaussianSplatRenderer arrivingWorld;
        [Tooltip("Wait for this door to finish first (it is how the visitor got here). Optional.")]
        public SplatPortalDoor after;

        [Header("Trigger")]
        [Tooltip("Centre of the area the visitor must stand in. Defaults to this transform.")]
        public Transform triggerCentre;
        public float triggerRadius = 4f;
        [Tooltip("Looking up means the head's forward has at least this much upward component.")]
        [Range(0f, 1f)] public float lookUpMin = 0.45f;

        [Header("Front")]
        public float frontFrom = -0.5f;
        public float frontTo = 35f;
        public float band = 1.4f;
        public float noiseAmplitude = 0.9f;
        public float noiseScale = 1.6f;
        public float dabGrow = 2.5f;

        [Header("Timing")]
        public float duskSeconds = 4f;
        public float riseSeconds = 12f;
        public float settleSeconds = 4f;

        [Header("Light")]
        public Color nightTint = new(0.22f, 0.28f, 0.6f);
        [Range(0f, 1f)] public float starsSpared = 0.9f;
        public Color dawnTint = new(1f, 0.72f, 0.6f);

        [Header("Music")]
        public AudioSource currentMusic;
        public AudioSource nextMusic;
        [Range(0f, 1f)] public float musicVolume = 0.32f;

        [Tooltip("The eye. Defaults to Camera.main.")]
        public Transform head;

        readonly RevealSequence _sequence = new();
        public RevealSequence Sequence => _sequence;

        void OnEnable()
        {
            _sequence.DuskSeconds = duskSeconds;
            _sequence.RiseSeconds = riseSeconds;
            _sequence.SettleSeconds = settleSeconds;
            _sequence.FrontFrom = frontFrom;
            _sequence.FrontTo = frontTo;
            if (arrivingWorld != null) arrivingWorld.m_DrawLimit = 0;   // loaded, not drawn
            if (nextMusic != null) { nextMusic.volume = 0f; if (!nextMusic.isPlaying) nextMusic.Play(); }
        }

        void OnDisable()
        {
            SplatReveal.Active = false;
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame && Ready)
                _sequence.Request();
        }

        bool Ready => after == null || after.Phase == PortalPhase.Done;

        void LateUpdate()
        {
            if (_sequence.Phase == RevealPhase.Done || !Ready) return;
            if (head == null) { var cam = Camera.main; if (cam == null) return; head = cam.transform; }

            var centre = triggerCentre != null ? triggerCentre.position : transform.position;
            var offset = head.position - centre;
            bool inPlace = new Vector2(offset.x, offset.z).magnitude <= triggerRadius;
            bool lookingUp = head.forward.y >= lookUpMin;

            switch (_sequence.Step(Time.deltaTime, inPlace, lookingUp))
            {
                case RevealEvent.StartedRising:
                    if (arrivingWorld != null)
                    {
                        arrivingWorld.m_DrawLimit = -1;
                        arrivingWorld.m_RevealRole = SplatRevealRole.Arriving;
                    }
                    if (leavingWorld != null)
                    {
                        leavingWorld.m_RevealRole = SplatRevealRole.Leaving;
                        leavingWorld.m_RenderOrder = 1;   // the old world draws first: in front in the band
                    }
                    SplatReveal.Active = true;
                    break;

                case RevealEvent.FrontPassed:
                    SplatReveal.Active = false;
                    if (arrivingWorld != null) arrivingWorld.m_RevealRole = SplatRevealRole.None;
                    if (leavingWorld != null) { Destroy(leavingWorld.gameObject); leavingWorld = null; }
                    if (currentMusic != null) currentMusic.Stop();
                    break;
            }

            PushFront();
            ApplyLight();
        }

        void PushFront()
        {
            SplatReveal.Normal = Vector3.up;
            SplatReveal.Front = _sequence.Front;
            SplatReveal.Band = band;
            SplatReveal.NoiseAmplitude = noiseAmplitude;
            SplatReveal.NoiseScale = noiseScale;
            SplatReveal.DabGrow = dabGrow;
        }

        void ApplyLight()
        {
            float night = PortalSequence.Ease(_sequence.Night);
            if (leavingWorld != null)
            {
                leavingWorld.m_Tint = Color.Lerp(Color.white, nightTint, night);
                leavingWorld.m_TintSparesBright = starsSpared * night;
            }
            if (arrivingWorld != null)
            {
                arrivingWorld.m_Tint = Color.Lerp(Color.white, dawnTint, PortalSequence.Ease(_sequence.Dawn));
                arrivingWorld.m_TintSparesBright = 0f;
            }
            float blend = _sequence.MusicBlend;
            if (currentMusic != null && currentMusic.isPlaying) currentMusic.volume = musicVolume * (1f - blend);
            if (nextMusic != null) nextMusic.volume = musicVolume * blend;
        }
    }
}
