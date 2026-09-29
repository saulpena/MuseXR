# Dynamic world generation with the Marble API — research

Branch `dynamicgeneration`, 28 Sep 2026. **Research only: nothing in Unity has changed.** No key
exists (`WORLDLABS_API_KEY` is unset at User scope), so no call has been made and nothing has been
billed. Every fact about the API below comes from World Labs' own OpenAPI spec
(`https://docs.worldlabs.ai/api/reference/openapi.yaml`, "Marble Public API v1" 1.0.0) and doc pages;
where the docs contradict themselves, it says so.

Labels: **[doc]** a primary source states it · **[measured]** checked here · **[inferred]** reasoning,
not a source.

## The short answer

- **Generating a world from a prompt at runtime is possible, but it is not instant.** A standard
  world takes **about 5 minutes** [doc] and costs **$1.26** (1,580 credits) [doc, re-checked against
  the pricing page]. A Draft world costs **$0.18** (230 credits), but World Labs publishes no
  timing or quality figures for it.
- **So "speak and step into it" does not work. Design for "ask now, walk in later."** The journey
  is long enough to hide five minutes: kick off the generation at stage 01–03 and open the
  generated world at stage 04 or later.
- **Key: decided (Saul, 28 Sep) — it rides in the APK like the OpenAI and MiniMax keys.** This is a
  hackathon, so `KeyInjectionBuildStep` handles it the same way. ToS §2.6 does forbid sharing
  credentials [doc]. The one practical difference from the other keys is that **World Labs lets the
  balance go negative**, so an extracted key can spend past what was bought. Keep the balance
  small, auto-refill off, and rotate the key after the event.
- **The headset cannot load a generated world today, and can after one porting job.** The `.spz`
  reader and `GaussianSplatAssetCreator` live in the splat package's Editor assembly. The processing itself
  (Morton order, chunking, quantisation) is plain Burst/C#; only the asset file I/O is
  Editor-bound. Unity 6.3 has `new TextAsset(ReadOnlySpan<byte>)` [measured, in
  `UnityEngine.CoreModule.xml`], and the renderer only calls `.GetData<T>()` on those TextAssets. So
  a runtime `GaussianSplatAsset` built from downloaded bytes is feasible, but it is a porting job.
- **Frames differ.** API downloads come in a different axis frame from the web-app exports every
  world in this project was made from [doc]. Everything else follows from that; see
  "Coordinate frame" below.
- **There is a Chisel-shaped route in the API.** `pano:depth_to_rgb` turns a depth panorama into an
  RGB panorama, which then generates a world [doc]. QUALITY.md's headline finding is that small
  designed rooms look far better than big captures at the same 500k. This route could produce
  them from code rather than by hand in the web app. It is the most interesting thing found here.

## What the API offers

**Base** `https://api.worldlabs.ai/marble/v1` · **auth** header `WLT-Api-Key` · keys from
`platform.worldlabs.ai/api-keys` · launched 21 Jan 2026 [doc].

| Endpoint | Does |
|---|---|
| `POST /worlds:generate` | start a generation; returns an Operation |
| `GET /operations/{id}` | poll it |
| `GET /worlds/{id}` · `DELETE /worlds/{id}` · `POST /worlds:list` | read / delete / list (API-created worlds only) |
| `POST /worlds/{id}:export` | PLY splats (sync, free) or HQ mesh (async, 3,500 credits, ≤4/hour) |
| `POST /media-assets:prepare_upload` | get a signed URL to PUT an input image/video to |
| `POST /pano:depth_to_rgb` | depth pano + text → RGB pano (the Chisel-like step) |
| `GET /credits` | balance — free, the safe first call with a new key |

### Inputs — `world_prompt.type`

| type | Fields | Pano cost |
|---|---|---|
| `text` | `text_prompt`, `disable_recaption` | 80 |
| `image` | `image_prompt` (content ref), optional `text_prompt`, `is_pano` auto/true/false | 80, or **0 if already a pano** |
| `multi-image` | `[{azimuth, content}]`, ≤4 (≤8 with `reconstruct_images`) | 100 |
| `video` | `video_prompt`, ≤100 MB | 100 |

A content reference is `{source:"media_asset", media_asset_id}`, `{source:"uri", uri}` (must be publicly
fetchable, ≤20 MB) or `{source:"data_base64", data_base64, extension}` (≤10 MB) [doc].
**`data_base64` removes the need muse-infinity has for a public HTTPS URL** (`safeHttpsUrl` in
`services/worldLabsApi.js`). A panorama or a painting can go straight up in the request.

Other fields: `model`, `display_name` (≤64 characters), `tags` (≤10), `seed`, and
`permission {public, allow_id_access, allowed_readers, allowed_writers}`, private by default.
**Visibility cannot be changed after creation**; there is no PATCH endpoint [doc]. In the web app the
default is Public (QUALITY.md). In the API it is private. Good.

### Models and price

$1 = 1,250 credits, $5 minimum, credits never expire, and they are separate from app credits [doc].

| `model` | World credits | Text → world total | |
|---|---|---|---|
| `marble-1.0-draft` | 150 | 230 = **$0.18** | "our fastest model"; no timing published |
| `marble-1.0` | 1,500 | 1,580 = $1.26 | |
| `marble-1.1` | 1,500 | 1,580 = $1.26 | |
| `marble-1.1-plus` | 1,500 + 0–1,500 variable | median $1.28, max $2.48 observed | grows the world by up to five "cubes" (QUALITY.md) |

**Always send `model`.** The Models page says the default is `marble-1.0`; the OpenAPI spec says
`marble-1.1` [doc: they disagree].

**Billing can overdraw.** Admission checks the balance against a low-balance threshold, not the
cost of the request, so the balance can go negative. The overage is invoiced monthly, and turning
auto-refill off does not cap it [doc]. A runaway loop is billed, not refused. The spend cap has to
live in our own code (a per-session generation count in the client).

**Rate limits** are per account: ~3 generation starts per minute and 60 per hour by default, with
`429` + `Retry-After` beyond that. There is no documented limit on concurrent jobs [doc].

**No idempotency key** [doc: none in the spec]. A retried `worlds:generate` is a second paid world.
Same rule as Tripo: never retry a generation automatically.

### Async shape

`worlds:generate` returns `{operation_id, done, error, metadata, response, cost, created_at,
expires_at}`. Poll until `done`. `metadata.progress.status` reads `IN_PROGRESS` → `SUCCEEDED`, and
`metadata.world_id` appears early. A failure is `done:true` with `error` set. A content-policy
rejection is a `400` at submit time [doc].

Three documentation inconsistencies worth coding defensively around:
- The `worlds:list` status enum is `SUCCEEDED | PENDING | FAILED | RUNNING`, which does not
  include `IN_PROGRESS`.
- The example response uses `id`, but the `World` schema requires `world_id`.
- The `response` snapshot may carry nulls. Call `GET /worlds/{id}` for the real record.
- `expires_at` is one hour after creation in the example. What expires is not documented.

### Outputs — `World.assets`

| Field | What | Notes |
|---|---|---|
| `splats.spz_urls["100k" \| "500k" \| "full_res"]` | the splats | full ≈ 2M, but not fixed (QUALITY.md measured 1.92M–4.32M) |
| `splats.semantics_metadata` | `metric_scale_factor`, `ground_plane_offset` | 1.0 means "could not infer" |
| `mesh.collider_mesh_url` | GLB, 100–200k tris | the walkable collider we use today |
| `mesh.hq_mesh_url` / `full_res_mesh_url` | textured / vertex-coloured GLB | only after an export |
| `imagery.pano_url` | 2560×1280 PNG | also the input for a 0-credit re-generation |
| `caption`, `thumbnail_url`, `world_marble_url` | | the caption could feed the masters' dialogue |

**How long the download URLs stay valid is not documented**, and ToS §2.10(a) forbids caching
"beyond the duration specified". The safe reading is to download immediately after generation,
into storage we own.

## Chisel through the API — what World Labs' own example does

There is no Chisel endpoint. What exists is Chisel's second half. World Labs' official example
`worldlabs-api-examples/web-chisel-depth-png` (one `index.html`, three.js 0.180, read 28 Sep 2026)
is a Chisel-by-hand editor:

1. Place primitives (boxes, spheres, cylinders) and a **pano camera** in a three.js scene.
2. Render a cubemap from the pano camera and convert it to an equirect of **radial depth**,
   2048×1024, normalised by the measured min/max distance and log-encoded.
3. Export that PNG. The example **does not call the API itself**. You send the PNG to
   `POST pano:depth_to_rgb` with `z_min`, `z_max` and a `text_prompt`.
4. Feed the returned `pano_url` to `worlds:generate` as `type:"image", is_pano:true`. Pano
   credits are 0, so the whole chain costs about 80 (depth→RGB, **[inferred]** from the pano rate)
   + 1,500.

**[inferred]** Unity can do step 2 natively: `Camera.RenderToCubemap` plus a small shader that
writes radial depth. A room described in code (walls in metres, columns against the walls,
nothing free-standing, per QUALITY.md's Chisel rules) could become a depth PNG without leaving the
project. Whether the result matches web-app Chisel quality is experiment 4, and it is unknown.

### Proposed shape: one room, many worlds (Saul, 28 Sep)

Build a basic room from cubes once, render its **depth** panorama once, and ship that PNG in the
APK. Each visitor's words become the `text_prompt` for `pano:depth_to_rgb` against the same depth
image. That call returns a new painted panorama, which goes to `worlds:generate`.

- Ship the **depth** pano, not an RGB pano. An RGB panorama sent to `worlds:generate` *is* the
  world, and text barely steers it. The depth pano fixes the geometry and leaves the look to the
  text.
- At 2048×1024 the PNG is well under the 10 MB `data_base64` limit, so it goes inline with no
  upload step.
- Every generated world shares one layout. So the spawn, the scale and the wall anchors for
  hanging artworks can be decided once, in advance. **The collider can be the cubes themselves**,
  exact and complete, instead of Marble's patchy one (CLAUDE.md: van-gogh's collider struck walls
  on 6 of 12 rays).
- **Unknown until measured:** how faithfully `depth_to_rgb` follows the geometry, and whether the
  reconstructed world keeps the walls where the depth pano put them. If it drifts, the cube
  collider and the splats disagree. Experiment 4 answers both.

**World size.** A text-prompt world's extent is whatever the model chooses. A depth-pano world is
bounded by the geometry you drew. Separately, QUALITY.md notes that `marble-1.1-plus` grows a world
by up to five "cubes" while plain `marble-1.1` does not. So for compact rooms, use depth→pano
with `marble-1.1`, never Plus.

## What it does not offer

- **No Chisel, Expand, Variations, Pano Edit or Compose endpoint.** Those are web-app features
  only. `DepthPanoPrompt` and `InpaintPanoPrompt` exist in the spec, but only as output variants
  describing how a world was made; `worlds:generate` does not accept them [doc].
- **No streaming or LoD format.** You get fixed-tier SPZ and PLY. Spark 2.x's `.RAD` LoD format
  is browser-only [doc].
- **No Unity SDK.** World Labs points at aras-p/UnityGaussianSplatting (our renderer) and the
  winnie1994 fork. Its community FAQ says 500k SPZs throw "Index out of range" in upstream,
  recommends 6.0 over 6.3 and Multi-view, and says 2M crashes a Quest 3 [doc]. **Treat that as
  anecdote. This project runs 6000.3 and Multi Pass, has a patched reader, and has measured its
  own ceiling at ~3.3M** (QUALITY.md).
- **No usable official client library.** `worldlabs-api-python` 0.1.0 is source-only and
  "for experimentation", and there is nothing on npm [doc; the npm/PyPI 404s were measured by the
  research agent]. Our `Tools/marble/marble.mjs` is already as complete as anything official.

## Coordinate frame — the trap that will cost a day if missed

API SPZs are in `marble_raw_opencv`: Y-down, in Marble's own units [doc]. The web app has exported
in OpenGL frame since 11 Dec 2025, with a choice added on 1 Jan 2026 [doc, release notes]. **Every
`.spz` in this project came from the web app, so every spawn in `WorldCatalog` is in the baked
frame.** An API download dropped in beside them would come out upside down and at the wrong scale.

The transform is documented [doc, `rendering-spz`] and muse-infinity already implements it for
`rawMarble` worlds (`lib/museum3d.js:286-346`):

1. Rotate 180° about X.
2. Scale positions, and linear Gaussian scales, by `metric_scale_factor`. For log-scale fields,
   add `ln(factor)` instead.
3. Subtract `ground_plane_offset` from Y. This applies to positions only, not Gaussian sizes.
4. Apply the **same** transform to the collider GLB. On top of that, keep the X mirror this
   project already applies to glTFast colliders (CLAUDE.md, "Marble colliders are X-mirrored").
   Whether the two compose cleanly has to be measured on the first real world.

The payoff is that the result stands in **metres with the ground at y=0**, which is exactly the
per-world data hand-measured over 27 commits for `WorldCatalog`. A generated world arrives with
its floor known. Where to stand on it is still open. **[inferred]** The panorama's capture point
(the origin) is the natural spawn, since that is where the world is sharpest (the worklist
measured sharpness falling off beyond ~15 m from the centre).

## What it would take in MuseXR

Three pieces, none started:

1. **Calling the API from the headset.** The key is injected at build time like the other keys
   (decision above). The client downloads the assets the moment an operation succeeds, because the
   URL lifetime is undocumented. It must never retry `worlds:generate`. A proxy stays the upgrade
   path if the app ever leaves the event.
2. **Runtime SPZ → `GaussianSplatAsset`.** Move `SPZFileReader` and the processing half of
   `GaussianSplatAssetCreator` into a runtime assembly, and replace the `AssetDatabase` writes with
   `new TextAsset(bytes)` + `ScriptableObject.CreateInstance<GaussianSplatAsset>()`. It must keep
   the **shLevel-0 patch**: every Marble SPZ is SH-less, and without the patch they convert
   silently to zeros (CLAUDE.md). **Alternative:** a desktop helper does the conversion and ships the four
   Unity-ready byte blobs, so the headset only wraps them. The device then does less work, at the
   price of a C# (or re-implemented) converter on the server. **Unmeasured:** how long a 500k
   conversion takes on a Quest 3S CPU. The Editor does five in ~4 s on a desktop.
3. **A `WorldDefinition` built at runtime.** Its spawn and scale come from `semantics_metadata`
   rather than the catalogue, and its collider is loaded with glTFast at runtime (already
   installed, and already used for Tripo downloads). Tier: **500k** to match everything else;
   100k exists and is ~20× lighter if a generated world has to sit alongside a loaded one.

What stays true: the render settings in CLAUDE.md are load-bearing (MSAA 1, renderScale 0.8,
HDR on, Multi Pass, splat layer at 0.6), and a generated world is subject to all of them.

### Where it fits in the journey — options, not decisions

| | Shape | Cost per visitor | Risk |
|---|---|---|---|
| **A. Pre-generate, curate** | Generate offline from prompts (or via depth→pano), review, ship as today | $1.26 per world, once | none new; it is only "Marble via script instead of via web app" |
| **B. Visitor's world, delivered later** | Visitor's words at stage 01–03 → headset starts a generation → world opens at 04+ | $1.26 (or $0.18 Draft) | 5 min must fit; unvetted content; needs pieces 1–3 |
| **C. Pano first, world if approved** | text → pano (80 credits, fast) shown as a 360° backdrop; generate the world only if the visitor keeps it | $0.06 per pano + $1.20 per kept world (0 credits for the second pano step) | pano timing unpublished; the pano itself is a cheap, instant-feeling preview |

**[inferred]** C is the one that feels dynamic: a panorama returns far sooner than a world, costs 80
credits, is a usable skybox immediately, and turns into a world for 1,500 more.
Pano latency is not published. It is the first number to measure.

## Existing code — what is right and what to fix when it is next touched

`Tools/marble/marble.mjs` (12 Sep) matches the spec on base URL, header, endpoints, poll path,
`spz_urls`, `semantics_metadata` and export. When it is next used:
- read `world.world_id ?? world.id` (the schema says `world_id`);
- after `done`, fetch `GET /worlds/{id}` instead of trusting the snapshot in `response`;
- send `permission` explicitly;
- add `marble-1.0-draft`, image/pano input with `data_base64`, and `pano:depth_to_rgb`;
- add a `credits` command (free) as the first check for a new key.

## Experiments to run once a key exists — cheapest first

| # | What | Cost | Answers |
|---|---|---|---|
| 1 | `GET /credits` | free | key works and points at the right account |
| 2 | text world with `marble-1.0-draft` (time the pano stage separately if the operation exposes it) | $0.18 | Draft latency and whether its quality is usable on Quest at all |
| 3 | same prompt, `marble-1.1` | $1.26 | real standard latency; diff the frame and `semantics_metadata` against a web export of a similar world |
| 4 | render a box-room depth pano (known metres) → `pano:depth_to_rgb` → world | ~$1.26 | whether code can make a Chisel-quality room to a designed size |
| 5 | API 500k SPZ through our converter | free | whether the "Index out of range" report applies to our patched reader |

About $3 in total, which is inside the $5 minimum purchase.

## Open questions the docs do not answer

1. How long the asset URLs live, and what `expires_at` governs.
2. Latency per tier, Draft and pano especially.
3. Whether concurrent running jobs are capped.
4. Whether the 100k/500k/full tiers from the API share one frame and one `semantics_metadata`.
5. Whether an API route for Chisel, Expand or inpainting is on the way.
6. What "attribution as specified in the API documentation" means; no page specifies it.

Sources are saved for re-reading in the session scratchpad (not in git): the full OpenAPI spec and
the pricing, rate-limit, errors, models, rendering-spz, export-specs and Unity pages.

## The test room — `Assets/Scenes/Tests/DepthRoom.unity` (28 Sep 2026)

A cube room shaped like the **Buddha Hall (Chisel)**, as Marble actually built it. It was measured
from that world's 500k splats (wall, floor and ceiling density peaks), not from the collider.

- **Frame: the splats at scale 1.7.** At that scale the hall is its designed width and height:
  20 m wide (x ±10), 19.2 m deep (z −13.4 back wall … +5.8 front wall; Marble built it short of
  the designed 28 m), 15.4 m high, floor at y 0. There are 4 columns per side, 1 m square, at
  x ±8.1, z −10.0 / −6.1 / −2.3 / 1.9. The pano capture point is `(0, 1.6, 0)`.
- The real splats sit in the scene as `REFERENCE Buddha Hall Chisel 500k (x1.7)`, tagged
  `EditorOnly`, so the scene can never ship them.
- **Fit, by blind review of a density overlay:** walls, floor and ceiling are within ~0.3–0.5 m.
  Columns are the right count and spacing, but their visible faces sit 0.3–0.5 m further toward
  the room's centre than the cubes. The splats extend 1–2 m beyond the shell at the far end and
  behind the back wall (alcoves) and have no cube counterpart; the cubes are the simplified room.
  Raycasts from the capture point hit a surface in all 81 test directions (the room is closed).
- **Finding: the Buddha Hall's `worldScale` 2.63 renders it 1.58× larger than designed** (31 × 30
  × 24 m). 2.63 was read off the collider's raw extent, but a web-downloaded collider carries
  Marble's metric transform in its root node matrix (scale 1.58 plus the Y/Z flip for this hall).
  The web-exported splats are already in metres (11.8 m wide at scale 1, the same as the collider
  × 1.58). So the scale for the designed size is ~1.7, the same as the other Chisel rooms. Nothing
  has been changed; the hall may be preferred larger.

## Step 1 done — the depth panorama (28 Sep 2026)

`MuseXR > Dynamic Worlds > Bake Depth Pano` (`Assets/Editor/DepthPanoBake.cs`, logic in
`Assets/Scripts/Worlds/DepthPano.cs`) casts one physics ray per pixel from
`Pano Camera (capture point)` and writes the result to `Tools/marble/depthpano/`:

- `depthroom-depth.png`, 2048×1024, 8-bit grey;
- `depthroom-depth.json`, holding `z_min` 1.600 and `z_max` 21.501, which the API needs to decode
  the PNG.

It takes about a second, and no rays missed. The encoding is World Labs' own, copied from their
example: log, near = white, and farthest and "nothing hit" both black, as theirs are.

- **Tests:** the 9 tests in `DepthPanoTests` pass. The full EditMode suite is 494/494.
- **An independent check that can fail:** a Python decoder read the PNG back and compared it with
  the room's real distances (front wall 5.8 m, back 13.4, sides 10, ceiling 13.8, floor at 45°,
  a column). All were within 0.5%. A front/back swap, a flipped up/down, or a linear encoding
  would each fail it.
- **Looked at, by me and by an impartial reviewer:** floor, ceiling, the nearest (front) wall in
  the centre, four columns per side in correct perspective, the back wall at the seam. The
  left/right edges meet exactly, the poles are uniform, and there are no holes.

Next: `node Tools/marble/marble.mjs credits`, then `depth2rgb`, once `WORLDLABS_API_KEY` exists.

## Step 5 measured — depth → painted panorama (29 Sep 2026)

This is the first paid call. The prompt was built from a random muse path: question "How do I live
with uncertainty?", companions Frida Kahlo, Monet and Socrates, and Socrates' answer *perception*.

| | Measured |
|---|---|
| Time | **19 s** from submit to `SUCCEEDED` (polled every 3 s) |
| Cost | **80 credits** (line item "Depth to RGB"), $0.064 |
| Output | 2304×1152 RGB PNG, 2.8 MB, on `cdn.marble.worldlabs.ai` |
| `expires_at` | **3 hours** after creation, not the 1 hour in the docs' example |
| Result location | **`response.assets.imagery.pano_url`**: a World-shaped response with empty `world_id`, **not** `response.pano_url` as the spec's `PanoDepthToRgbResult` says. `marble.mjs` reads both; `fetch-pano <operation_id>` re-downloads a finished one for free |

The geometry was followed closely: 4 columns per side, at the depth panorama's positions.

## Steps 5–8 — first generated world in Unity (29 Sep 2026)

1. **Re-paint with a matte floor** (op `3802fc79…`): 19 s, 80 credits. The first panorama's mirror
   floor was the risk: reflections can turn into a ghost room under the floor.
2. **Panorama → world** (`marble-1.1`, world `2fa37677-9a02-4173-a6d9-960da154b6eb`, private):
   **471 s, 1,500 credits.** The tiers returned were 100k, **150k**, 500k and full_res; the 150k
   tier is not in the docs. The download is at
   `Tools/marble/worlds-out/2fa37677-…/`: 500k `.spz` (7.3 MB), collider `.glb` (9.4 MB),
   pano (11 MB), `semantics.json`.
3. **Converted** at Medium to `Assets/Worlds/Generated/depthroom-uncertainty-500k.asset`
   (500,000 splats; the bounds are non-zero, so the SH-less reader patch held). It is placed in
   `DepthRoom.unity` as `GENERATED World (Marble API)`.

**Placement: two findings.**
- **`metric_scale_factor` does not honour our geometry.** With the documented transform (Rx 180,
  × 0.7723, + 0.4529) the room comes out 5.3 × 5.1 × 4.1 m, against our 20 × 19.2 × 15.4 m.
  Marble infers scale from the painted image and ignores the `z_min`/`z_max` we sent. Because we
  know the geometry, the fix is a fit: **one factor, 3.769, matches width, front wall, back wall
  and ceiling to within 2%** (3.754–3.841). So the Unity scale is 0.7723 × 3.769 = **2.911** and
  the lift is 0.4529 × 3.769 = **1.707 m**.
- **Front and back come out swapped** relative to our pano's forward (+Z). Rx 180 plus a
  measured yaw of 180 gives **rotation (0, 0, 180)**. The room is mirror-symmetric in x, so a
  left/right mirror cannot be ruled out from this room; an asymmetric room would settle it.
- Columns land at z −9.2 / −5.8 / −2.2 / 1.8 against the cubes' −10.0 / −6.1 / −2.3 / 1.9, with
  faces ~0.4 m further toward the room's centre. That is the same offset the Buddha Hall Chisel
  room showed.

**Cost of this whole first run:** 80 + 80 + 1,500 = 1,660 credits ≈ **$1.33**, of which $0.06 was
the discarded mirror-floor pano. 5,340 credits are left.

## Timing, Draft, and comparison with Chisel (29 Sep 2026)

All three runs used the same painted panorama (`depthroom-depth-3802fc79.png`).

| Run | Model | World generation | Credits | Splats | Scale data |
|---|---|---|---|---|---|
| 1 | `marble-1.1` | **471 s** | 1,500 | 500,000 (7.3 MB) | 0.7723 / 0.4529 |
| 2 | `marble-1.1` (repeat) | **297 s** | 1,500 | 500,000 (7.3 MB) | 0.7723 / 0.4540 |
| 3 | `marble-1.0-draft` | **≤ 20 s** (done at the second 10 s poll) | 150 | 497,664 (4.9 MB) | **none returned** |

- **Standard generation varies a lot:** 5.0–7.9 min over two samples. The docs' "about 5 minutes"
  is the good case.
- **The metric data is nearly deterministic** for the same panorama (0.7723 both times), so one
  fitted scale per room would carry across generations. That is inferred from 2 samples.
- **Draft fits the same way.** Its scale comes only from the fit: one factor, 2.902, matches all
  walls within 3%. The rotation is the same (0, 0, 180) and the lift is 1.752 m.
- **Draft quality (blind review):** the same geometry everywhere, but roughly 2× blurrier. The door
  carving is gone, the petals are blobs, the column stripes are mushy. It has none of the standard
  world's wall blotches. It works as a stand-in or a quick preview, not as the final world.
- **Where the time goes** (standard): painted pano 19 s + generation 297–471 s + download of
  ~17 MB (seconds) + Unity conversion **1.35 s** on the desktop (Draft 0.84 s). Generation is
  over 95% of it. Conversion on a Quest CPU is unmeasured, but even 20× slower would be under
  30 s.

**Against the Buddha Hall Chisel world**, both at the size where they fill the same 20 m room:

| | Chisel (web export, x1.7) | Generated (API, fitted) |
|---|---|---|
| Splats / file | 500,000 / 7.3 MB | 500,000 / 7.6 MB |
| Inside the room shell | 97.2% | 98.6% |
| Splats per m² of room surface | 246 | 250 |
| Median splat size | 2.9 cm | 2.5 cm |
| Faint splats (opacity < 0.1) | 21.6% | 13.8% |
| "Trained with anti-aliasing" flag | **1** | **0** |

So the API world is a like-for-like replacement for a Chisel room: the same budget, density and
extent. Headset frame rate should therefore match the Chisel rooms (50–66 FPS at 500k,
QUALITY.md), but that has not been measured.

Saul reports the microphone works on the headset, which removes that gap from the speech → world
path.

## On the headset — the whole chain runs on a Quest 3S (29 Sep 2026)

`Assets/Scenes/Tests/DynamicWorld.unity`, built with `MuseXR › Build › Quest › Dynamic`
(`Builds/MuseXR-Quest-Dynamic.apk`, ~59 MB). A menu offers three worlds (UNDERWATER, VOLCANO,
ICE) plus a STANDARD / DRAFT quality switch. **Nothing starts until a plate is pressed**, and
there is one world per launch. The flow is:
1. The depth pano shipped in StreamingAssets plus the plate's prompt go to World Labs.
2. The painted panorama is shown as the sky.
3. World Labs builds the world.
4. The 500k `.spz` is downloaded, **converted on the headset**
   (`RuntimeSplatAssetBuilder`), and placed on the DepthRoom frame.

The grey cubes stay as invisible colliders, so walking stops at the generated walls. Every step is
logged as `[DynamicWorld]`.

| Run | Model | Paint pano | Build world | Download + convert on Quest | Click → world | Cost |
|---|---|---|---|---|---|---|
| Rainbows | marble-1.1 | 20.2 s | **358.8 s** | 2.9 s | ~6.5 min | $1.26 |
| Ice | draft | 17.8 s | **15.5 s** | 2.6 s | ~48 s | $0.18 |
| Volcano | draft | 17.4 s | **10.4 s** | 2.3 s | ~44 s | $0.18 |

- **On-headset conversion is fast:** decode takes 1.0–1.3 s and encode 0.6–0.7 s for 500k
  splats. That is about the desktop's speed; Burst runs on the Quest.
- **The two placements agree:** Marble's ground data gives a lift of 1.698 m and the floor-band
  estimate 1.711 m. Draft has no metric data and uses the floor band (1.69 m).
- **The runtime converter matches the Editor's.** The test checks chunks and SH byte-for-byte;
  positions and colours differ by at most one rounding step on about 0.1% of values.
- **Not yet judged in the headset by a person:** Draft vs standard quality up close, and depth when
  walking. Headset screenshots show recognisable rooms (the rainbow hall, the ice cathedral, the
  volcanic forge).
- **A relaunch mid-build loses the world on the device**, but World Labs still finishes and
  charges for it. The underwater world (operation `27ab6f2c…`) was lost that way.

A Chisel world made by hand in the Marble web app with the same kind of prompts:
`Tools/marble/forbidden-city-chisel-prompts.txt` (room, panorama, fix, create).
