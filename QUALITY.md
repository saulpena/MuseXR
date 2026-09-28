# Splat visual quality on standalone headsets

The companion to `PERFORMANCE.md`. That file is about frame rate; this one is about how the worlds
**look** on a Quest 3S, and what makes them look better or worse. Branch `quality-benchmark`,
started 28 Sep 2026 because Skylar was not happy with the visual quality of the experience.

Read this before changing splat assets, the splat renderer, or how worlds are made in Marble.
Web research behind several findings is in `Tools/splat/RESEARCH-quest-quality.md` (every claim
sourced and dated, tagged OFFICIAL / ACADEMIC / COMMUNITY / INFERENCE).

## Headline findings

1. **Small, enclosed rooms made in Marble's Chisel look far better than big captures at the same
   500k.** Saul's verdict in the headset, 28 Sep: the Chisel versions "look a lot better than the
   non-Chisel versions". The splat budget is per world, not per square metre, so a small room gets
   several times the density. Chisel lets the room be designed to size.
2. **At 500k, Chisel rooms run 50–66 FPS on Quest 3S** (splat resolution 0.6). Denser rooms cost
   more per splat because every splat is close and large on screen (see the table below).
3. **Nothing above ~3.3M splats can render on a Quest 3S at all**, whatever the frame rate: the
   splat renderer needs one GPU buffer of 32 B (SH) and 40 B (view data) per splat, and the device's
   maximum buffer is 134,217,728 B. A 4.32M world throws
   `graphics buffer (138240000 bytes) exceeds the maximum buffer size` and **draws nothing** — the
   frame rate then reads a perfect 72 FPS of an empty scene. The bench panel now shows
   **NOT RENDERING** for this case (`GaussianSplatRenderer.HasValidRenderSetup`).
4. **A 3.8M world fits the buffers but draws at 0.1–3 FPS and the system kills the app for memory
   within a minute** (logcat `Low on memory`, MuseXR as top activity).
5. **Marble's "full" export is not a fixed size**: 4,320,000 for four worlds, 1,920,000 for the
   Chisel garden. Research suggests Marble 1.1 Plus grows a world by up to five "cubes"; plain 1.1
   is recommended for contained interiors (Research §1).
6. **Every Marble `.spz` is flagged "trained with anti-aliasing", and our renderer ignores it** —
   see the dedicated section below. Likely contributor to floor speckle; not yet fixed or tested.
7. **Deleting splats to reach a budget (our visibility/coverage pruning) gives mixed results**:
   closer to the full export by the numbers, but dark or coloured blotches and lost detail on
   surfaces seen up close (columns especially). Saul in the headset: "better in some areas while
   others it looks worse, details are lost". Research says the survivors' colours only look right
   together with the deleted splats; **merging** (or re-fitting colours after) is the fix
   (Research §3, §5). Tested: merging removes the blotches but blurs everything, and neither
   method beat Marble's own 500k — see "Reduction methods".

## Anti-aliasing flag — found 28 Sep 2026, NOT yet fixed

**What the files say (measured).** SPZ header byte 14 is a flags byte; bit `0x1` means the splats
were trained with anti-aliasing (Mip-Splatting-style 2D filter). All 23 Marble `.spz` files in this
project have `flags = 0x01`: every 500k and every full export, old worlds and Chisel ones alike.
Header layout: magic(4) version(4) count(4) shDegree(1) fractionalBits(1) **flags(1)** reserved(1).
All are SPZ v2, SH degree 0, 10 fractional bits.

**What our pipeline does with it (read in code).**
- `Packages/org.nesnausk.gaussian-splatting/Editor/Utils/SPZFileReader.cs:51` reads `flags` and
  then never uses it. The asset has no field for it.
- `Packages/org.nesnausk.gaussian-splatting/Shaders/GaussianSplatting.hlsl:86-88` adds 0.3 px² to the
  2D covariance ("low pass filter to make each splat at least 1px") **without** compensating opacity.

**Why it matters.** A splat trained with anti-aliasing expects the renderer to apply that 0.3
dilation *and* scale its opacity by `sqrt(det(cov2D) / det(cov2D + 0.3·I))`, so a tiny splat gets
wider but fainter. Without the compensation, tiny and thin splats render fatter and more opaque than
they were trained to look — speckle, sparkle, over-dark thin detail — and more so at our reduced
splat-layer resolution (0.6). The mechanism is established (Mip-Splatting; gsplat's "antialiased"
mode). That it explains *our* speckle is an inference until tested.

**Independent confirmation.** PlayCanvas splat-transform, converting a Marble `.spz` to PLY, writes
`comment SplatRenderMode: mip` into the header — it too reads the flag as "render in mip mode".

**The fix, when approved:** read the flag into the asset, and in `CalcCovariance2D`'s caller scale
opacity by the factor above when it is set. Small, local to the package's shader; A/B it on the
headset against the same world with the flag ignored.

## The benchmark

`Assets/Scenes/Tests/QualityBench.unity`, built with **MuseXR > Build > Quest | PICO > Bench**
(`Builds/MuseXR-Quest-Bench.apk`). One version of one world at a time, labelled, with FPS.

| Control | Does |
|---|---|
| Right A / B (N / P) | next / previous version |
| Left Y (E) | next environment |
| Left X (F9) | splat-layer resolution 1.0 → 0.6 |
| Left stick click (B) | hide / show the Buddha |
| Right stick click (H) | hide / show the panel |

- Versions and their provenance: `Assets/Scripts/Worlds/QualityBenchCatalog.cs`. Assets live in
  `Assets/Worlds/Bench/` (group `BenchWorlds`); versions the headset cannot render live in
  `Assets/Worlds/Bench/Reference/` (group `BenchReference`, comparison-only, never built).
  `QualityBenchCatalogTests` pins keys, spawns and that folder split.
- The chapter's Buddha (`Assets/Props/Peach/buddha-statue.gltf`, scale 12.239) stands where the
  game puts it in TEMPLE HALL, and 8 m in front of the back wall in BUDDHA HALL (Chisel). It is a
  mesh prop, **not part of any splat**.
- **Readings go to a CSV on the headset**, one row per second:
  `adb pull /sdcard/Android/data/com.musexr.impossiblemuseum/files/bench/` (in Git Bash prefix
  `MSYS_NO_PATHCONV=1`). The Quest's default 256 KB log buffer lost an entire session of logcat
  readings on 28 Sep; `adb logcat -G 16M` raises it until reboot. Pulled runs: `Tools/perf/bench-runs/`.

### Quest 3S, 28 Sep 2026 (CSV; splat resolution 0.6; median / slowest-10% FPS)

| Version | FPS | Seen |
|---|---|---|
| TEMPLE HALL 500k (original capture) + Buddha | 72 / 65 | 70 s |
| SMALL TEMPLE (prompt) 500k | 71 / 66 | 7 s |
| CONSERVATORY 500k (original) | 72 / 72 | 2 s |
| GARDEN (Chisel) 500k | 66 / 42 | 53 s |
| BUDDHA HALL (Chisel) 500k + Buddha | 61 / 41 | 33 s |
| SMALL TEMPLE (Chisel) 500k | 50 / 36 | 45 s |
| SMALL TEMPLE (Chisel) FULL → 750k (coverage) | 33 / 29 | 8 s |
| BUDDHA HALL (Chisel) FULL → 750k / 1M (coverage) | 38 / 36 · 36 / 33 | 5 s · 2 s |
| SMALL TEMPLE (Chisel) FULL → 1M (coverage) | 24 / 24 | 16 s |
| TEMPLE HALL FULL → 1.5M | 23 / 23 | 1 s |
| SMALL TEMPLE (Chisel) 500k at splat resolution **1.0** | 19 / 18 | 19 s |

Short readings (1–8 s) are indicative only. Not yet measured on the headset: the garden's 1M and
1.92M, the prompt temple's 1M, and the merged Buddha-hall versions.

## Making worlds in Marble (Chisel)

What worked, 28 Sep 2026 (Marble web UI, account Saul's):

1. **3D input → describe the layout with exact metres** ("20 m wide, 28 m deep, 16 m ceiling…").
   Chisel builds walls/columns/platforms to those numbers — verify under Geometry › Walls.
2. **Put nothing free-standing in the middle.** A splat world is reconstructed from a panorama taken
   at one spot, so a free-standing object is captured from one side and smears when walked around
   (the dragon screen in the small Chisel hall). Columns against walls are fine; the Buddha is a
   mesh added in Unity.
3. **Generate panorama → look at it → Apply edit** to fix what the text prompt got wrong (statues
   appear despite "no statues"; walls come out pale) → **Create world**. Each step costs credits;
   Marble shows no price beforehand.
4. **Set every new world Private** (the default is Public; it can only be changed once generation
   finishes). Download 500k `.spz`, full `.spz` and collider `.glb`.
5. **Marble exports Chisel worlds at its own scale** (~0.36–0.38 m per unit). Set `worldScale` from
   the collider so the room is its designed size: 2.63 for the Buddha hall, 1.7 for the garden.
   Room size, not the scale number, sets splat density.
6. Colliders cover only part of a room (the prompt temple's covered the strip between its column
   rows: "16 × 24 m" was wrong, the hall is ~30 × 36 m). Measure rooms from the splats
   (`Tools/splat/splatvis.py` renders, position percentiles, top-down density maps).

## Reduction methods tried

| Method | How | Result |
|---|---|---|
| Visibility pruning (`Tools/splat/prune.py`, default) | delete splats that never add ≥1/255 from the walk area | invisible cuts only: 9–12% on walkable halls; indistinguishable, but frees little |
| Top-N by max contribution (`--budgets`) | keep the N brightest-ever | **black holes** at 1M / 750k of 4.32M — rejected |
| Top-N by accumulated coverage (`--rank coverage`) | keep the N covering most pixels over all views | best numbers so far; **blotches on surfaces seen up close** (red cascade on Buddha-hall columns; mottling on prompt-temple columns even with eyes kept 1.5 m away); tie with full on the garden (full was only 1.92M) |
| **Merging — splat-transform `--decimate-adaptive`** | PlayCanvas CLI 3.7.0 (MIT), pairwise merge, budget by local error; ~1 min per world, 2 GB RAM; output must be `.ply` | **no speckle, no blotches — but visibly softer everywhere** (carving, door gilding, scroll text), worse at 750k. Numbers: 27.5% / 34% px > 8/255 vs full, between coverage cuts and Marble's 500k |

**Verdict on the Buddha hall (blind review of six versions, 28 Sep 2026):** full > **Marble's own
500k** > merged 1M > coverage 1M > merged 750k > coverage 750k. Marble's 500k has mild floor speckle
but no glaring fault; coverage cuts are sharp but carry the column blotch; merging trades every
blotch for global blur. **Neither reduction of a full export beat Marble's own 500k.** The practical
route to quality is therefore authoring (small Chisel rooms at 500k), not reducing full exports —
plus the anti-aliasing fix, which applies at every tier. Not yet seen on the headset.

Always judge a reduction with an **independent blind reviewer** on same-view renders
(`Tools/splat/compare_views.py`) *and* the headset. Pixel statistics missed the Buddha-hall column
blotch entirely (1M was "best" by numbers, worst by eye).

splat-transform round-trip facts (measured): positions come out in the same axis frame as the raw
`.spz` (no flips); our PLY and SPZ importers both take coordinates as stored; converted assets
therefore land where the `.spz` would. Command:
`npx -y @playcanvas/splat-transform@3.7.0 <full.spz> --decimate-adaptive 1000000 <out.ply>`

## Tools

- `Tools/splat/preview.py <a.spz> [<b.spz> …]` — contact sheet from the capture's centre, five
  directions, one row per file. Quick look without Unity.
- **SuperSplat** (superspl.at/editor) — free browser viewer/editor, opens `.spz`; use it to look at a
  world before it goes near Unity, and to delete floaters by hand. Never move/rotate/scale the whole
  world in it (spawns assume the original frame). Export SPZ **v3** or PLY: our importer is v1.1.1
  and may not read SPZ v4.
- `Tools/splat/prune.py`, `validate.py`, `compare_views.py`, `splatvis.py` — see `Tools/splat/README.md`.

## Open

- Anti-aliasing opacity compensation: implement and A/B on the headset.
- Merged Buddha hall 1M / 750k: blind-reviewed (softer, no blotches); not yet seen on the headset.
- Buddha hall coverage cuts carry a red column blotch; superseded if merging wins.
- Garden 1M / 1.92M and prompt-temple 1M not yet seen on the headset.
- Research next steps not yet tried: re-fit colours of a reduced splat against renders of the full
  (Research, ranked step 2), building from plain Marble 1.1 instead of 1.1 Plus.

## Session state, end of 28 Sep 2026 (for whoever picks this up)

**Where to see the reduction results.** `Tools/splat/out/` is git-ignored, so these exist only on
Saul's PC: `Tools/splat/out/buddha-compare/PLAYCANVAS-COMPARISON.png` (six versions × three views,
labelled), the merged PLYs in `Tools/splat/out/buddha-st/` (open in SuperSplat), and the per-world
comparison folders `*-compare/`. Regenerate them with `compare_views.py` (presets `buddha-chisel`,
`garden-chisel`, `prompt-temple`, `chisel`) from the sources listed in `QualityBenchCatalog.cs`.

**Decision (Saul, 28 Sep): Chisel worlds stay at Marble's own 500k — no cutting, no pruning.**

**Chisel worlds made so far** (Marble library, all set Private; the Van Gogh one's Private setting
was chosen but not re-confirmed after reload):

| Marble name | In project as | Size (designed) | worldScale | Status |
|---|---|---|---|---|
| Rectangular Temple Hall Interior (downloaded as "Ornate Temple Hall Interior") | SMALL TEMPLE (Chisel) | 16 × 24 m | 1.7 (room ~10 × 15.5 m) | in bench |
| Empty Temple Hall Interior (downloaded as "Ornate Golden Temple Interior") | BUDDHA HALL (Chisel) | 20 × 28 m | 2.63 | in bench, handed to MuseXR-B |
| Enclosed Garden Courtyard | GARDEN (Chisel) | 20 × 30 m | 1.7 | in bench |
| Rectangular Art Gallery Interior (Van Gogh) | not in bench | 10 × 24 m | 2.8 (from collider) | handed to MuseXR-B; **not looked at**; spawn unverified |

**Hand-off to MuseXR-B.** Splats are git-ignored, so the Buddha hall and Van Gogh 500k `.spz`, their
colliders and a README with placement numbers were copied to
`MuseXR-B/Tools/marble/handoff/` (outside Assets/, because that project's editor was open). Its
agent session was messaged twice with the paths and import steps.

**Not done / next:**
- The headset APK predates the merged Buddha-hall versions; rebuild (MuseXR > Build > Quest > Bench)
  to see them on the Quest.
- Anti-aliasing opacity compensation (see its section) — the one untried lever that applies to every
  world at every tier.
- Credits: the Marble account ran out once on 28 Sep; Marble shows no per-step price.
