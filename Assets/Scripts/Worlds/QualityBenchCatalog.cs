using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// One version of one environment in the quality benchmark: which splat file, where it came
    /// from, and the label the visitor reads while looking at it.
    ///
    /// Every version of an environment shares that environment's spawn and worldScale, because the
    /// versions are cut from the same capture in the same frame. What changes between them is the
    /// splats and nothing else, which is the point of the comparison.
    /// </summary>
    public sealed class BenchVariant
    {
        /// <summary>Addressables key, and the converted asset's file name in <see cref="QualityBenchCatalog.Folder"/>.</summary>
        public string key;

        /// <summary>Which environment, in capitals, e.g. "TEMPLE HALL".</summary>
        public string environment;

        /// <summary>Which version of it, e.g. "500k · untouched".</summary>
        public string version;

        /// <summary>Where the splats came from, in one line: what was downloaded and what we did to it.</summary>
        public string provenance;

        /// <summary>The world this version stands in: spawn, scale, far clip.</summary>
        public WorldDefinition world;

        /// <summary>
        /// True for a version kept only to compare against in the Editor: it cannot render on a
        /// Quest 3S. Its asset lives in <see cref="QualityBenchCatalog.ReferenceFolder"/>, which no
        /// player build carries, so the headset build simply skips it.
        /// </summary>
        public bool referenceOnly;

        /// <summary>
        /// Where the chapter's 12 m Buddha stands in this version, in Unity metres, facing +z; null
        /// when the environment has none. The statue is a mesh, so it looks right from every side,
        /// and it is what the room has to be judged around.
        /// </summary>
        public Vector3? buddhaAt;

        /// <summary>The label as shown and logged: "TEMPLE HALL · 500k · untouched".</summary>
        public string Label => $"{environment} · {version}";
    }

    /// <summary>
    /// The versions the quality benchmark cycles through, in viewing order. Pure data, testable in
    /// EditMode. A version whose converted asset does not exist yet is simply skipped at runtime
    /// and reported, so the list can name what is planned before it is built.
    /// </summary>
    public static class QualityBenchCatalog
    {
        /// <summary>Where every benchmark asset is converted to, and the Addressables group that folder feeds.</summary>
        public const string Folder = "Assets/Worlds/Bench";

        /// <summary>
        /// Versions the headset cannot render, kept for Editor comparison only. Measured on a Quest
        /// 3S, 28 Sep 2026: a 4.32M-splat asset needs a 138,240,000-byte SH buffer against the
        /// device's 134,217,728-byte limit, so the renderer throws and draws nothing; the 3.80M crop
        /// fits, draws at 0.1-3 FPS, and the system kills the app for memory within a minute.
        /// </summary>
        public const string ReferenceFolder = Folder + "/Reference";

        /// <summary>Every benchmark key starts with this, so a bench asset can never collide with a shipped world's address.</summary>
        public const string Prefix = "bench-";

        const string Temple = "TEMPLE HALL";
        const string Conservatory = "CONSERVATORY";
        const string SmallPrompt = "SMALL TEMPLE (prompt)";
        const string SmallChisel = "SMALL TEMPLE (Chisel)";
        const string BuddhaChisel = "BUDDHA HALL (Chisel)";
        const string GardenChisel = "GARDEN (Chisel)";

        /// <summary>The Buddha's place in the shipped chapter (Museum.unity, WorldProps of the temple hall).</summary>
        static readonly Vector3 TempleBuddha = new Vector3(0f, -0.6f, 0f);

        /// <summary>
        /// In the Chisel Buddha hall: 8 m in from the room's centre toward the gilded back wall, so
        /// the visitor arriving 6 m the other side of centre sees it whole, with floor all round.
        /// </summary>
        static readonly Vector3 ChiselBuddha = new Vector3(0f, 0f, -8f);

        static WorldDefinition Base(string smallKey)
        {
            foreach (var w in WorldCatalog.Small)
                if (w.key == smallKey + WorldCatalog.SmallSuffix) return w;
            throw new KeyNotFoundException($"QualityBench: no WorldCatalog.Small entry for '{smallKey}'");
        }

        /// <summary>The same spawn and scale as <paramref name="source"/>, under a bench key.</summary>
        static WorldDefinition As(WorldDefinition source, string key) => new WorldDefinition
        {
            key = key, displayName = source.displayName, worldScale = source.worldScale,
            spawn = source.spawn, groundY = source.groundY, yawDegrees = source.yawDegrees,
            cameraFar = source.cameraFar, hasMeasuredSpawn = source.hasMeasuredSpawn,
            walkBounds = source.walkBounds,
        };

        static BenchVariant V(string env, string version, string provenance, string key, WorldDefinition from,
                              bool referenceOnly = false, Vector3? buddhaAt = null) =>
            new BenchVariant
            {
                key = Prefix + key, environment = env, version = version, provenance = provenance,
                world = As(from, Prefix + key), referenceOnly = referenceOnly, buddhaAt = buddhaAt,
            };

        static readonly WorldDefinition TempleWorld = Base("empty-chinese-imperial-temple-hall");
        static readonly WorldDefinition ConservatoryWorld = Base("grand-conservatory-garden-path");

        /// <summary>
        /// The small hall generated from a text prompt on 28 Sep 2026. Not a shipped world, so its
        /// placement lives here. Measured with Tools/splat/splatvis from the capture's centre: the
        /// floor there sits at y 0 (p50 of the splats within 1.5 m), yaw 0 faces the near altar,
        /// yaw 180 looks down the rest of the hall. The visitor stands 3 m back from that centre:
        /// at the centre itself the altar cabinet filled the view (Editor Play Mode, 28 Sep). Same 1.7 scale as every other
        /// world, so nothing about its size is changed to flatter it.
        /// </summary>
        static readonly WorldDefinition SmallPromptWorld = new WorldDefinition
        {
            key = "chinese-imperial-temple-hall-prompt", displayName = "Small Temple (prompt)",
            worldScale = 1.7f, spawn = new Vector2(0f, -3f / 1.7f), groundY = 0f, yawDegrees = 0f,
            cameraFar = 250f, hasMeasuredSpawn = true,
        };

        /// <summary>
        /// The hall generated through Marble's Chisel (3D layout) on 28 Sep 2026 from a 16 x 24 m,
        /// 10 m box with two rows of five columns and a statue plinth; Marble exported it at its own
        /// scale, so its collider measures 5.8 x 9.1 raw, ~10 x 15.5 m at 1.7. Spawn measured with
        /// Tools/splat/splatvis from the capture's centre: floor at y 0 (p50 within 1.5 m), yaw 180
        /// faces the carved dragon screen on the plinth, yaw 0 the entrance door.
        /// </summary>
        static readonly WorldDefinition SmallChiselWorld = new WorldDefinition
        {
            key = "ornate-temple-hall-chisel", displayName = "Small Temple (Chisel)",
            worldScale = 1.7f, spawn = Vector2.zero, groundY = 0f, yawDegrees = 180f,
            cameraFar = 250f, hasMeasuredSpawn = true,
        };

        /// <summary>
        /// The hall built in Marble's Chisel on 28 Sep 2026 to hold the chapter's Buddha: a 20 x 28 m,
        /// 16 m box with twelve columns against the side walls and NOTHING in the middle (a free-standing
        /// object is captured from one spot and smears from the side, which the dragon screen in the
        /// small Chisel hall showed). Marble exported it at its own scale: the collider is 7.6 wide and
        /// 5.8 high raw, so 2.63 makes it the designed 20 m wide and 16 m high. Its depth came out
        /// short of 28 m. Measured with Tools/splat/splatvis at 2.63: floor y 0 at the centre, the
        /// entrance doors ~10 m ahead at yaw 0, the gilded dragon relief wall behind at yaw 180.
        /// </summary>
        /// <summary>
        /// The conservatory garden rebuilt small in Marble's Chisel on 28 Sep 2026: a 20 x 30 m
        /// courtyard, flagstone path down the middle, reflecting pools either side, planting beds
        /// against the walls, the conservatory's facade at the end, nothing in the middle. Its
        /// collider is 11.6 x 18.7 raw, so the usual 1.7 already makes it ~20 x 32 m. Measured with
        /// Tools/splat/splatvis at 1.7: floor y 0 at the centre, a baroque facade ahead at yaw 0
        /// (Marble put a second one behind, at yaw 180). Its full export is 1.92M splats, not 4.32M,
        /// so unlike the others its full file fits the headset's GPU buffer limit.
        /// </summary>
        static readonly WorldDefinition GardenChiselWorld = new WorldDefinition
        {
            key = "garden-courtyard-chisel", displayName = "Garden (Chisel)",
            worldScale = 1.7f, spawn = Vector2.zero, groundY = 0f, yawDegrees = 0f,
            cameraFar = 250f, hasMeasuredSpawn = true,
        };

        static readonly WorldDefinition BuddhaChiselWorld = new WorldDefinition
        {
            key = "buddha-hall-chisel", displayName = "Buddha Hall (Chisel)",
            worldScale = 2.63f, spawn = new Vector2(0f, 6f / 2.63f), groundY = 0f, yawDegrees = 180f,
            cameraFar = 250f, hasMeasuredSpawn = true,
        };

        public static readonly IReadOnlyList<BenchVariant> All = new List<BenchVariant>
        {
            // Temple hall: "Empty Chinese Imperial Temple Hall", Marble 1.1 Plus, generated by Saul
            // 27 Sep 2026. In the app it is chapter 01, "Hall of the Great Buddha". The cleaned and
            // cropped versions were cut for a visitor walking x -9..9, z -9..18 (Unity metres) by
            // Tools/splat/prune.py on 28 Sep 2026, and each passed validate.py on held-out views.
            // Budgets of 1M and 750k were also cut and left out: they render with black holes.
            V(Temple, "500k · untouched",
              "Marble's 500k export, exactly as shipped in the app today",
              "temple-500k-untouched", TempleWorld, buddhaAt: TempleBuddha),
            V(Temple, "500k · cleaned",
              "453,051 of the 500k: splats never seen from the walk area removed",
              "temple-500k-cleaned", TempleWorld, buddhaAt: TempleBuddha),
            V(Temple, "FULL → 1.5M crop",
              "Marble's full-res export (4.32M), the 1.5M that matter most from the walk area",
              "temple-full-crop-1500k", TempleWorld, buddhaAt: TempleBuddha),
            V(Temple, "FULL → visible crop",
              "Marble's full-res export, the 3.80M visible from the walk area. Reference: expect low FPS",
              "temple-full-crop-visible", TempleWorld, referenceOnly: true),
            V(Temple, "FULL 4.32M · untouched",
              "Marble's full-res export, unmodified. Reference only: expect far below frame rate",
              "temple-full-untouched", TempleWorld, referenceOnly: true),

            // Small temple from a text prompt: "Chinese Imperial Temple Hall", Marble 1.1 Plus,
            // 28 Sep 2026. CORRECTED the same day: its collider (~16 x 24 m at 1.7) only covers the
            // strip between the column rows. The splats show a ~30 x 36 m hall with 24 free-standing
            // columns (Tools/splat/out/prompt-final/colmap.png), so it is mid-sized, not small.
            V(SmallPrompt, "500k · untouched",
              "Marble's 500k export of a ~30 x 36 m columned hall, generated from a text prompt",
              "small-prompt-500k-untouched", SmallPromptWorld),
            V(SmallPrompt, "FULL → 1M (coverage)",
              "The best-covering 1M of Marble's full export (4.32M), sampled clear of the columns",
              "small-prompt-full-cov-1000k", SmallPromptWorld),
            V(SmallPrompt, "FULL 4.32M · untouched",
              "Marble's full-res export of the small hall, unmodified. Reference: expect low FPS",
              "small-prompt-full-untouched", SmallPromptWorld, referenceOnly: true),

            // Small temple from Chisel: "Ornate Temple Hall Interior" (Marble named it), Marble 1.1
            // Plus, a 3D layout plus an edited panorama, 28 Sep 2026.
            V(SmallChisel, "500k · untouched",
              "Marble's 500k export of a hall built from a 3D layout (Chisel), ~10 x 15.5 m here",
              "small-chisel-500k-untouched", SmallChiselWorld),
            // Cut from the full export (4.32M) by accumulated coverage over the walkable floor
            // (Tools/splat/prune.py --rank coverage, 28 Sep 2026). On held-out views both are closer
            // to the full than Marble's own 500k by mean and share of changed pixels, but in renders
            // they carry dark blotches on the carved side-wall panels: mild at 1M, clear at 750k.
            V(SmallChisel, "FULL → 1M (coverage)",
              "The best-covering 1M of Marble's full export (4.32M) over the walkable floor",
              "small-chisel-full-cov-1000k", SmallChiselWorld),
            V(SmallChisel, "FULL → 750k (coverage)",
              "The best-covering 750k of Marble's full export (4.32M) over the walkable floor",
              "small-chisel-full-cov-750k", SmallChiselWorld),

            // Buddha hall from Chisel: "Ornate Golden Temple Interior" (Marble named it), 28 Sep 2026.
            // The chapter's Buddha stands in it, so the room is judged as it would be used.
            V(BuddhaChisel, "500k · untouched",
              "Marble's 500k export of a 20 m wide hall built in Chisel around the Buddha",
              "buddha-chisel-500k-untouched", BuddhaChiselWorld, buddhaAt: ChiselBuddha),
            V(BuddhaChisel, "FULL → 1M (coverage)",
              "The best-covering 1M of Marble's full export (4.32M) over the walkable floor",
              "buddha-chisel-full-cov-1000k", BuddhaChiselWorld, buddhaAt: ChiselBuddha),
            V(BuddhaChisel, "FULL → 750k (coverage)",
              "The best-covering 750k of Marble's full export (4.32M) over the walkable floor",
              "buddha-chisel-full-cov-750k", BuddhaChiselWorld, buddhaAt: ChiselBuddha),
            // The same full export reduced by MERGING instead of deleting: PlayCanvas splat-transform
            // 3.7.0 --decimate-adaptive (pairwise merge, budget allocated by local error), no other
            // filter, 28 Sep 2026. The coverage cuts above delete splats and keep the survivors
            // unchanged, which left blotches where the deleted ones had been compensating.
            V(BuddhaChisel, "FULL → 1M (merged)",
              "Marble's full export (4.32M) merged down to 1M by splat-transform --decimate-adaptive",
              "buddha-chisel-full-merge-1000k", BuddhaChiselWorld, buddhaAt: ChiselBuddha),
            V(BuddhaChisel, "FULL → 750k (merged)",
              "Marble's full export (4.32M) merged down to 750k by splat-transform --decimate-adaptive",
              "buddha-chisel-full-merge-750k", BuddhaChiselWorld, buddhaAt: ChiselBuddha),

            // Conservatory: "Grand Conservatory Garden Path", Marble 1.1 Plus, generated by Saul
            // 23 Sep 2026. In the app it is the intro, "Threshold Conservatory". The visitor stands
            // still there, so its cleaned version was pruned for head movement at the spawn only.
            V(Conservatory, "500k · untouched",
              "Marble's 500k export, as shipped before 25 Sep",
              "conservatory-500k-untouched", ConservatoryWorld),
            V(Conservatory, "500k · cleaned",
              "339,578 of 500k kept: splats never seen from the spawn removed, 25 Sep (shipped)",
              "conservatory-500k-cleaned", ConservatoryWorld),

            // The same garden rebuilt small in Chisel: "Enclosed Garden Courtyard", 28 Sep 2026.
            V(GardenChisel, "500k · untouched",
              "Marble's 500k export of the garden rebuilt as a ~20 x 32 m courtyard in Chisel",
              "garden-chisel-500k-untouched", GardenChiselWorld),
            V(GardenChisel, "FULL → 1M (coverage)",
              "The best-covering 1M of Marble's full export (1.92M) over the walkable path",
              "garden-chisel-full-cov-1000k", GardenChiselWorld),
            V(GardenChisel, "FULL 1.92M · untouched",
              "Marble's full export, unmodified: small enough to fit the headset's GPU buffers",
              "garden-chisel-full-untouched", GardenChiselWorld),
        };

        /// <summary>The environments in viewing order, each once.</summary>
        public static IReadOnlyList<string> Environments
        {
            get
            {
                var list = new List<string>();
                foreach (var v in All) if (!list.Contains(v.environment)) list.Add(v.environment);
                return list;
            }
        }

        /// <summary>Index of the first version of the environment after the one at <paramref name="index"/>, wrapping.</summary>
        public static int NextEnvironmentStart(IReadOnlyList<BenchVariant> list, int index)
        {
            if (list.Count == 0) return -1;
            var env = list[Mathf.Clamp(index, 0, list.Count - 1)].environment;
            for (int step = 1; step <= list.Count; step++)
            {
                int i = (index + step) % list.Count;
                if (list[i].environment != env) return i;
            }
            return index;
        }

        /// <summary>"3 / 6" within its environment: which of that environment's versions this is.</summary>
        public static string PositionInEnvironment(IReadOnlyList<BenchVariant> list, int index)
        {
            var env = list[index].environment;
            int n = 0, at = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].environment != env) continue;
                n++;
                if (i == index) at = n;
            }
            return $"{at} / {n}";
        }
    }
}
