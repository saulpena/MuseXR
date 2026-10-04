# Moon Gate — the chapter transition, as a prefab

One transition for every chapter that ends in a moon gate (chatplan Q2: portal doors, the next world
showing through, the visitor walks or teleports through — no fades; Curate/Palace = moon gate, a round
mask). The Palace → Grotto run in `Assets/Scenes/Tests/PalaceChapter.unity` uses exactly this.

## What it does

1. Nothing shows until `MoonGate.Open(...)` is called (the Palace calls it when A keeps the choice).
2. The next world is loaded **behind** the gate, placed so its playtested spawn lies 1.2 m through it,
   facing on.
3. When the visitor is within 10 m and looks toward the gate, the next world reveals **inside a keyhole**
   (round opening on a passage) — no door leaves; a capture's own stone ring is the frame.
4. Walking or teleporting through: `Crossed` fires, the props you passed are hidden; once the door has
   shut the old world and those props are destroyed and `Arrived` fires.

Code: `Assets/Scripts/Worlds/MoonGate.cs` (namespace `MuseXR.Worlds`, assembly `MusePico.Worlds`).
It drives `Assets/Prefabs/Portal Door.prefab`'s `SplatPortalDoor` unchanged.

## Use it in a scene — three steps

1. **Drag `Assets/Prefabs/Moon Gate.prefab` into the scene.** Place it in the Scene view:
   - position: centre of the gate's threshold, **on the floor**;
   - rotation: the **cyan line** (its +Z) points **out through the gate, into the next world**.
   The gold gizmo is the keyhole; line it up with the capture's own gate.
2. **Inspector:** set `Next World Asset` (the converted `…-500k` `GaussianSplatAsset`) and
   `Next World Key` (its key in `WorldCatalog.Small`, e.g. `palace-court-of-keeping-500k`).
   `Door Prefab` is already set.
3. **From code, when the chapter is done:**
   ```csharp
   moonGate.Open(currentWorldRenderer, propsToLeaveBehind);   // returns false and logs what is missing
   moonGate.Crossed += () => { /* in the next world: re-place companions, etc. */ };
   ```
   Anything that should come along (companions) must be moved **out** of `propsToLeaveBehind` first —
   they are destroyed with the old world.

## Things that cost time

- **Find the gate in the capture by eye, not from a diagram or the collider.** In the palace the layout's
  schematic "Exit Moon gate" marker stood 3.5 m from the capture's real gate, in front of a column, and
  the collider reports a sloping surface across the opening above 1.4 m. The palace gate was located by
  triangulating two Editor captures: threshold at **(-7.5, 0, 0)**, opening along **-X**.
- **Editor captures do not show splats** unless the splat layer is at full scale for the render:
  set `GaussianSplatSettings.ResolutionScale = 1` around the probe camera's `Render()`, then restore it
  (default 0.6). With a Link headset active the XR camera's own capture comes back white.
- The keyhole size is the palace capture's gate (radius 1.6 m, centre 2.3 m up, passage 2.1 m) —
  constants in `MoonGate`. A different gate shape would need its own numbers.
- Tests: `Assets/Tests/EditMode/MoonGateTests.cs` (the keyhole shape and mask mesh).
