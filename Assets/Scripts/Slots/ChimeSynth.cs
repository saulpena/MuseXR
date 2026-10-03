using System;

namespace MuseXR.Slots
{
    /// <summary>
    /// The four chapter sounds (§3.2: Palace a bronze bell, Grotto a stone chime, Van Gogh wood,
    /// Monet water), made by modal synthesis: a struck object is a sum of decaying partials at the
    /// ratios its physics gives it, plus a short strike noise. Generated, so there is no asset to
    /// ship, the same way the Gate's chime is. Pure: returns mono samples in -1..1.
    ///
    ///   Bronze bell  a bell's partials (hum 0.5, prime 1, tierce 1.2, quint 1.5, nominal 2 ...),
    ///                each paired with a slightly detuned twin so it beats, ~4 s.
    ///   Stone chime  a free stone bar's inharmonic modes (1, 2.76, 5.40, 8.93), bright, ~1.6 s.
    ///   Wood         a tuned wooden bar (1, 3.93, 9.2, like a marimba key), warm and short, ~0.8 s.
    ///   Water        three drops, each a bubble whose pitch rises as it closes (Minnaert), ~1.1 s.
    /// </summary>
    public static class ChimeSynth
    {
        public const int SampleRate = 44100;
        public const float Peak = 0.8f;
        public const float BellHz = 330f, StoneHz = 740f, WoodHz = 523.25f, WaterHz = 1050f;

        public static float Seconds(ChapterSound sound) => sound switch
        {
            ChapterSound.BronzeBell => 4f,
            ChapterSound.StoneChime => 1.6f,
            ChapterSound.Wood => 0.8f,
            _ => 1.1f,
        };

        public static float[] Render(ChapterSound sound, int sampleRate = SampleRate)
        {
            var data = new float[(int)(Seconds(sound) * sampleRate)];
            switch (sound)
            {
                case ChapterSound.BronzeBell:
                    // (ratio, amplitude, decay per second)
                    Partials(data, sampleRate, BellHz, new[]
                    {
                        (0.5f, 0.55f, 0.55f), (1f, 0.8f, 0.9f), (1.2f, 0.5f, 1.2f), (1.5f, 0.3f, 1.6f),
                        (2f, 0.45f, 1.9f), (2.5f, 0.2f, 3f), (2.67f, 0.18f, 3.4f), (3.01f, 0.12f, 4.5f), (4.07f, 0.08f, 6f),
                    }, beatHz: 0.7f);
                    Strike(data, sampleRate, 0.006f, 0.25f, 1);
                    break;
                case ChapterSound.StoneChime:
                    Partials(data, sampleRate, StoneHz, new[]
                    {
                        (1f, 1f, 2.6f), (2.756f, 0.42f, 5.5f), (5.404f, 0.22f, 10f), (8.933f, 0.1f, 17f),
                    }, beatHz: 0f);
                    Strike(data, sampleRate, 0.004f, 0.35f, 2);
                    break;
                case ChapterSound.Wood:
                    Partials(data, sampleRate, WoodHz, new[]
                    {
                        (1f, 1f, 6.5f), (3.93f, 0.35f, 15f), (9.2f, 0.12f, 32f),
                    }, beatHz: 0f);
                    Strike(data, sampleRate, 0.003f, 0.5f, 3);
                    break;
                default:
                    Drop(data, sampleRate, 0f, WaterHz, 1f);
                    Drop(data, sampleRate, 0.13f, 820f, 0.6f);
                    Drop(data, sampleRate, 0.31f, 1320f, 0.42f);
                    break;
            }
            Normalise(data, Peak);
            return data;
        }

        /// <summary>The detent tick for the time ring: one small, short water drop.</summary>
        public static float[] Tick(int sampleRate = SampleRate)
        {
            var data = new float[(int)(0.25f * sampleRate)];
            Drop(data, sampleRate, 0f, 1500f, 1f);
            Normalise(data, 0.5f);
            return data;
        }

        public const float RefuseHz = 196f;

        /// <summary>
        /// The refusal (her "the fourth is refused with visible and audible feedback"): two dull, low,
        /// quickly damped knocks falling a tone, unlike any chapter sound, so it reads as "no".
        /// </summary>
        public static float[] Refuse(int sampleRate = SampleRate)
        {
            var data = new float[(int)(0.45f * sampleRate)];
            var second = (int)(0.13f * sampleRate);
            var a = new float[data.Length];
            var b = new float[data.Length - second];
            Partials(a, sampleRate, RefuseHz, new[] { (1f, 1f, 22f), (2.3f, 0.25f, 40f) }, beatHz: 0f);
            Partials(b, sampleRate, RefuseHz * 0.84f, new[] { (1f, 1f, 22f), (2.3f, 0.25f, 40f) }, beatHz: 0f);
            Strike(a, sampleRate, 0.004f, 0.3f, 7);
            for (var i = 0; i < data.Length; i++) data[i] = a[i] + (i >= second ? 0.8f * b[i - second] : 0f);
            Normalise(data, 0.7f);
            return data;
        }

        static void Partials(float[] data, int rate, float f0, (float ratio, float amp, float decay)[] modes, float beatHz)
        {
            var attack = rate * 0.002f;   // 2 ms: a strike, not a click
            for (var m = 0; m < modes.Length; m++)
            {
                var (ratio, amp, decay) = modes[m];
                var w = 2.0 * Math.PI * f0 * ratio / rate;
                var w2 = 2.0 * Math.PI * (f0 * ratio + beatHz * (m + 1) * 0.5) / rate;
                var k = Math.Exp(-decay / rate);
                var env = 1.0;
                for (var i = 0; i < data.Length && env > 1e-5; i++)
                {
                    var a = i < attack ? i / attack : 1.0;
                    var s = beatHz > 0f ? 0.65 * Math.Sin(w * i) + 0.35 * Math.Sin(w2 * i) : Math.Sin(w * i);
                    data[i] += (float)(amp * env * a * s);
                    env *= k;
                }
            }
        }

        /// <summary>The mallet: a few milliseconds of fading noise, differenced so it is bright. Seeded, so repeatable.</summary>
        static void Strike(float[] data, int rate, float seconds, float amp, uint seed)
        {
            var n = Math.Min(data.Length, (int)(seconds * rate));
            var rng = seed * 2654435761u + 1u;
            var prev = 0f;
            for (var i = 0; i < n; i++)
            {
                rng = rng * 1664525u + 1013904223u;
                var white = (rng >> 8) / 8388608f - 1f;
                data[i] += amp * (white - prev) * (1f - i / (float)n);
                prev = white;
            }
        }

        /// <summary>One water drop: a sine whose pitch climbs ~70% over its first ~60 ms, dying fast.</summary>
        static void Drop(float[] data, int rate, float start, float f0, float amp)
        {
            var from = (int)(start * rate);
            var n = Math.Min(data.Length - from, (int)(0.35f * rate));
            var phase = 0.0;
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)rate;
                var f = f0 * (1.0 + 0.7 * (1.0 - Math.Exp(-t / 0.03)));
                phase += 2.0 * Math.PI * f / rate;
                var env = Math.Exp(-t * 28.0) * Math.Min(1.0, i / (rate * 0.001));
                data[from + i] += (float)(amp * env * Math.Sin(phase));
            }
        }

        static void Normalise(float[] data, float peak)
        {
            var max = 0f;
            foreach (var s in data) { var a = Math.Abs(s); if (a > max) max = a; }
            if (max <= 0f) return;
            var g = peak / max;
            for (var i = 0; i < data.Length; i++) data[i] *= g;
        }
    }
}
