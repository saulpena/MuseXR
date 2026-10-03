using System;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Shows a <see cref="TimeRing"/> in the scene: grades and hazes the splat world, sets mesh fog
    /// to match, lights the frames and drives the drifting particles. The ring mesh itself turns to
    /// the chosen detent. Keys 1/2/3 choose mist/afternoon/dusk and T turns to the next (Editor and
    /// the emulator's keyboard); the headset turns it through <see cref="Choose"/>.
    /// </summary>
    public sealed class TimeRingDriver : MonoBehaviour
    {
        [Tooltip("Splat worlds to grade. Empty = every GaussianSplatRenderer in the scene, found each time one is chosen.")]
        public GaussianSplatRenderer[] worlds = Array.Empty<GaussianSplatRenderer>();
        [Tooltip("OPTIONAL real lights on lit meshes. The hung artworks need none: MuseXR/Artwork fakes the lamp from globals this driver sets.")]
        public Light[] frameLights = Array.Empty<Light>();
        [Tooltip("The hung artworks and anything else unlit in the world. Tinted to the world's grade, since no light reaches them.")]
        public Renderer[] artworks = Array.Empty<Renderer>();
        [Tooltip("Mist motes, dusk dust. Emission scales with the look's particle amount.")]
        public ParticleSystem particles;
        public float particlesAtFull = 40f;
        [Tooltip("The ring mesh. Turns about its local Y to the chosen setting's detent.")]
        public Transform ringMesh;
        public float detentDegrees = 60f;
        [Tooltip("Also set Unity's scene fog. OFF by default: measured 3 Oct 2026, with RenderSettings.fog on, " +
                 "the meshes in a splat world (paintings, the ring) vanish from the frame entirely. The splat haze " +
                 "carries the mist on its own.")]
        public bool driveMeshFog;
        public bool keyboard = true;

        public TimeOfDay startAt = TimeOfDay.Afternoon;
        public float easeSeconds = TimeRing.DefaultEaseSeconds;

        /// <summary>Raised when the visitor turns the ring to a new setting.</summary>
        public event Action<TimeOfDay> Chosen;

        public TimeRing Ring { get; private set; }

        void Awake()
        {
            Ring = new TimeRing(startAt, easeSeconds);
            Apply(Ring.Current);
        }

        public void Choose(TimeOfDay time)
        {
            if (!Ring.Choose(time)) return;
            if (worlds.Length == 0) _found = null;
            Chosen?.Invoke(time);
        }

        void Update()
        {
            if (keyboard && Keyboard.current != null)
            {
                var k = Keyboard.current;
                if (k.digit1Key.wasPressedThisFrame) Choose(TimeOfDay.Mist);
                if (k.digit2Key.wasPressedThisFrame) Choose(TimeOfDay.Afternoon);
                if (k.digit3Key.wasPressedThisFrame) Choose(TimeOfDay.Dusk);
                if (k.tKey.wasPressedThisFrame) Choose(TimeRing.Next(Ring.Chosen));
            }
            if (Ring.IsEasing) Apply(Ring.Tick(Time.deltaTime));

            if (ringMesh != null)
            {
                float target = ((int)Ring.Chosen - 1) * detentDegrees;
                var goal = Quaternion.Euler(0f, target, 0f);
                ringMesh.localRotation = Quaternion.Slerp(ringMesh.localRotation, goal, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }
        }

        GaussianSplatRenderer[] _found;
        MaterialPropertyBlock _block;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int MuseFrameLight = Shader.PropertyToID("_MuseFrameLight");
        static readonly int MuseHaze = Shader.PropertyToID("_MuseHaze");

        void Apply(in TimeOfDayLook look)
        {
            var targets = worlds.Length > 0 ? worlds : (_found ??= FindObjectsByType<GaussianSplatRenderer>(FindObjectsSortMode.None));
            foreach (var w in targets)
            {
                if (w == null) continue;
                w.m_Tint = look.splatTint;
                w.m_TintSparesBright = look.tintSparesBright;
                w.m_Saturation = look.saturation;
                w.m_Exposure = look.exposure;
                w.m_HazeColor = look.hazeColor;
                w.m_HazeDensity = look.hazeDensity;
            }
            // MuseXR/Artwork reads these: every hung work gets the lamp and the haze at once.
            Shader.SetGlobalVector(MuseFrameLight, look.FrameLampGlobal);
            Shader.SetGlobalVector(MuseHaze, look.HazeGlobal);
            foreach (var l in frameLights)
            {
                if (l == null) continue;
                l.color = look.frameLightColor;
                l.intensity = look.frameLightIntensity;
            }
            if (artworks.Length > 0)
            {
                _block ??= new MaterialPropertyBlock();
                var tint = look.ArtworkTint;
                foreach (var a in artworks)
                {
                    if (a == null) continue;
                    a.GetPropertyBlock(_block);
                    _block.SetColor(BaseColor, tint);
                    a.SetPropertyBlock(_block);
                }
            }
            if (driveMeshFog)
            {
                RenderSettings.fog = look.hazeDensity > 0.0005f;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = look.hazeColor;
                RenderSettings.fogDensity = look.hazeDensity;
            }
            if (particles != null)
            {
                var emission = particles.emission;
                emission.rateOverTimeMultiplier = look.particles * particlesAtFull;
            }
        }

        void OnDisable()
        {
            if (Ring != null) Apply(TimeRing.Preset(TimeOfDay.Afternoon));
            if (driveMeshFog) RenderSettings.fog = false;
        }
    }
}
