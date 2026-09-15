# SplatLoop

**The Gaussian-splat road, working.** Five World Labs sample worlds, shown one at a time from
inside, held about eight seconds each, then looped.

## What it does

`SplatWorldCycler` (`Assets/Scripts/Worlds/`) holds direct references to five converted
`GaussianSplatAsset`s, activates one at a time, and moves the XR Origin into it using
`SplatPlacement` — pure arithmetic that decides where to stand in a world that carries no
authored spawn. A head-locked TMP caption names the current world.

Deliberately simpler than MuseXR's equivalent, which loads through Addressables. That is right
there — Skylar's eight worlds are ~1.37 GB and cannot all be resident. Here it is five samples at
~23 MB each, 116 MB against the emulator's reported 5.9 GB, so direct references cost nothing and
remove a whole failure surface.

## Status

| | |
|---|---|
| Editor Play Mode | **Should now work** — the Editor runs Direct3D12 as of 13 Sep. Previously black: `SplatUtilities.compute` needs wave intrinsics, which DX11 does not have. Not re-checked since the switch |
| **PICO Emulator 6.1.0** | **Verified 13 Sep 2026** — screenshot showed the kitchen in full: cabinets, checkerboard floor, window light, caption over it |
| PICO hardware | Never run |
| Quest | Never run |

## What is missing

- **Two of the five worlds put the viewer in the sky.** The three interiors frame correctly; the
  two outdoor captures (`modern_house…`, `narrow_european_cobblestone_lane…`) have bounds running
  20–48 m below the walkable ground, so the eye lands above the sky dome looking at its underside.
  Bounds alone cannot separate the two cases. Still open — see the worklist question.
- No way to choose a world; it advances on a timer.
- Skylar's own eight worlds are not here, only World Labs' five samples.

## It is walkable as of 14 Sep 2026

It uses the shared `MuseXR Rig`, so there is locomotion, snap turn and controllers. Because the
World Labs samples ship **no collider mesh at all** — the CDN export is splats only — `PhysicsBounds`
builds an invisible floor and four walls from each world's own splat bounds, inset 0.5 m so the
visitor cannot press into the noisy outer shell of the capture. Without that box the
`CharacterController` has nothing to stand on and gravity drops you out of the world on frame one.

The floor goes at the origin Y that `SplatPlacement` computes, which under Floor tracking is the
standing surface. So the same heuristic that decides where you stand also decides where the floor
is — **including on the two outdoor captures where that heuristic is still wrong**, and the floor
will be up in the sky with you.

## Notes

Three things that each render a black screen with **no error at all**, all learned here:

1. **OpenXR render mode must be Multi Pass.** The splat renderer's Single Pass Instanced support
   is an unmerged upstream PR (#173). Under the XR default every health signal reads fine —
   valid asset, valid render setup, all seven compute kernels present, splat counts logged — and
   nothing draws. Costs roughly double per eye.
2. **`GaussianSplatURPFeature` must be on the renderer** (`Mobile_Renderer` *and* `PC_Renderer`),
   with Render Graph Compatibility Mode OFF.
3. A compute kernel the platform cannot run — which is the DX11 case above.

The capability dump in `SplatWorldCycler` exists precisely because none of these throw.

**Do not copy assets from MuseXR.** It is Unity 6000.3.12f1 and this project is 6000.0.73f1 — a
downgrade, which Unity does not support for serialized assets. Reconvert from the `.spz` sources.
The check that it worked is the bounds: they must match MuseXR's to the decimal. Zeroed bounds
mean the embedded shLevel-0 SH patch is missing.
