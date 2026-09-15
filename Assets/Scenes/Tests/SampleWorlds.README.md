# SampleWorlds

**The same cycling scene as `Worlds`, pointed at World Labs' five 500k samples instead of Skylar's
eight.** Build target **Samples** — `MuseXR > Build > {PICO|Quest} > Samples`.

~2.5M splats and ~116 MB against Skylar's ~29.8M and ~1.37 GB. **This is the realistic option**,
and the direct counterpart of MusePico's `SplatLoop`, which runs these exact five worlds.

## What it does

Identical to `Tests/Worlds` — `XR Origin (VR)`, both controllers, a `World Cycler` and a
`World Label` — with `WorldCycler.worldSet` set to `Samples`. Still streamed through Addressables,
against its own Addressable group so a Samples APK never quietly carries Skylar's 1.37 GB.

`World_van-gogh-inspired-gallery-interior` is still sitting in the scene as a leftover starting
world from `Worlds`, which is not a sample. Worth checking that is intended.

## Status

| | |
|---|---|
| Editor | Renders |
| PICO 4 hardware | **Never run** |
| Quest | **Never run** |

Untracked in git as of 14 Sep 2026, along with `Assets/Editor/AddressableWorldSetup.cs` — see the
worklist's first task. Untracked means git is not protecting it at all.

## What is missing

- **A device run**, which is the whole point of this variant. These five worlds are already
  verified rendering in a live XR session on the emulator *in MusePico* (13 Sep 2026), so the
  content is known-good; what is untested is this project's rig, render mode and build profile.
- **No measured spawns.** `WorldCatalog.Sample()` gives every sample `spawn = Vector2.zero`,
  `groundY = 0`, `yaw = 0`, `worldScale = 1` and `hasMeasuredSpawn = false`. The CDN samples carry
  no `semantics_metadata`, so there is no ground plane or metric scale to read. This is the same
  open defect MusePico has, and it has the same fix: either generate through the Marble API (which
  returns `metric_scale_factor` and `ground_plane_offset`) or compute a per-world statistic at
  conversion time — median Y is the promising one, since most splats are structure near the ground
  rather than sky.

## Notes

- **`m_renderMode` is `1` (Single Pass Instanced) and must be `0` (Multi Pass) before splats will
  draw.** See `Worlds.README.md` for why this fails silently. It applies to this scene equally.
- Because MusePico has proven these same five worlds render, this scene is the **best first
  hardware test in the project** — a failure here is far more likely to be MuseXR's rig or render
  mode than the content.
- The natural comparison to run is this APK against MusePico's `SplatLoop`: same worlds, same
  renderer, different Unity version, different project, real silicon versus translated x86. That is
  the cleanest available read on how much the emulator's ~3 FPS is Houdini rather than the splats.
