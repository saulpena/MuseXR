# XRRig

**The rig, tracking and controllers with no content on top.** Build target **Rig** —
`MuseXR > Build > {PICO|Quest} > Rig` ships exactly this scene.

This is MuseXR's equivalent of MusePico's `SampleScene`: the scene you reach for when a build
launches on a new device and shows nothing, because if the posts and the cube are there the problem
is downstream of the rig.

## What it does

An `XR Origin (VR)` with a `Camera Offset`, `Main Camera` and both controllers — `Left Controller`
and `Right Controller`, each with a controller model and a ray.

On top of that, a deliberate scale reference, which is the point of the scene:

- a `Floor`
- a `Target Cube`
- `Post 1m`, `Post 2m`, `Post 3m`, `Post 5m` and a `1m Scale Reference`

The posts exist so that "is the world the right size" can be answered by looking rather than
guessed. A splat world with the wrong `worldScale` reads as a plausible room until you stand next
to a known metre.

## Status

| | |
|---|---|
| Editor | Renders |
| PICO 4 hardware | **Never run** |
| Quest | **Never run** |
| PICO Emulator | Not applicable — this project does not run on the emulator |

**Nothing in MuseXR has been verified on a headset.** The project has been dormant since the
emulator road began; the last commit is `23d1a75`. Treat every row above as unconfirmed until
someone runs it, including a clean build.

## What is missing

- **A device run.** This is the cheapest possible thing to put on a Quest, and it is the
  prerequisite for believing anything else in the project.
- No locomotion. You can look around and see your controllers; you cannot move.
  `WalkRig` (ported from MusePico) is the thing that would change that.

## Notes

- `XRBuild.cs` builds this with **no Addressable group**, so the Rig APK carries none of the
  1.37 GB of worlds. It is meant to be small and fast to install.
- MusePico's lesson applies here unchanged: **a scene that renders black is usually an empty scene,
  not a broken renderer.** The posts and cube are this scene's version of that guarantee.
