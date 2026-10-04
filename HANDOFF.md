# HANDOFF — MuseXR journey, state on 4 Oct 2026

For a fresh agent picking this up. Read this, then `CLAUDE.md` here and `../CLAUDE.md` one level up.
Everything below was true at commit `acf61b3` on `main` (github.com/saulpena/MuseXR).

The epistemic state of each claim is marked:
- **looked at:** an image was taken and judged.
- **measured:** a value was read in Play or from a test.
- **not checked:** written, compiled and maybe unit-tested, but nobody has seen it run.

---

## 0. Where things are

| | |
|---|---|
| **The scene** | `Assets/Scenes/Tests/GateWorld.unity`. It is the whole journey in one scene: Gate → Palace → Grotto → Van Gogh → Monet → round table → Your world → back to the conservatory. Press Play to start from the title card. |
| **Chapter content** | One prefab per chapter in `Assets/Prefabs/Chapters/` (`Palace/Grotto/Van Gogh/Monet/Your World Frame.prefab`). **Edit chapters in their prefabs, not in GateWorld.** Editing an open scene's file on disk raises Unity's "modified externally" modal, which freezes the MCP bridge. That is why the prefabs exist. |
| **Skylar's design (the spec)** | `MUSE-VR-design\muse\01-vr-design\index-en.html`, with its images under `img/`. Concept stills for the Tripo and generated heroes are in `02-artworks/tripo-previews/` and `01-vr-design/img/tripo/`. Her texts (questions, lantern lines, reactions, disclaimers) are quoted verbatim in code. Keep them verbatim. |
| **Older plan** | `../chatplan.md` (rev 6, 3 Oct). Still right about the design and the shared machinery (§3). Its *status* and *build order* are out of date; this file supersedes them. |
| **Worklist** | https://claude.ai/code/artifact/fda7d729-06cd-46e5-98bb-4453e30929ed. **Not updated this session.** Bring it in line with this file. |
| **Old scene** | `Assets/Scenes/Museum.unity` runs the old ten-stage arc (`MuseumJourneyRunner`). Do not edit it. It still holds the original features listed in §4 that GateWorld has not taken over. |

### Two agents, one repo

- **This clone** is `C:\Git\PicoHackathon\MuseXR-B`, with Unity MCP server `UnityMCPXR`, instance `MuseXR-B@6c1ef995a334ca4f`.
- **Peer agent `musexr-bb`** works in `C:\Git\PicoHackathon\MuseXR`, with its own editor. Both push to `main`.
- Coordinate with SendMessage before touching a prefab or a file the other owns. The current split, agreed 4 Oct:
  - **This agent:** the Gate, Your world, the Palace and Grotto choice flow, and every transition (MoonGate, SplatPortalDoor, ChapterLink).
  - **Peer:** the hero works with Replicable, the Van Gogh easel and stroke, the Monet time ring and work pick, the round table, and the Van Gogh and Monet choice flow.

### House rules that bite

- Before every commit run `git checkout -- Assets/Fonts/`; the TMP atlas churns. Before every push run `git pull --rebase`.
- Push with `git push origin HEAD:main`. The local branch is `feat/interactions`, but it tracks main.
- **Captures for review:**
  - Use a probe camera, with URP `renderScale` 1 and `GaussianSplatSettings.ResolutionScale` 1 during the render; restore `renderScale` to 0.8 afterwards.
  - The probe camera can draw a false black polygon over meshes in splat worlds. When in doubt, use `manage_camera screenshot` with no camera, which is the visitor's own view.
  - A probe camera placed at the eye also sees anything parented to the eye, such as a fade quad.
- **Every visual claim needs an image and a blind review** by the `visual-reviewer` agent (global CLAUDE.md, "Done means looked at").
- **Billed services need Saul's OK first:**
  - Keys: `OPENAI_API_KEY`, `MINIMAX_API_KEY` and `MODEL_API_KEY` are set; `TRIPO_API_KEY` is not.
  - Saul OK'd one generator test, which was spent on the goddess.

---

## 1. What was done (this agent, 3–4 Oct 2026)

### Journey wiring

- **ChapterLink** chains the chapter frames. When a gate opens, the next frame stands behind it. On arrival, frame and rig move to the origin, the previous frame is destroyed, and gravity is held until the new floor exists.
- **The Gate follows her script:**
  - Her three sample questions and Picasso as the seventh master.
  - Name cards, intros on hover, and the trio's answers. Her trio lines are baked in `Resources/OpeningAnswers.json`.
  - Her lantern lines. The lantern moon gate is the exit.
- **Chapter questions:** each chapter shows her "Stop N · question" card on arrival (`ChapterQuestion`). Monet's spawn is 30 m out, so its radius is 60.
- **JourneyMemory.Record** (the journey record):
  - It holds the question, companions, each chapter's choice, the replies (her axes), dwell, the final answer and the world title.
  - The question and companions are now recorded when the moon gate forms (`JourneyCuration.MakeGate`). The Gate is destroyed before arrival, so the round table used to get an empty question. **Measured:** the record holds "Of what I inherited, what is worth keeping?".

### Paintings: every work is interactive

- **WorldPaintings** hangs her works per chapter, with mats and crops. Every work:
  - is grabbable, and scales with two hands;
  - speaks a master's insight on tap (live readings come from `gpt-5.6-luna` via `DialogueClient`);
  - shows her artwork card (`ArtworkCard`, her §4.1) after a 0.4 s point or when the visitor walks up.
- **Her reply chips** follow an insight ("What is this painting to you?", three axes, her `AXIS_CHAMPIONS` reaction). The answer is recorded.
- **Hush:** `ArtworkCard.Hushed` silences the card and the chips while the round table runs. The peer sets it.

### Heroes

- **Gate:** the Mona Lisa wall and the Venus de Milo (`GateHero`). The Venus is the SMK's CC0 scan.
- **Grotto** (`GrottoHeroes`):
  - The cliff Buddha, an 87 m model covering the capture's own, with a chest glow that answers the lamp.
  - A small replicable Buddha.
  - The five Buddhas and the Guanyin. These are pointable through trigger bounds, so they don't block the walk.
- **Palace — new 4 Oct** (`PalaceHeroes`, `Assets/Art/Heroes/goddess-throne.glb`):
  - The golden phoenix-crowned goddess, a image-to-3D test from her concept still: 29,874 triangles, textures at 1024, 16 MB of texture memory.
  - 7 m tall on the capture's throne at (0, 0, -7.2), tinted to the hall's gold.
  - Replicable as "A 15 cm gold figurine". **Measured:** it lands in the satchel.
  - **Looked at and blind-reviewed:** placement is convincing. She is a little glossy, and her plinth reads cool beige.

### Your world

- **The miniature** (`YourWorldMiniature`):
  - It rises 1.1 m in front of the visitor, beside the round table, after the table's answer is kept.
  - Enter it with A or by pointing at its door. A 0.3 s fade takes the visitor through.
  - **Looked at** in the visitor's own view: it reads clearly.
- **The ending** (`YourWorldEnding`):
  - The visitor's satchel copies stand at 3 m, with chimes.
  - The things they left in each chapter appear: the Palace piece, the Grotto lamp, the stroke at 1.5×, the works.
  - The answer stone shows the final answer; with no answer it shows the question, and it is never blank.
  - The memento card.
  - **Looked at.** The final engraving size is **not checked**.
- **Fixed:** entering Your world left a black fade on the eye for good. The miniature was destroyed with the Monet frame mid-transition. **Measured:** fixed.

### Interactions before choosing — new 4 Oct, Saul's request

**Rule:** before a chapter asks the visitor to choose, they must interact with every option and hear the masters on each. Instructions guide them.

- **`ChoicePreview`** is a card by the options: "Before you choose, hear your companions on each". Each option has a line that ticks when a master speaks on it, then the card shows her instruction for the choice itself. The pure logic is `OptionsHeard`, covered by `ChoicePreviewTests`.
- **Palace:** the crane and the turtle can't be lifted until both are heard (`SlotStation.GrabGate`). Reaching early pulses the card. **Measured:** `CanGrab` is false before; the crane ticked when heard.
- **Grotto:** the lamp can be carried and held to the relief, but it seats in neither socket until both sockets ("Look at detail" / "Look at the whole") are heard (`SlotStation.SeatGate`).
- **Taps while a companion speaks** are now queued in `MasterInsights`, not ignored. Before this, tapping the turtle during the crane's reading did nothing.
- **Walked in Play, 4 Oct (second session), billed calls muted (no voice, no live readings):**
  - **Palace, measured:** a grab before hearing is refused; tapping the turtle while the crane's line plays queues it, and the card unlocks when it lands; crane set in the court, chip, A; record `palace{crane, "It still looks up"}`; moon gate opens.
  - **Palace, looked at:** the card read correctly but stood in front of a hanging scroll. Moved to the court's other side, against a red pillar (`fix-palace-01-card-other-side.png`).
  - **Grotto, measured:** the lamp set on the detail stand before hearing floats home (placed = -1); both stands heard, then it seats; the round runs about 12 s a line; A keeps; record `grotto{detail}`; arch opens.
  - **Grotto, looked at:** the card hung 2 m up across the cliff Buddha's chest, dark glass on gold, barely legible, and 10 m out it had faded before the visitor arrived. It now stands 45% of the way to the stands, just above eye height, against sky and balustrade (`fix-grotto-02-card-after-question.png`). The arrival guide said "Take the lamp" first; it now says to hear both stands first (`Step.Hear`). Post titles were shortened, because the canned openings quote them.
  - Captures are in `Assets/Screenshots/review-1004/`. The blind review of the first-run set is summarised in §2.
- **Van Gogh and Monet** (`ChapterFeatures.Take`, not `ChoicePreview`): the companions take each option in turn, voiced as each turn starts.
  - **Van Gogh:** after the reply lights the easel, touching each pot gives its take, with nothing on the brush yet. The prompt counts n / 3, then "Now choose"; the next touch picks.
  - **Monet:** the time ring speaks on each moment the first time it is reached. The painting chips appear only after all three. The first tap on each chip gives its take, and after all four a tap chooses (3 s undo).
  - **Measured and looked at, 4 Oct:** real pointer clicks and a grip-and-twist on the ring, in Play.

### Transitions — new 4 Oct, Saul's request

- **Hole behind you after a gate:** `MoonGate.CutArrivalFloaters` cuts the next world's floaters out of the view through the gate, and the cut was never removed. It is now restored on arrival (`MoonGate.RestoreCut`).
  - **Gate → Palace:** **measured** that the cut is gone after arrival, and **looked at**: solid wall behind the visitor.
  - **Every other link, 4 Oct (second session):** **measured** zero cutouts left after each arrival, and **looked at** the view back after each: Palace→Grotto (carved niche wall), Grotto→Van Gogh (blue corridor), Van Gogh→Monet (rose arbour; near floaters are soft but there is no hole), Monet→Your world.
- **Two companion groups in every chapter (fixed):** `MasterInsights` built its own group over the "Mark *" figures in the frame before a chapter's `Start()` made the real one, and kept it. **Measured** in the Grotto and Monet: two groups driving the same three figures, so insights and the chapter's turns could talk over each other. It now adopts the chapter's group (`_built`). **Measured** after the fix: one group in the Palace, Grotto and Monet, and it is the chapter's own.
- **The last link re-opened its gate (fixed):** the `Your world to the Gate` link has no `previousFrame`. On arrival it destroyed the door only, and Your world's `ChapterExit` saw its gate closed and opened it again. The result was a second conservatory renderer, a stray floater cut, and Your world's labels and paintings floating in the conservatory (`conservatory-01-lookback-after-yourworld.png`). Now `ChapterExit` opens once, and the last link destroys the frame the gate stands in. It keeps that frame's `Walk Floor`, which is the only floor the conservatory has: without it the visitor fell to y -86.
- **Your world's exit was doubled and too close (fixed):** the layout's schematic "Exit Behind: start again" arch stood 0.36 m behind the real gate frame, and both were 0.9 m behind the arrival point, which is one step back for a gate that opens by itself after 20 s. The marker is now hidden when the gate opens, and the gate moved to 2.5 m (prefab). **Looked at:** one arch (`fix-yourworld-03-single-exit-alone.png`).
- **No delay crossing:**
  - Gates are walkable at 35% risen (was 60%).
  - The door shuts behind in 0.4 s (was 2.5), so the next chapter wakes almost at once. The Gate's own moon gate uses 0.6 s, set in `JourneyCuration`.
  - **Not checked in a headset.**
- **Still to do:**
  - The next chapter's interactions, companions and paintings only wake on arrival, because they assume the origin. Waking them while the gate opens would remove the last hitch, but every chapter `Start()` that reads the head or world positions would need checking first.
  - Before crossing, only the next world's "priority" splats draw through the gate.

### Other fixes

- **Double hands:** the Grotto prefab carried its own `HandsBootstrap`, which built a second hand and pointer. Removed; the peer also added a guard.
- **The Grotto's big statue groups** no longer block the walk to the arch.
- **The round table** moved out of the splat floaters, to the exedra at (8, 0, -15.2). Peer change.

---

## 2. What is left (Skylar's design vs the build)

In priority order:

1. **Choice flow and transitions: walked in Play on 4 Oct, second session (see §1).** Still open from that walk and its blind review:
   - **Companions in view: fixed and measured (4 Oct, third session).** Saul's rule: "peripheral view and sides at most; they follow you but do not get in your way". `CompanionClearanceProbe` (add it to any object in Play) walks, turns, snaps, glances, teleports and steps back, judges every companion each frame (`CompanionClearance`, pure, tested), captures each move as it lands and 0.3 s later, and prints a VERDICT. **Before:** 13 faults at the Gate (in front of the eye for 2-3.5 s; three inside the visitor after a teleport and a turn). **After:** PASS in the Gate (step-out included), Palace, Grotto, Van Gogh, Monet and Your world. The fixes:
     - the crowd moves round the visitor in an arc (`CrowdOrbit`, pure, tested). It never comes nearer than 1.4 m, never crosses the front, and keeps out of a 42 degree cone round the head's actual gaze;
     - the crowd is placed in `LateUpdate`, after the frame's turn or teleport. A blind review caught Van Gogh mid-view on the frame a snap turn landed, in all five rooms, while the harness still passed: a one-frame lag;
     - a snap turn snaps the crowd with it; walking backwards no longer turns the body round (`BodyFrame`);
     - the speaking marks moved from -32/26/54 to -45@2.0, 42@1.9 and 60@2.2. This is still inside her 1.5-2.2 m and 60 degrees. It replaces the 3 Oct headset decision to keep all three in view, because the shared answer panel names the speaker;
     - the Gate trio steps out of the line-up in an arc (`CompanyStage.StepOut`), not a straight line across the view.
     - Sheets: `Assets/Screenshots/review-1004/sheet-v3-*.png`. **Not yet in a headset.**
   - **Fixed this session:** the "Five golden Buddhas" label stood on the arrival spot at knee height (labels are now never nearer than 2.5 m). Van Gogh's 21 m ceiling stuck out of the Grotto arch, and Monet's labels showed through Van Gogh's wall: a chapter waiting behind a gate now shows a mesh only while it is seen through the opening (`ChapterLink.HoldBack`). The Venus stood behind the Pissarro, so only her drum showed (`GateHero.VenusAside` 2.6).
   - **Still open:** a dark smear of loose splats near Monet's arrival point shows at the bottom right of the Van Gogh side door. On arrival in the Grotto three UI panels stack mid-view (choice card, guide, answer). The answer panel is large and tilted in most frames. The live round table WAS run on 4 Oct (musexr-bb): three threads, the one-sentence draft, see below.
   - **At the very end, the companions go with Your world**, so the conservatory is empty of them. Decide whether that is the ending wanted.
   - **The Palace card is beside the court now.** Seen from the court it reads well; from the arrival spot it sits at the edge of the view.
   - **Grotto `Step.Hear` and the moved card: looked at, not yet in a headset.**
2. **Pre-wake chapters when the gate opens: not done, and not worth it now.** Derived from the code, not measured with a timer: a chapter wakes about 0.4 s plus three frames after the crossing (`MoonGate.CloseBehindSeconds`, then the `ChapterLink.Arrive` frames, then `Start()`'s one-frame yield). Every chapter `Start()` reads the head at the origin, so waking it early is a refactor of all five. Only do it if Saul still feels the delay in a headset.
4. **The remaining Palace/Grotto heroes from her concept stills**, by the same generated route as the goddess, after Saul approves the cost:
   - The two peach-tray goddesses flanking the throne steps (`goddess-tray.webp`, 4 m each).
   - Her gold-roofed red hall (`palace.webp`). It has no placement yet, because the Palace's moon gate shows the Grotto.
   - The current Grotto five-Buddhas and Guanyin models came from `Assets/Props/Peach`; check them against `five-buddhas.webp` and `guanyin-group.webp`.
5. **Goddess polish (optional):** reduce the gloss to match the hall's matte gilding, warm the plinth, and move her name plate off the dais front.
6. **Four failing `ArtworkCatalogTests`** (pre-existing): `sunset-frames` lists a fifth work, `picasso-multiple-realities`, which has no image or source URL. Either stage an image or drop it from that collection.
7. **Three other failing tests** are local content gaps in this clone, not code: `grand-conservatory-garden-path-500k` is not converted, and the `elegant-floral-palace-interior` collider is missing. Reconvert, or confirm they exist in the other clone.
8. **Update the worklist page** from this file.

### Spec gaps closed on 4 Oct (musexr-bb, Saul: "do all of the things missing")

All looked at in Play, in the Editor; none yet in a headset.

- **Gate: the question engraved over the arch.** It was there, but at the name cards' height 50 m away, cut into fragments by them. Saul chose "far arch, bigger and higher": `GateStage.letteringHeight` 15, cap 1.6 m, width 24 m; the company panel is 0.6 m higher. Both lines now read between the name cards and the panel.
- **Grotto: the stele's depth under the lamp.** Already worked (`LampLight`, niche). Looked at: flat grey with the lamp low, warm gold relief with it raised beside the stele. No change.
- **Round table, staged to her spec.**
  - The table is a stone top on a pedestal with a gilt band.
  - A warm light and floor glow come up once the garden's choice is kept, and the sign brightens.
  - The Stacks of Wheat moved 1.6 m along its wall, off the table.
  - The draft is now the visitor's answer in ONE sentence, asked live while the masters speak (`DraftLive`). Measured: "Of what I inherited, I will keep slow persistence, attentive looking, bold beginnings, and changing light."
  - Saul chose that the masters **stand** at the table (the models are standing poses).
- **Masters speak on approach only for a chapter's choice pieces.** `InsightTarget.onApproach`, set by `ChoicePreview`. Everything else answers a click. Measured in the Palace: the scrolls stay quiet, while the crane and the turtle start a take.
- **Every piece is tracked** for Your world (`Exhibit` sweep): heroes, statues and props get look-time into the record, like hung works.
- **Your world:** the four Redons hang on the corridor walls, and the kept pieces (the crane, the stroke ribbon) are there. Looked at.
- **Left for a human:** spoken input ("hold X and say your own") inside the journey; everything in a headset.


---

## 3. Measured on 4 Oct (the full walk)

- **Gate:** the question is set, Pissarro tap → reply chips → recorded, Picasso's intro, the trio answers, the lantern line, the moon gate.
- **Palace:** "Stop 1" shown. The turtle is grabbed and placed, then chip and A. The record holds "turtle · It holds steady". Bell, gate.
- **Grotto:** "Stop 2". The lamp held to the relief lights the Buddha's chest glow. Detail socket, A, record, stone chime, arch.
- **Van Gogh:** "Stop 3". The Bedroom insight → reply → the easel unlocks (peer). The stroke is kept; wood chime; side door.
- **Monet:** the time ring set to dusk, Water Lilies picked, water chime, the live round table. A kept; title "Light, Pressure, Unasked Inquiry".
- **Your world:** the miniature, then entry; four choice lines, the memento.

Tests: 704 of 711 EditMode pass. The 7 failures are those in §2 items 6–7.

---

## 4. Original features that must keep working (regression list)

These came from muse-infinity and the earlier MuseXR stages. After any change, check that they still work in GateWorld; the "where" column says where each one lives.

| Feature | Where | Status in GateWorld |
|---|---|---|
| Masters comment on any work: tap or walk up, one opens, live readings follow | `MasterInsights`, `InsightTarget`, `DialogueClient` (`gpt-5.6-luna`) | works (measured) |
| Masters' voices (MiniMax `speech-2.8-turbo`, her seven cast voices) | `MiniMaxVoice`, `JourneyOpening.VoiceFor` | works at the Gate. **Not checked** whether chapter insights are voiced or subtitles only |
| Subtitles under speech | `SubtitleRig` | works |
| Artwork card with title, artist, date, source, rights, URL and AI tag | `ArtworkCard` | looked at |
| Her reply chips, axes and champion reactions, recorded | `MasterInsights.ShowReplies`, `Insights.Reaction` | measured |
| Dwell: 4 s or more of gaze counts as seen | `DwellReporter` in `WorldPaintings.cs` | in the record; **not checked** in the round table's prompt |
| Grab and two-hand scale on every work | `Grabbable` (MusePico.Grab) | measured |
| Replicate a hero (hold the trigger 1 s) into the satchel on the left wrist | `Replicable`, `Satchel` | measured |
| Compass on the waist panel pointing to the next step | `CompassTarget`; orders under 30 are steps, 30 and above are optional works (peer, 4 Oct) | works |
| Chapter questions, chapter chimes | `ChapterQuestion`, `ChapterChimes` | measured |
| Her own question by voice ("hold X") | `GateStage` with `OpenAiSpeechToText` (`gpt-4o-transcribe`) | **not checked with a real microphone** in GateWorld |
| Round table: three threads, the draft answer, Keep / Rewrite / Say my own, world title | `MonetFeatures` (peer) | measured once, live |
| Your world built from the record, the memento, the answer stone | `YourWorldEnding` | looked at |
| Honesty lines: AI-interpretation disclaimer; AI studies tagged; the cliff Buddha labelled "not a real site" | various | present |
| Full conversation with masters (the old salon) | `MuseumDialogue` | **only in `MuseumSalon.unity` / `Museum.unity`, not in GateWorld** |

---

## 5. Traps found this session (keep)

- **A choice option needs `askReply = false`.** Otherwise the reply chips ask "What is this painting to you?" about a crane.
- **`MasterInsights.Clicked` used to drop taps while the group was busy.** Taps are queued now. Anything that waits for "heard" should listen to `MasterInsights.Spoke`.
- **`SlotStation.SeatGate` works by giving the board infinite distances**, so a piece released near a socket floats home. Keep that in mind if `SlotBoard` changes.
- **The Palace court snaps within 0.12 m of the piece's *base*.** A sloppy carry floats home; that is not a bug.
- **image-to-3D honours `should_remesh` + `target_polycount`** (29,874 triangles from a 30,000 target) and returns 2048 textures. Halve them with `MusePico/Tools/tripo/decimate.mjs --tex 1024`.
- **A gate's cutout is parented to the next frame**, which ChapterLink moves. Restoring it on `MoonGate.Arrived` survives the previous frame, and the gate, being destroyed.
