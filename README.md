# MuseXR

A native Unity XR rebuild of [**MUSE∞ — The Impossible Museum**](https://github.com/SkylarWJY/muse-infinity)
for PICO and Meta Quest.

The original is a browser app — vanilla JS, Three.js, Gaussian-splat worlds rendered with
Spark. This is not a port of that code. It is a rebuild in Unity against the original as a
specification, so the walkable museum runs as a standalone APK on the headset itself.

## Status

Pre-Unity. The project scaffold is being prepared; the Unity project is created next.

Open questions, setup tasks and findings live on the project worklist rather than here —
see `../CLAUDE.md` for the link.

## Performance

Splat worlds on standalone headsets, and how the intro reached 72 FPS on a Quest 3S:
see [`PERFORMANCE.md`](PERFORMANCE.md).

## Layout

This repo expects to sit beside the original:

```
PicoHackathon/
├── muse-infinity/   reference implementation — the spec (do not commit into it)
└── MuseXR/          this repo
```

The original is worth keeping on disk. It carries the measured data the rebuild needs:

| File | What it holds |
|---|---|
| `config/worlds.js` | every world's spawn point, ground height, bounds, yaw, and splat transform |
| `config/museumAssets.js` | the 42 artworks and the seven masters |
| `config/exhibitionScenes.js` | the nine-scene exhibition spine |
| `server.mjs` | the API behaviour the C# client must match |

## Assets are not in git

The nine Marble worlds (512 MB of `.spz`) and the rigged masters (278 MB) come from the
original's GitHub release, not from version control:

```bash
gh release download worlds-v1 -R SkylarWJY/muse-infinity
unzip -o worlds.zip -d assets/ && unzip -o characters.zip -d assets/
```

They are already extracted under `../muse-infinity/assets/`. Unity imports from there;
`.gitignore` keeps them and the converted splat assets out of the repo.

## Requirements

- **Unity 6 LTS** with Android Build Support (OpenJDK + Android SDK & NDK Tools)
- **URP** — the Gaussian splat renderer needs URP on Unity 6, with Render Graph
  Compatibility Mode **off** and a `GaussianSplatURPFeature` on the renderer
- **Vulkan** graphics API (DX11 is not supported by the splat renderer)
- **PICO Unity OpenXR SDK** — *not* the older PICO Unity Integration SDK. Installing both
  breaks the build with `Multiple plugins with the same name (libEGL)` and
  `Assembly with name 'PICO.Platform' already exists`.

One project, two build profiles (PICO and Quest) rather than a single universal APK.

## Secrets

No API keys in this repo, ever. Keys are environment variables at development time; how they
reach a shipped build is a separate decision recorded on the worklist.

## Licence

The original MUSE∞ is MIT. Artwork is Art Institute of Chicago Open Access.
