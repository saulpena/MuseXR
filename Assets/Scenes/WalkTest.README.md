# WalkTest

**Walking, in one of Skylar's worlds, on the emulator.** Her `van-gogh-inspired-gallery-interior`
splat and her collider GLB, neither reconverted nor re-derived, walked with the shared
`MuseXR Rig`.

Branched from `SplatLoop`: same XR rig, the World Cycler replaced by `WalkRig`.

## What it does

`WalkRig` (`Assets/Scripts/Worlds/`) builds the splat renderer, instantiates the collider GLB as
hidden `MeshCollider` geometry, closes her walk box with `PhysicsBounds`, and drops the visitor at
her recorded spawn.

**It does not move the visitor.** Locomotion, turning, gravity, walls and step-up belong to the
shared rig's `CharacterController` and `DynamicMoveProvider` (see `README.md` in this folder).
Up to 14 Sep 2026 `WalkRig` did its own thumbstick reading, wall raycasts and per-frame ground
snapping; that duplicated the engine badly and would now fight the move provider for the same
transform every frame. `WalkGround.PickGroundHeight` survives and is still used once, to seat the
visitor at spawn; `ClampToBounds` and `WalkForward` were deleted with their tests.

## Status

| | |
|---|---|
| EditMode tests | **9 passing**, inside a suite of 146 (5 retired with the hand-rolled locomotion) |
| Editor Play Mode | **Verified 14 Sep 2026** — renders, places at spawn, walls solid. Locomotion exercised by driving the origin, not by input |
| **PICO Emulator 6.1.0** | **Verified 14 Sep 2026** — session reached `FOCUSED`, splat rendered, walked 2.5 m forward over adb with ground snapping the whole way |
| PICO hardware | Never run |
| Quest | Never run |

## The three things it proved

1. **Converted worlds copy between the two projects.** These assets came from `MuseXR/Assets/Worlds`
   (Unity 6000.3) into this project (6000.0) with no reconversion. Legal because both run
   gaussian-splatting **v1.1.1**, so `GaussianSplatAsset`'s script GUID and its five `.bytes`
   references resolve. Verified by the bounds: `(-22.75, -19.03, -27.63)..(29.34, 45.72, 26.81)`,
   identical to MuseXR, and `validAsset`/`validRenderSetup` both true on device.
2. **Her collider GLBs are the ground.** A splat cannot be raycast; the collider can. It is imported
   by glTFast, kept invisible, and exists only for physics queries. No conversion — a `.glb` is a
   source format, so the downgrade rule never applied to it.
3. **The collider arrives mirrored, and must be flipped on X.** glTFast negates X importing glTF
   into Unity's left-handed space; three.js keeps glTF coordinates, and our `.spz` converter matches
   three.js. Measured, not assumed:

   | | ray at her spawn (1.79, 0.30) | bounds centre X |
   |---|---|---|
   | as imported | **no hit** | −4.24 |
   | X negated | **y = −0.011** | +4.24 (her `worlds.js`: 3.77) |

   Her recorded `groundY` is `0.0`.

## What is missing

- **The collider is a floor-and-walls shell with no furniture, so you walk through the bed.** Ground
  stayed at 0.02 crossing it. Her browser build has the same gap; it matters more in a headset.
- **The collider floor is a patch, not a full floor.** Along the gallery at x=1.79 it covers
  z ≈ −3 to +8 and nothing outside (12/28 sample rays hit). Her `bounds` is the bounding box of the
  *whole* collider — walls and ceiling included — not a walkable-area map. So `WalkGround`'s third
  tier, falling back to the flat profile ground, is load-bearing here rather than a safety net.
- **Only one world.** The APK is 355 MB for it. Eight will not ship this way; Addressables is now a
  requirement, and `MuseXR/Assets/Editor/AddressableWorldSetup.cs` is the thing to bring over.
- **`bounds` is hardcoded on the component.** It belongs in a world catalogue beside spawn/ground/
  yaw — `WorldCatalog` already carries the rest of Skylar's per-world data but not this. It now
  also sizes the `PhysicsBounds` floor and walls, so getting it wrong is a wall in the wrong place
  rather than only a clamp in the wrong place.
- **Seven of her nine collider GLBs are not imported.** Only `van-gogh` and `yellow-polka-dot` are
  in `Assets/Worlds/Colliders/`; the rest sit in `muse-infinity/assets/worlds/`.
- No snap-turn or wall-collision test on device; both are written but only the forward walk was
  driven.

## Driving it over adb

**Superseded 14 Sep 2026.** The hand-rolled keyboard bindings are gone; movement now comes from the
rig's `XRI Default Input Actions`, which are bound to controllers rather than to a keyboard. The
old note — that a plain `adb shell input keyevent` lands entirely between two frames at ~3 FPS and
a polled `ReadValue` never sees it, so `--longpress` was required — is kept because it is still
true of any polled binding, and it is the reason 25 taps once produced a byte-identical screenshot.
Driving XR controller input over adb is a different problem and is not solved here.

## Notes

- The HUD prints `origin` and `head` separately on purpose. On the emulator they differ — the
  simulated 6DoF headset moves the head within the tracking space while locomotion moves the origin,
  and seeing both confirms the two transforms compose instead of fighting.
- `worldScale` is **1**, not Skylar's 1.7. She scaled worlds up so companions read at a decent size
  in a browser viewport; in a headset metres should be metres, and scaling the world up scales the
  visitor down.
- The Vulkan warnings about `_SplatViewData` / `_SplatSelectedBits` appear **exactly once**, before
  the first frame. That is a startup transient, not the repeating `_OrderBuffer` warning that means
  Single Pass Instanced is silently skipping every draw.
