using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>The three settings on Skylar's Monet time ring, in the order the ring turns.</summary>
    public enum TimeOfDay { Mist, Afternoon, Dusk }

    /// <summary>
    /// Everything one time of day changes. Her plan (chapter D, "Water and Light"): "Grade, fog, sky
    /// and frame lights ease over 4s". Splat lighting is baked, so this never relights the capture:
    /// it grades the splats (tint, saturation, exposure), hazes them with distance, sets mesh fog,
    /// and lights the frames, which are real meshes.
    /// </summary>
    [System.Serializable]
    public struct TimeOfDayLook
    {
        public Color splatTint;
        [Range(0f, 1f)] public float tintSparesBright;
        public float saturation;
        public float exposure;
        public Color hazeColor;
        public float hazeDensity;
        public Color frameLightColor;
        public float frameLightIntensity;
        [Range(0f, 1f)] public float particles;

        public static TimeOfDayLook Lerp(in TimeOfDayLook a, in TimeOfDayLook b, float t)
        {
            t = Mathf.Clamp01(t);
            return new TimeOfDayLook
            {
                splatTint = Color.Lerp(a.splatTint, b.splatTint, t),
                tintSparesBright = Mathf.Lerp(a.tintSparesBright, b.tintSparesBright, t),
                saturation = Mathf.Lerp(a.saturation, b.saturation, t),
                exposure = Mathf.Lerp(a.exposure, b.exposure, t),
                hazeColor = Color.Lerp(a.hazeColor, b.hazeColor, t),
                hazeDensity = Mathf.Lerp(a.hazeDensity, b.hazeDensity, t),
                frameLightColor = Color.Lerp(a.frameLightColor, b.frameLightColor, t),
                frameLightIntensity = Mathf.Lerp(a.frameLightIntensity, b.frameLightIntensity, t),
                particles = Mathf.Lerp(a.particles, b.particles, t),
            };
        }

        /// <summary>
        /// Colour for the hung artworks. They are URP/Unlit, so a light cannot reach them: they take
        /// the world's grade as a material tint instead, or they look pasted onto the room.
        /// </summary>
        public Color ArtworkTint => new Color(splatTint.r * exposure, splatTint.g * exposure, splatTint.b * exposure, 1f);

        /// <summary>How hard the faked picture lamp (MuseXR/Artwork) lifts the top of a canvas.</summary>
        public const float LampStrength = 0.35f;

        /// <summary>The picture lamp for MuseXR/Artwork's _MuseFrameLight: colour times strength.</summary>
        public Vector4 FrameLampGlobal => new Vector4(
            frameLightColor.r * frameLightIntensity * LampStrength,
            frameLightColor.g * frameLightIntensity * LampStrength,
            frameLightColor.b * frameLightIntensity * LampStrength, 0f);

        /// <summary>The splats' haze for MuseXR/Artwork's _MuseHaze: colour, density per metre.</summary>
        public Vector4 HazeGlobal => new Vector4(hazeColor.r, hazeColor.g, hazeColor.b, hazeDensity);

        /// <summary>The capture exactly as World Labs made it: no tint, no grade, no haze.</summary>
        public bool LeavesSplatsAsCaptured =>
            splatTint == Color.white && Mathf.Approximately(saturation, 1f) &&
            Mathf.Approximately(exposure, 1f) && hazeDensity <= 0f;
    }

    /// <summary>
    /// The ring's state: which time is chosen, and the eased look between the last one and it.
    /// Turning the ring mid-ease starts the new ease from wherever the look currently is, so the
    /// world never jumps. No Unity lifecycle here; <see cref="TimeRingDriver"/> feeds it time.
    /// </summary>
    public sealed class TimeRing
    {
        public const float DefaultEaseSeconds = 4f;

        public static TimeOfDayLook Preset(TimeOfDay time) => time switch
        {
            TimeOfDay.Mist => new TimeOfDayLook
            {
                // Reviewed 3 Oct 2026: saturation 0.55 + exposure 1.05 read as "a white veil, a faded
                // photo". Near colour stays; only the distance fades, which is what mist does.
                splatTint = new Color(0.95f, 0.98f, 1f), tintSparesBright = 0f,
                saturation = 0.82f, exposure = 0.95f,
                hazeColor = new Color(0.76f, 0.8f, 0.83f), hazeDensity = 0.045f,
                frameLightColor = new Color(0.9f, 0.95f, 1f), frameLightIntensity = 0.8f,
                particles = 1f,
            },
            TimeOfDay.Dusk => new TimeOfDayLook
            {
                // Reviewed 3 Oct 2026: at exposure 0.8 with a light haze it read as "a pink filter,
                // not falling light". Darker, and the haze darkens the distance instead of lifting it.
                splatTint = new Color(1f, 0.72f, 0.6f), tintSparesBright = 0.6f,
                saturation = 1.1f, exposure = 0.62f,
                hazeColor = new Color(0.38f, 0.24f, 0.34f), hazeDensity = 0.03f,
                frameLightColor = new Color(1f, 0.74f, 0.42f), frameLightIntensity = 1.8f,
                particles = 0.35f,
            },
            _ => new TimeOfDayLook
            {
                splatTint = Color.white, tintSparesBright = 0f,
                saturation = 1f, exposure = 1f,
                hazeColor = Color.white, hazeDensity = 0f,
                frameLightColor = new Color(1f, 0.94f, 0.82f), frameLightIntensity = 1.2f,
                particles = 0f,
            },
        };

        readonly float _easeSeconds;
        TimeOfDayLook _from;
        float _elapsed;

        public TimeOfDay Chosen { get; private set; }
        public TimeOfDayLook Current { get; private set; }
        public bool IsEasing => _elapsed < _easeSeconds;

        public TimeRing(TimeOfDay start = TimeOfDay.Afternoon, float easeSeconds = DefaultEaseSeconds)
        {
            _easeSeconds = Mathf.Max(0.0001f, easeSeconds);
            Chosen = start;
            Current = _from = Preset(start);
            _elapsed = _easeSeconds;
        }

        /// <summary>Turn the ring. Returns false if that time was already chosen.</summary>
        public bool Choose(TimeOfDay time)
        {
            if (time == Chosen) return false;
            Chosen = time;
            _from = Current;
            _elapsed = 0f;
            return true;
        }

        /// <summary>The next setting round the ring, wrapping: mist, afternoon, dusk, mist.</summary>
        public static TimeOfDay Next(TimeOfDay time) => (TimeOfDay)(((int)time + 1) % 3);

        /// <summary>Advance the ease. Smoothstep, so it leaves and settles gently.</summary>
        public TimeOfDayLook Tick(float deltaSeconds)
        {
            if (!IsEasing) return Current;
            _elapsed = Mathf.Min(_easeSeconds, _elapsed + Mathf.Max(0f, deltaSeconds));
            Current = TimeOfDayLook.Lerp(_from, Preset(Chosen), Mathf.SmoothStep(0f, 1f, _elapsed / _easeSeconds));
            return Current;
        }
    }
}
