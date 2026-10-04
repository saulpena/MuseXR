# The Gate walk — start of the journey to the Grotto, in one scene

`Assets/Scenes/Tests/GateWorld.unity`. Her stages 2.1–2.4 in order, then the Palace's moon gate
into the Grotto. Owner of this stretch: the main MuseXR session; the chapters' interactions
(Palace, Grotto) belong to the musexr-b agent and are used here unchanged.

## What the visitor does

| Her stage | In the headset | Code |
|---|---|---|
| 2.1 The Gate | Point at a sample question or hold X to speak one. It is lettered over the arch. | `Journey/GateStage.cs` |
| 2.2 Company | Six masters in a row 4.5 m ahead. Point + trigger invites 1–3 (gold ring, "Invited"); rose ring on hover. A confirms. Each answers in one line, **spoken in their MiniMax voice from where they stand**, subtitle over their head; A skips ahead. | `JourneyOpening/JourneyOpening.cs`, `Interaction/CompanyStage.cs` |
| 2.3 Curation | Four generated lanterns light along the walk (Palace on the path, the rest to alternate sides, all inside the 8 m pointer reach). The companions move to the visitor's flanks and come along. Point + trigger at a lantern: its line on the card, **spoken by a companion**. Once one is heard, A turns the first lantern into the moon gate. No timer. | `JourneyOpening/JourneyCuration.cs` |
| → 2.4 | Walk or teleport through the moon gate (shared `Moon Gate.prefab`, round keyhole, her generated gate as the frame). The Palace replaces the conservatory; the companions stand on the Palace's three marks. | `Worlds/MoonGate.cs`, `JourneyCuration.Arrive` |
| 2.4 Palace | Grip the crane or turtle, turn it with the stick, set it in the court, pick a reason, A keeps it. | `Interaction/PalaceChapterInteractions.cs` |
| → Grotto | "Kept. The moon gate is open, on your right." The Palace's own ring opens onto the Grotto; walk through. | `Moon Gate to Grotto` in the scene |

## Companion rules (hers) and where each is enforced

| Rule | Where | State |
|---|---|---|
| Off the main path, 1.5–2.2 m | `Slots/Companions.cs` (answering: -32/+26/+54°, 2.0–2.2 m); `JourneyCuration.Flanks` (walk: -52/+32/+58°, 1.7–2.1 m) | done |
| Within ±60° of forward, never behind | same marks; `CompanionGroup` re-marks after 1.5 s if one is >100° off gaze | done |
| After a teleport, to new marks next frame, no walking or clipping | `CompanionGroup.PlaceAll` on a teleport or snap turn | **snaps, does not fade yet** |
| Speaker gets a floor ring; others turn their heads toward them | `CompanionGroup.ActiveSpeaker` (gold ring, others turn the whole body toward the speaker) | done; whole body, not head bone |

## Voices

`MuseumDialogue.VoiceAsync` returns one line as a clip in a master's voice (MiniMax
`speech-2.8-turbo`, voice ids from `masters.json`). `JourneyOpening.VoiceFor` caches per
speaker+line and is called ahead: answers when the company is chosen, lantern lines when the
lanterns light. `JourneyOpening.Say` plays it from a 3D `AudioSource` on the figure. Measured in the
Editor on 3 Oct: Monet's answer 10.2 s, his Palace-lantern line 8.0 s. With no MiniMax key the turns
fall back to reading time (`CompanionGroup.EstimateSeconds`).

## Answers and lantern lines

Baked for her four sample questions (`Resources/OpeningAnswers.json`, `Resources/LanternLines.json`),
so they appear at once; any other question is asked live (`gpt-5.6-luna`) the moment the doors
open. A failed lantern call shows a written line, labelled as the fallback.

## Traps found on this stretch

- **Splats draw nothing in Editor captures** at the rig's splat scale 0.6: set
  `GaussianSplatSettings.ResolutionScale = 1` (and URP renderScale 1) for captures. Fine on device.
- **Arriving in a world with no floor yet drops the rig forever.** `Arrive` holds the
  `GravityProvider` off until the Palace's `Teleport Floor` exists (the Palace now also makes it in `Awake`).
- **The masters' invitation colliders caught the laser** after they were chosen, so lanterns behind
  them could not be pointed at. They are switched off when the lanterns light.
- **Pointer reach is 8 m.** Lanterns at 6.5 m + 2.6 m steps were mostly out of reach.
- The answer test harness must pass `float`s to `Quaternion.Euler` (`%ff`); doubles fail to compile
  silently inside `execute_code` and the camera never moves.

## Open

- Companions should **fade** to their new marks after a teleport (her rule); they snap.
- The Grotto's own interactions are not loaded in this scene yet (musexr-b is building them in
  `Tests/GrottoChapter.unity`).
- Lantern lines are not voiced for a visitor who has no companions (none chosen).
