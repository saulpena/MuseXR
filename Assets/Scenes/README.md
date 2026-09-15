# Scenes

Eight scenes, **all using the same rig**. If a scene here does not have `MuseXR Rig` in it, that
is a bug.

| Scene | Build target | What it is for |
|---|---|---|
| `Tests/XRRig` | **Rig** | Rig, tracking and controllers with nothing on top. First thing to put on a new device |
| `Tests/Worlds` | **Worlds** | Skylar's 8 at full resolution, streamed through Addressables (~1.5 GB) |
| `Tests/SampleWorlds` | **Samples** | The 5 World Labs samples through the *same Addressables path*, at 116 MB — the cheap test of the streaming code |
| `SplatLoop` | **SplatLoop** | The same 5 samples by direct reference, walkable |
| `WalkTest` | **Walk** | Skylar's van-gogh gallery with her collider, walkable |
| `TripoGallery` | **Gallery** | The five Tripo characters on plinths |
| `TripoRuntime` | **Generate** | Runtime generation bench |
| `MuseumSalon` | **Salon** | Three masters answering a spoken question |

## The rig

`Assets/Prefabs/MuseXR Rig.prefab` — a **prefab variant** of XRI 3.6's
`Samples/XR Interaction Toolkit/3.6.0/Starter Assets/Prefabs/XR Origin (XR Rig)`. Being a variant
is the point: upstream fixes arrive with a package update, and everything we changed shows up as an
override rather than as a fork nobody can diff.

It carries, all first-party Unity code:

- 6DoF head and both controllers (`TrackedPoseDriver`), plus gaze
- `DynamicMoveProvider` (left stick), `SnapTurnProvider` + `ContinuousTurnProvider` (right stick),
  `TeleportationProvider`, `GravityProvider`, `JumpProvider`, `ClimbProvider`
- a `CharacterController` — Unity's own character-vs-world solver, not raycasts
- `NearFarInteractor`, `XRPokeInteractor`, teleport ray, `XRInteractionManager`
- `XRI Default Input Actions`, **actually assigned**

**One override from stock:** tracking origin mode is **Floor**. Starter Assets ships `NotSpecified`.

**One rig covers PICO 4 and Quest.** The actions bind to the generic `<XRController>` layout, so
nothing in the rig is vendor-specific — the per-vendor difference lives in the OpenXR interaction
profiles that `MuseXR > Switch To > PICO | Quest` swaps, which is where it belongs.

## Why splat scenes need an invisible floor

**A Gaussian splat is not geometry.** It cannot be collided with or raycast, so a world made only
of splats has nothing for the `CharacterController` to stand on — under gravity the visitor falls
out of it on the first frame. `PhysicsBounds` closes a walkable box with an invisible floor and
four walls. Two different situations end there:

- **Skylar's worlds** ship a `*-collider.glb`, which is real geometry and does the real work. The
  box is a *backstop*: her collider is a shell with no furniture, and its floor is a patch rather
  than a full floor — along the van-gogh gallery only 12 of 28 sample rays hit anything, and a hole
  under gravity is a bottomless fall.
- **World Labs' samples** ship no collider and never will; the CDN export is splats only. Here the
  box **is** the floor.

## What we have of Skylar's data, and what we do not

| | Her 8 worlds | The 5 samples |
|---|---|---|
| Spawn / ground / yaw / bounds | **Yes** — `WorldCatalog`, from `config/worlds.js`, tuned over 27 commits, `hasMeasuredSpawn = true` | **No** — zeros; `SplatPlacement` infers from asset bounds |
| Collider mesh | **2 of 9 present** (`van-gogh`, `yellow-polka-dot`). The other 7 are in her repo, not yet imported | None exist |

**The two gaps worth knowing:** seven of her collider GLBs are sitting unused in
`muse-infinity/assets/worlds/`, and the samples' inferred placement still puts the eye above the
sky dome on the two outdoor captures (`modern_house…`, `narrow_european_cobblestone_lane…`),
because bounds alone cannot tell an interior from a landscape.

## Deleted 14 Sep 2026

`SampleScene` (bare URP template, no XR, in no build) and `Tests/World` (flat no-XR splat import
probe; the SH-patch finding it existed to prove is recorded in the worklist). Both are recoverable
from git history.
