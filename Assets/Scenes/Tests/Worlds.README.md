# Worlds

**Skylar's eight worlds at full resolution, cycled from inside, streamed through Addressables.**
Build target **Worlds** — `MuseXR > Build > {PICO|Quest} > Worlds`.

This is the scene MusePico's `SplatLoop` was ported *from*, and the two have diverged on purpose:
this one streams, because 1.37 GB cannot be resident.

## What it does

An `XR Origin (VR)` with both controllers, a `World Cycler` and a `World Label`.

`WorldCycler` is set to `worldSet: Skylar`, `autoAdvance: true`, `secondsPerWorld: 15`. It loads
one world at a time through Addressables, releases it before taking the next, and places the
visitor using `WorldCatalog` — spawn, ground height, yaw, `worldScale` and `cameraFar` per world.
`World_van-gogh-inspired-gallery-interior` is present in the scene as the starting world.

**The placement data is the asset here.** `WorldCatalog.cs` carries hand-measured values for all
eight worlds, ported from `muse-infinity/config/worlds.js` and tuned across 27 commits of
playtesting — two of them carry the commit message that produced them in a comment
(`coastal-villa spawn re-derived from collider (visitor started in the sky)`,
`sunlit-palace spawn advanced ~20m to the courtyard before the facade`). Every Skylar world has
`hasMeasuredSpawn = true`. MusePico has none of this and infers placement from asset bounds
instead, which is exactly why two of its five sample worlds put the eye in the sky.

## Status

| | |
|---|---|
| Editor | Renders — spawns were verified by eye (commit `74bc124`) |
| PICO 4 hardware | **Never run** |
| Quest | **Never run** |

**Expected to struggle, and built to measure how badly.** These are 1.9M–4.3M splats per world;
World Labs' own docs say 2M+ crashes standalone VR. That measurement has never been taken.

## What is missing

- **Any performance number at all.** This is the project's largest open question and the only one
  hardware can answer. The emulator structurally cannot — it renders through CPU translation on a
  desktop RTX 5090, so neither its CPU nor GPU figures transfer.
- **A decision on re-converting at a lower tier.** Open on the worklist. The alternative to
  measuring is simply landing in a budget that runs.

## Notes

- **`m_renderMode` is currently `1` — Single Pass Instanced — and that will draw nothing.**
  Measured in MusePico: the splat renderer's Single Pass Instanced support is an unmerged upstream
  PR (#173). Under it every health signal reads fine — valid asset, valid render setup, all seven
  compute kernels present, splat counts logged — and no splat is drawn. The only clue is a Vulkan
  warning about a missing `_OrderBuffer` compute buffer. MusePico runs Multi Pass (`0`), which is
  the whole difference between a room and a black screen. **MuseXR has never rendered splats on a
  device, so this has never had the chance to be caught here.** Fix before the first hardware run,
  and expect roughly double cost per eye.
- `GaussianSplatURPFeature` **is** present on both `Mobile_Renderer` and `PC_Renderer`, so that
  half of the requirement is already satisfied.
- `WorldCycler` labels with a legacy **`TextMesh`**, not TMP. MusePico's `SplatWorldCycler` — now
  ported into `Assets/Scripts/Worlds/` — uses TMP, which is the project convention. Switching is
  the cheap way to converge the two.
- Addressables belongs here and deliberately not in MusePico: one bundle per world, LZ4, loaded and
  released one at a time. If Skylar's worlds ever move to MusePico, `AddressableWorldSetup.cs`
  moves with them.
