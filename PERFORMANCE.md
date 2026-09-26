# Splat performance on standalone headsets

How the intro went from **24 to 72 FPS on a Quest 3S** (25 Sep 2026), what each change costs,
how it was measured, and how to take it further. Branch `perf/splat-framerate`.

## Result

Intro stage (Threshold Conservatory), release builds, Quest 3S, 72 Hz display.

| Build | FPS | GPU ms/frame | Commit |
|---|---|---|---|
| Original (500k splats) | 24-25 | ~30 | `ee2943e` |
| Visibility-pruned world (339,578) | 24-36, oscillating | 20.7-26.1 | `2cfe4ec` |
| + one splat sort per frame | 36-37 | 17.9-24.6 | `2cfe4ec` |
| + SH order 0, SH fetch skipped | 36-37 | 21.1-23.5 | `5852849` |
| + splat layer at 0.8 of the target | 53-55 | 15.4 | `60d5cbf` |
| + splat layer at 0.7 | 59-65 | 12.5-15.6 | `60d5cbf` |
| **+ splat layer at 0.6 (default)** | **72-73** | **11.5** | `90409ed` |

The last four rows were measured in one session by cycling the splat scale live, and 1.0 in that
same session read 36-37 FPS / 20-22 ms, matching the build before the change.

PICO 4 with the same build: 30 FPS of 90, ~30 ms GPU (22-23 FPS before). Not yet broken down.

Reported in the headset after the change: the distortion under head movement that had been
causing headaches has eased. An observation, not a measurement.

## Test log — one variable per test

Every test changes ONE thing, is built from a commit, and that commit is tagged `perf-test/Txx`
(`git checkout perf-test/T07` rebuilds exactly what was measured). A rejected test is reverted with
a new commit, never erased. Quest 3S unless noted. From T11 on, every row is measured by
`Tools/perf/measure_quest.sh` (capture mode, fixed view, F9 splat-scale sweep) and its raw numbers
are in `Tools/perf/results.csv`.
`Tools/perf/compare.sh <baseline> <test>` diffs two runs scale by scale and flags any GPU time
more than 0.7 ms worse (the run-to-run noise measured by T10 vs T11).

| Test | Tag / commit | The one variable | World | Default (splat 0.6) | At splat 1.0 | What it looked like | Verdict |
|---|---|---|---|---|---|---|---|
| T01 | `ee2943e` | Starting point, no changes | intro | 24-25 FPS, 26-30 ms | (no splat layer yet) | reference | baseline |
| T02 | *none* ¹ | Intro world visibility-pruned 500k → 340k | intro | 24-36 FPS (oscillating), 21-26 ms | — | nothing missing (0.000% of pixels >8/255 on 40 unseen views) | **kept** |
| T03 | `2cfe4ec` | One splat sort per frame (`SortNthPass 2`) | intro | 36-37 FPS, 18-25 ms | — | no shimmer on fast turns | **kept** |
| T04 | `5852849` | SH order 0 + skip the all-zero SH fetch | intro | 36-37 FPS, 21-24 ms; compute 29→21% | — | pixel-identical in Editor A/B | **kept** |
| T05 | `eac166d` | URP renderScale 0.8 → 0.65 for everything | intro | 54-57 FPS, 15.0 ms | — | text and meshes visibly softer | **rejected** (probe only) |
| T06 | `60d5cbf` | Splat layer at its own resolution, 0.8 | intro | 53-55 FPS, 15.4 ms | 36-37 FPS, 20-22 ms | text sharp; fine splat detail slightly soft | **kept** |
| T07 | `90409ed` | Splat layer default 0.8 → 0.6 | intro | **72-73 FPS, 11.5 ms** | 36-37 FPS, 20-22 ms | softer fine detail, blocky paving specks | **kept** |
| T08 | `6dbdb1f` | World: intro → Celestial Peach Blossom (unpruned) | peach | 56-58 FPS, 15.1 ms | 28-37 FPS, 24.5 ms | — (content change, not an optimisation) | reference |
| T09 | `99d9c24` | + 4 Tripo props (210k tris, textures uncompressed) | peach | 36-48 FPS, 17.5 ms | 30-32 FPS, 25.9 ms | props read well, float on the lake | content kept |
| T10 | `ae13fb8` | Props' textures compressed (ASTC 6x6) | peach | 36-48 FPS, 17.5 ms | 30 FPS, 25.8 ms | no visible difference; APK −32 MB | **kept** (memory, not speed) |
| T11 | `2d1c335` | *Pipeline check:* committed code + launch-option world, no scene edit | peach | 36-49 FPS, 17.3 ms | 30-33 FPS, 25.2 ms | fixed view identical to T10 (0.52/255) | **method validated** — every scale within 0.6 ms of T10 |
| T12 | `384592d` | Props material: glTF PBR → URP Simple Lit | peach | **48-49 FPS, 16.0 ms** | 36-37 FPS, 24.0 ms | headset crops: the big buddha loses its metallic highlights and reads flatter, more orange; the golden-buddha group and temple near-identical | **research** — kept as the comparison point for the replacement props (due 27 Sep); 0.9-1.3 ms saved at every scale |
| T13 | `9dc7221` (tag `perf-baseline/T13`) | *Baseline* before the sort test: T12 build re-measured on both worlds | peach / intro | 48-49 FPS, 15.9 ms / 72-73 FPS, 11.7 ms | 36-37, 24.0 ms / 35-37, 22.7 ms | reproduces T12 (≤0.3 ms) and T07 | **baseline** — `compare.sh` checks later runs against it |
| T14 | `b007009` | Sort splats every other frame (`SortNthPass` 2 → 4) | peach | **62 FPS steady, 14.3 ms** (−1.6) | 35-37 FPS, 22.4 ms (−1.6) | static view identical (0.4% px >8/255, overlay only); compute 43 → 32% | pending Saul's head-turn check |
| T14 | `b007009` | same build | intro | 72-73 FPS, **10.5 ms** (−1.2); 0.7 now also holds 72 | 37 FPS, 21.1 ms (−1.6) | static view identical (1.7% px, overlay only) | pending Saul's head-turn check |
| T15 | `9f25b63` | Quest trades one CPU level for one GPU level (`com.oculus.trade_cpu_for_gpu_amount=1`), on top of T14 | peach | **69-70 FPS, 13.2 ms** (−1.1 vs T14, −2.7 vs baseline) | 36-37 FPS, 20.7 ms | GPU level 4 → **5**, 545 → **599 MHz**; CPU stays level 2-3, <30% busy. Capture differs only by a sub-pixel shift (whole-frame edge outlines) | **kept** pending the T14 head-turn check; thermals over a long session unmeasured |
| T15 | `9f25b63` | same build | intro | 72-73 FPS, **9.7 ms** (−0.8 vs T14, −2.0 vs baseline) | 36-37 FPS, 21.0 ms | same levels; capture 0.03% px changed | as above |

¹ Built from an uncommitted one-line state (sort change held back), so it cannot be checked out
exactly. From T11 on every test is committed before it is built.

## Celestial Peach Blossom Paradise, 26 Sep 2026

Chapter 01's world, unpruned (500,000 splats), with and without the four Tripo props
(`Assets/Props/Peach`, 210k triangles). Quest 3S, both builds launched in **capture mode** so the
view is identical (the screenshots line up exactly), splat scale stepped by `adb shell input keyevent
KEYCODE_F9` with nobody touching the headset. Test builds: branches `probe/peach-home` and
`probe/peach-objects`.

| Splat scale | World alone | + 4 props |
|---|---|---|
| 1.0 | 28-37 FPS, 24.5 ms | 30-32 FPS, 25.9 ms |
| 0.9 | 36 FPS, 21.2 ms | 36-37 FPS, 22.9 ms |
| 0.8 | 36-37 FPS, 18.5 ms | 36-37 FPS, 20.3 ms |
| 0.7 | 39-49 FPS, 16.7 ms | 36-37 FPS, 18.4 ms |
| **0.6 (default)** | **56-58 FPS, 15.1 ms** | **36-48 FPS, 17.5 ms** |

- **The props cost 1.4-2.4 ms per frame** at every scale — about 2 ms at the default, enough to
  fall off the 57 FPS step.
- **Compressing their textures did NOT change that** (tested, `probe/peach-objects-astc`): 17.5 ms
  at 0.6 either way, and every scale within 0.2 ms. Moving the textures out of the .glb so Unity
  compresses them (ASTC 6x6) cut textures in the build from 158 to 86.5 MB and the APK by 32 MB,
  with no visible difference in headset crops — worth keeping for memory, but it is not where the
  frame time goes. The GPU split points at pixel shading instead: fragments rose from 44% to 51%
  of GPU time with the props, i.e. glTFast's full PBR material on large objects.
- **The world misses 72 FPS on its own** because it is not pruned yet: compute is 41% of GPU time
  against 21% on the pruned intro. The first scan says about half its splats are invisible from
  the spawn. Waiting on the Marble collider and a real spawn.
- Capture mode is for the desk only. Worn, it looks like the whole world is stuck to your head,
  which it is: head tracking is paused.

## What each change does, and costs

| Change | Where | Visible cost |
|---|---|---|
| **Visibility pruning.** Delete splats that never add 1/255 to any pixel from anywhere the head can be | `Tools/splat/prune.py`, world `grand-conservatory-garden-path-cut-500k` | None found: 0.000% of pixels move >8/255 on 40 held-out views; a random cut of the same size moves 13% |
| **One sort per frame.** The package counts render *passes*; Multi Pass runs two a frame, so `SortNthPass = 2` sorts on the left eye and reuses it for the right | `SplatRenderTuning.SortNthPass` | None seen on fast head turns |
| **SH order 0.** Every world here is SH degree 0, so the SH buffer is zeros; its fetch is now skipped | `SplatRenderTuning.SHOrder`, `LoadSplatData(idx, loadSH)` | None: Editor A/B max pixel difference 0 |
| **Splat layer at reduced resolution.** Only the splats render smaller; text, UI and meshes keep full resolution | `GaussianSplatSettings.ResolutionScale`, `SplatRenderTuning.SplatResolutionScale` | At 0.6 fine splat detail is softer and the paving's tiny white specks become small squares |

Everything else in the render pipeline is unchanged: URP `renderScale` stays 0.8, MSAA 1, HDR on.
See `../CLAUDE.md`, "The render pipeline settings are load-bearing for splats".

### The splat layer, in detail

Changes are in the embedded package `Packages/org.nesnausk.gaussian-splatting` and all marked
`MuseXR`. At scale 1.0 the package's original path runs unchanged.

- The splat render target is sized by the scale; splat footprints stay in full-resolution pixel
  units, so splats are the same size on screen.
- Composite **pass 1** copies the camera depth to the splat target's size, keeping the
  **farthest** depth of each 2x2 block. Geometry still hides the world behind it; at silhouettes
  the world overlaps an object by about a pixel rather than leaving a gap.
- Composite **pass 0** samples bilinearly and restores colour to the strongest of the four taps'
  coverage. Without that, the premultiplied target blended twice drew a dark line along every
  object edge.
- Only Multi Pass (2D targets). Anything else falls back to the original path.

## Controls for testing

| Control | What it does | Log tag |
|---|---|---|
| Left **X** (F9 in the Editor) | Cycles the splat scale 1.0 / 0.9 / 0.8 / 0.7 / 0.6 live | `[SplatScale]` |
| Launch flag `--ez musexr.capturePose true` (F10 in the Editor) | Capture mode: head tracking paused, camera fixed at the stage origin, 1.6 m, level. Two builds then give pixel-identical screenshots. Not for wearing | `[CapturePose]` |
| always on | Splat target vs eye texture size, on passes 1 / 100 / 1000 | `[SplatDiag]` |

The app also launches with the controllers asleep now: the manifest declares hand support
(`Assets/Editor/Manifest/HandsLaunchManifest.cs`), which stops Quest's "Controller required"
dialog and PICO's hand dialog. Verified on PICO 4. It only declares hands; input still comes from
the controllers.

## Measuring

```bash
# Quest: FPS and GPU time per second ("App=" is GPU ms)
adb logcat -s VrApi:I | grep -o "FPS=[^,]*\|App=[^,]*"
# Quest: GPU time split (fragments / vertices / compute) and bus load
adb shell "ovrgpuprofiler -r=\"3,24,31,42\""
# PICO: FPS and GPU time
adb logcat -s PxrMetric:I
# Both: a screenshot of both eyes
adb exec-out screencap -p > shot.png          # from bash, never PowerShell
# Capture mode for pixel-identical comparisons between builds
adb shell am start -n com.musexr.impossiblemuseum/com.unity3d.player.UnityPlayerActivity --ez musexr.capturePose true
```

The script also prints the CPU/GPU clock levels per scale (`CPU4/GPU=cpu/gpu,MHz`) and whether the
CPU-for-GPU trade is active (`tradeCpuForGpu is 1`).

Before trusting any number, check the app is in front
(`adb shell dumpsys activity activities | grep topResumedActivity`). With a system dialog up
(controller required, tracking lost) the counters read the system shell, not the app.

Traps met on the way:
- **Editor captures misplace the splat layer** (bottom-left, the renderScale fraction). Judge
  splat framing only from headset captures.
- **An alignment check dominated by a static element proves nothing.** The lens border and the
  OVR Metrics overlay made two headset captures look aligned when the head had moved. Match
  scene features instead, or use capture mode.

## Taking it further

- **Prune the other worlds.** Each needs its movement area decided first: head-only stages cut
  hard, walkable ones less. Same tools; validate at random positions inside the area, not just
  the sampled ones.
- **Measure the other stages.** Only the intro is measured. Walkable stages need a range and the
  worst spot, not one number.
- **PICO.** 30 FPS of 90 with GPU time pinned near 30 ms; find out whether that is real work or
  the 90 Hz display. 72 Hz is a cheap test.
- **Before building anything lossy**, model it in `Tools/splat` first (`pixel_options.py`
  measures fragments saved against image change). A per-splat size cap was ruled out that way:
  2-13% saved for visible damage.
