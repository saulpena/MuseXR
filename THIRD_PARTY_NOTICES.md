# Third-party notices

Everything bundled in this repository that was not written for it, with its source and licence.
The Gaussian-splat worlds are **not** in the repository (see the README).

## Artwork images — public domain

**Art Institute of Chicago Open Access** — 44 images, `Assets/Museum/Artworks/aic-<id>.jpg`, plus
`monet-water-lilies-1906.jpg`, `van-gogh-bedroom-1889.jpg` and `seurat-grande-jatte-1884.jpg`.
Every record was checked against the museum's API and is marked `is_public_domain`. The record for
each is `https://www.artic.edu/artworks/<id>`. Source: https://www.artic.edu/open-access

**The Metropolitan Museum of Art Open Access** — `met-42719.jpg`, *Buddha Dipankara (Randengfo)*,
marked public domain by the museum (https://www.metmuseum.org/art/collection/search/42719).

**Mona Lisa** — `Assets/Resources/Heroes/mona-lisa.jpg`, Leonardo da Vinci, the C2RMF scan via
Wikimedia Commons, public domain.

**Portraits of the masters** — `Assets/Museum/Portraits/` and `Assets/Resources/Portraits/`, the same
public-domain Wikimedia Commons images MUSE∞ uses; each source is listed in its
[`THIRD_PARTY_NOTICES.md`](https://github.com/SkylarWJY/muse-infinity/blob/main/THIRD_PARTY_NOTICES.md).

## 3D scans — CC0

**Venus de Milo** — `Assets/Art/Heroes/venus-de-milo.glb`, the SMK (Statens Museum for Kunst) open
scan, CC0 1.0.

## From MUSE∞ (MIT)

Concept images, chapter backdrops and the generated "world" paintings come from
[MUSE∞](https://github.com/SkylarWJY/muse-infinity) by SkylarWJY, MIT licence:
`Assets/Museum/Chapters/`, `Assets/Textures/ExhibitionScenes/`, and in `Assets/Museum/Artworks/`
`frida-living-memory`, `future-being`, `kusama-infinite-self`, `picasso-multiple-realities`,
`qi-baishi-living-ink` and `van-gogh-emotional-sky`. These are **AI-generated images made for that
project, inspired by the artists named** — not the artists' works and not reproductions.
The Tripo character models in `Assets/Tripo/Models/` were generated for MUSE∞ by its author.

The VR design diagrams, storyboards and interface mockups in `Docs/HerPlan/` and `Assets/Art/HerPlan/`
are SkylarWJY's design for this VR version, included with her permission.

## Music — public-domain recordings

`Assets/Audio/promenade.ogg` (Mussorgsky), `clair-de-lune.ogg` (Debussy) and `gymnopedie.ogg`
(Satie), the Wikimedia Commons public-domain recordings MUSE∞ uses; sources are in its notices file.

## Models and animation

**Universal Animation Library** by Quaternius — `Assets/Art/Quaternius/UAL/`, CC0 1.0
(licence file included).

**Props, doors, heroes and the masters' bodies** (`Assets/Art/`, `Assets/Resources/Props/`,
`Assets/Resources/Heroes/`, `Assets/Props/Peach/`, `Assets/Art/Characters/Painters/`) were
AI-generated for this project on paid plans, from MUSE∞'s concept images and the public-domain
portraits above. They are interpretations, not likenesses or records of real objects.

## Fonts — SIL Open Font License 1.1

Inter, Gilda Display, Cormorant Garamond and JetBrains Mono (`Assets/Fonts/`, each with its OFL
text), and Liberation Sans (bundled with TextMesh Pro).

## Code and packages

- **Gaussian Splatting for Unity** by Aras Pranckevičius — `Packages/org.nesnausk.gaussian-splatting/`,
  MIT (`LICENSE.md` in the package). Included with local patches (Multi Pass stereo, splat-layer
  resolution scaling, sort pacing); see `PERFORMANCE.md`.
- **XR Interaction Toolkit Starter Assets** and **TextMesh Pro** resources — Unity Technologies,
  Unity Companion License.
- **PICO Unity OpenXR SDK** — fetched by the package manager under PICO's own terms;
  `Assets/Resources/PXR_DebuggerPanel.prefab` is the SDK's own debugger panel.
- Other Unity packages (URP, OpenXR, Input System, Addressables, glTFast) are fetched by the package
  manager from `Packages/manifest.json` and are not redistributed here.

## Services used at runtime (not bundled)

OpenAI (dialogue, transcription), MiniMax (voices) and, optionally, World Labs Marble (world
generation) and Tripo (model generation). Their keys are never in this repository.
