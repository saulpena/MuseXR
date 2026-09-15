# TripoRuntime

**The bench for generating 3D at runtime.** Pick a pipeline, run it, read what it cost. Currently
**scene 0** in Build Settings, so a plain build lands here.

## What it does

A plinth, a status panel and a progress bar. Five presets compare the pipelines that decide whether
runtime generation is viable at all — textured vs untextured, the three budget tiers, and a
deliberately place-shaped request to see what Tripo does with one.

Controls, at a desk or in a headset:

| | |
|---|---|
| Trigger / Space | generate the selected preset |
| Stick / Tab | next preset |
| Grip / V | speak — the utterance ends itself when you stop |
| B / Esc | cancel |

Everything it reports is measured on the spot: elapsed seconds, credits actually consumed,
triangles, materials, texture memory and a verdict against a headset budget. **"Is generated
output performant enough for VR" is meant to be a reading from your account, not an opinion.**

No world-space Canvas on purpose. A canvas needs the XR UI raycasting stack wired up before a
single button can be pressed, and every part of that is another candidate for "why does nothing
happen on the emulator". This drives off raw device buttons and 3D text.

## Status

| | |
|---|---|
| Editor | Panel and plinth verified — `Assets/Screenshots/tripo-runtime-03.png` |
| **PICO Emulator** | **Built, installed, permissions granted — never actually launched.** The first launch after an emulator boot is intercepted by PICO's "Controllers Required" modal, which is 3D-composited so `adb shell input tap` cannot reach it. Someone has to click **Continue Anyway** in the emulator window once per boot |
| PICO hardware | Never run |
| Quest | Never run |

`RECORD_AUDIO` and `INTERNET` are in the manifest and both grant headlessly — verified by
`dumpsys package com.musexr.pico`.

## What is missing

- **No Tripo API key, so no generation has ever run.** Every claim about the wire format comes
  from Tripo's own Go SDK and docs, not from a live response.
- **Never launched.** Everything above is inference from a successful build plus Editor renders.
- The transcription path is written but unexercised — `whisper-1` is confirmed by DuckyMayhem
  using it against this account, not by this project calling it.
- Whether the emulator passes **host** microphone audio through is unknown. It declares
  `android.hardware.microphone` and runs live 48 kHz RECORD threads, which is why the level meter
  exists: from code, "recording silence" and "no microphone" are identical.
- No retexture / rig / animate presets, though the client supports all three.

## Notes

- **Three request options bill fully and then produce an unusable file.** `compress: "geometry"`
  emits meshopt, which glTFast decodes only with an add-on package that is not installed;
  `quad: true` forces FBX, which glTFast cannot open; `generate_parts` is incompatible with
  texture and pbr. `VrAssetBudget.ApplyTo` forces all three off and tests pin it.
- **P1 caps at 20,000 triangles** and produces game-ready topology there. That single fact is what
  makes runtime generation viable — the bundled characters are ~1.96M each and there is no
  decimation step on device.
- The panel sits at **yaw 0**, not 180. See `TripoGallery.README.md` — copying the gallery's 180
  here mirrored the text, which is how the actual rule got established.
- Nothing bills without a confirmation that states the price, and there is no retry anywhere: a
  silent retry on a failed generation bills twice for one mistake.
