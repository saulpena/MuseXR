# MuseumSalon

**Skylar's dialogue, in a headset.** Monet, Van Gogh and Socrates stand in a shallow arc; you ask a
question out loud and all three answer at once, each in their own voice.

## What it does

Speak (or press Tab for the typed question) → `whisper-1` transcribes → three concurrent calls to
the OpenAI Responses API, one per master, each carrying that master's authored lens → the readings
appear above their heads and are spoken in turn through MiniMax, in the voice each master is cast
with in muse-infinity.

The three masters are the authored defaults, and happen to be three of the five models already
converted, so they are the actual Tripo characters rather than stand-ins.

## Status

| | |
|---|---|
| Editor | Layout verified — `Assets/Screenshots/museum-salon-02.png`, with seeded example readings |
| **PICO Emulator** | **Never built or launched** |
| PICO hardware | Never run |
| Quest | Never run |
| Live dialogue | **Never run.** No call has been made with a key |

## What is missing

- **MiniMax key.** Without it the readings still arrive; only the voices are lost. That is
  deliberate — narration is decoration and must never cost the reading.
- **The dialogue model is unverified.** Defaulted to `gpt-5.6`, but muse-infinity's own
  `.env.example` records that the default was *too slow for its 15 s timeout* and pins
  `gpt-5.3-codex-spark` after measuring. Measure before trusting; it is a public field.
- No artwork actually in the scene. `artworkTitle/Artist/Date` are Inspector strings that go into
  the prompt, but the visitor sees no painting — the masters describe something that is not there.
- The `effect` each master chooses (`mist`, `fracture`, `garden`, `network`) is printed as text.
  In her build it drives scene lighting; here nothing acts on it yet.
- No conversation memory. Each question is independent; her roundtable synthesis is not ported.
- Only three of the seven masters are seated. The other four (Picasso, Frida, Hilma, Morisot) are
  in the roster and have models for two of them.

## Notes

**The lens data is the product. Treat `Assets/Dialogue/masters.json` as read-only here.** It is
exported verbatim from `muse-infinity/config/museumAssets.js` and `config/masterVoices.js`.
Re-syncing when Skylar edits a lens is re-running the export, not editing this copy — six authored
fields per master, several written to close a specific observed failure, and a dropped clause would
not break a build or throw. It would show up only as three masters gradually sounding like one.

Rules carried over, each with a test because none would fail loudly:

- **Shared rules reach the model exactly once**, in the instructions, and appear in no master's
  block. An earlier revision emitted them 2–3× per call while the separating fields arrived once.
  Repetition is emphasis, so the model was emphatically told to be a careful museum voice and only
  briefly told to be *this* master.
- **The master's block leads with the SHAPE** — the speech act. Two masters told to "report what
  you see" with different nouns are one voice in two costumes.
- Under **50** words, not 55. Restating 55 produced four breaches in thirty replies.
- No deictic openers ("Look", "Notice"). Every voice reaches for them, so they identify none.
- Never restate the disclaimer in the reading — it gave all three the same opening phrasing.

**Three concurrent calls, not one returning three readings.** Measured, n=32 interleaved:
P50 3.30 s vs 6.40 s, P95 7.62 s vs 14.07 s, distinctiveness unaffected.

**The whole turn, measured 14 Sep 2026:** transcription ~1 s · dialogue ~3.3 s · MiniMax synthesis
**1.5–2.4 s per line** (the three can be synthesised concurrently) · **4–5 s of audio per master**,
played in sequence. So one question answered by three masters is about **20 seconds, of which ~14
is speech**.

Against a 45–90 s Tripo generation that is **three to six exchanges** — enough that the wait can be
filled, which was the point. An earlier version of this note said fifteen; that counted only the
model's latency and forgot that someone has to listen to the answer.

**MiniMax returns PCM here, not her hex mp3.** A browser decodes mp3 free; Unity does not, and
`AudioClip.Create` takes float samples. Her voice ids are *system* voices, so they should exist on
any account — but model availability is account-gated (her own comment records probing and finding
`speech-2.5-turbo` absent), so probe before assuming.

Readings are spoken one after another, never together. Three masters talking over each other is
noise, and being able to tell them apart is the entire point of asking three.
