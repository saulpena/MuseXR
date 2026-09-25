using GaussianSplatting.Runtime;

namespace MuseXR.Worlds
{
    /// <summary>
    /// The per-renderer performance knobs every runtime-created splat world gets.
    ///
    /// Deliberately constants on a static class rather than Inspector fields: a public field on a
    /// MonoBehaviour is serialized into every scene holding it, and the scene copy then silently
    /// beats the code default (the <c>MuseumDialogue.dialogueModel</c> trap). Change them here,
    /// one per build, and measure on the headset — the Editor cannot judge either of them.
    /// </summary>
    public static class SplatRenderTuning
    {
        /// <summary>
        /// Radix-sort the splats on every Nth render PASS. The package's counter increments once
        /// per pass, and Multi Pass renders the camera twice per frame, so:
        /// 1 = sort for each eye (package default, two 500k sorts a frame);
        /// 2 = sort for the left eye and reuse that order for the right — once per frame;
        /// 3+ = which eye sorts alternates frame to frame.
        /// The eyes are ~63 mm apart, so one shared order is visually equivalent.
        /// </summary>
        public const int SortNthPass = 2;

        /// <summary>
        /// Spherical-harmonics order evaluated per splat. Every Marble <c>.spz</c> in this project
        /// is <c>shDegree 0</c> (read from the headers of all nine 500k sources), so bands 1-3
        /// carry no data. At 0 the embedded package's <c>CSCalcViewData</c> also skips fetching the
        /// SH buffer (a local patch to <c>LoadSplatData</c>), which was 32 of the 48 bytes read per
        /// splat per eye. Raise it only for content that genuinely carries SH.
        /// </summary>
        public const int SHOrder = 0;

        public static void Apply(GaussianSplatRenderer renderer)
        {
            renderer.m_SortNthFrame = SortNthPass;
            renderer.m_SHOrder = SHOrder;
        }

        /// <summary>
        /// Resolution of the splat layer only, relative to the camera target (itself at URP
        /// renderScale 0.8). Text, UI, characters and artworks are unaffected.
        ///
        /// Measured on Quest 3S, intro, 25 Sep 2026, cycling live with the X button:
        ///   1.0  36-37 FPS, 20-22 ms   0.8  53-55 FPS, 15.4 ms
        ///   0.7  59-65 FPS, 12.5-15.6  0.6  72-73 FPS, 11.5 ms (full rate)
        /// 0.6 chosen by Saul. Cost seen in headset captures: softer fine detail (window bars,
        /// petals) and the paving's tiny white specks turn into small squares.
        /// </summary>
        public const float SplatResolutionScale = 0.6f;

        /// <summary>The steps the X button (F9 in the Editor) cycles through, for A/B in the headset.</summary>
        public static readonly float[] SplatScaleSteps = { 1.0f, 0.9f, 0.8f, 0.7f, 0.6f };

        /// <summary>The step after <paramref name="current"/>, wrapping; the first step if it is not one.</summary>
        public static float NextSplatScale(float current)
        {
            for (int i = 0; i < SplatScaleSteps.Length; i++)
                if (UnityEngine.Mathf.Abs(SplatScaleSteps[i] - current) < 0.001f)
                    return SplatScaleSteps[(i + 1) % SplatScaleSteps.Length];
            return SplatScaleSteps[0];
        }
    }
}
