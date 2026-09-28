# Marble splats on Quest 3S: quality research (28 Sep 2026)

This is web research only. Nothing in the project was run or changed. I've tagged every claim:
**[OFFICIAL]** means the vendor's own docs or blog. **[ACADEMIC]** means a paper. **[COMMUNITY]** means a user, press or third party. **[INFERENCE]** means my own reasoning, which I haven't verified.
Dates are as recent as I could find. Where a page had no date, I've said so.

---

## 1. Marble / World Labs

### Export tiers
- **[OFFICIAL]** The export specs list SPZ and PLY at "~2M" and "~500k" splats. It says SPZ is "optimized for file size" and PLY is "uncompressed… compatible with more software". The same page lists a collider GLB (100–200k triangles) and an HQ mesh (600k triangles textured, or 1M vertex-coloured). The page has no date. https://docs.worldlabs.ai/marble/export/specs
  - It says **nothing** about SH degree, file sizes, or VR/mobile budgets.
- **[OFFICIAL]** The export index says: *"The lower-resolution files have been optimized to be as perceptually similar as possible to the higher-resolution files."* No date. https://docs.worldlabs.ai/marble/export/gaussian-splat/index
  - This supports what we measured: the 500k tier is a separate reduction or reconstruction, not a subset of the full export.
- **[COMMUNITY, third-party docs]** Scenario's Marble guide (updated about Jul 2026) lists three tiers: `100k`, `500k` and `full` (the default). Its advice: "full for hero worlds… step down to 500k or 100k for lighter files". https://help.scenario.com/articles/4774055514-marble-by-world-labs-the-essentials
- **[COMMUNITY]** One search summary says the API returns 100k, **150k**, 500k and full SPZ. I could not confirm the 150k tier on any official page, so treat it as unverified.
- **[OFFICIAL]** "Full" is not a fixed number. Marble 1.1 Plus (April 2026) "automatically expands its spatial coverage… up to five dynamic cubes", at 1,500 credits plus 300 per extra cube. https://radiancefields.com/world-labs-releases-marble-1.1-and-marble-1.1-plus · https://x.com/theworldlabs/status/2041554646561677701
  - **[INFERENCE]** This probably explains why our full exports came out at 4.32M in one case and 1.92M in another. A multi-cube world spreads its splats over more volume.
  - Scenario's advice matches this: use 1.1 for "contained interiors and single locations" and 1.1 Plus for expansive scenes.
- **[OFFICIAL]** Marble 1.1 (April 2026) claims "a major reduction in visual artifacts" over 1.0. Same sources as above.

### Release notes that matter to us
[OFFICIAL] https://docs.worldlabs.ai/marble/release-notes
- **11 Dec 2025:** exports switched to the OpenGL coordinate system (previously OpenCV). SPZ now defaults to **v2**, with v3 available as an opt-in.
- **1 Jan 2026:** added a choice between OpenGL and OpenCV coordinates.
- **20 Nov 2025:** Marble Studio moved to a "Level-of-Detail splat-rendering backend". That is the editor only; it does not change the exports.

### API metadata
- **[OFFICIAL]** The API returns `semantics_metadata` with `metric_scale_factor` and `ground_plane_offset`. Sources: https://docs.worldlabs.ai/api/faq and https://docs.worldlabs.ai/api/rendering-spz.md (no dates).
  - Apply `metric_scale_factor` to centres and linear scales. For log-scales, add `log(factor)` instead.
  - Subtract `ground_plane_offset` from Y on the **centres only**.
  - The raw SPZ is `marble_raw_opencv`. Do the axis conversion after the metric transform.
- **[OFFICIAL]** API models are `marble-1.0`, `marble-1.1` and `marble-1.1-plus`. 1.1-plus adds 0–1,500 variable cost for larger worlds.

### Official VR / Unity guidance
[OFFICIAL] "Exporting to Unity", no date, but it references Unity 6.3: https://docs.worldlabs.ai/marble/export/gaussian-splat/unity
- They recommend the fork **github.com/winnie1994/UnityGaussianSplatting**, which has "patches resolving draw-order issues". I couldn't tell from its README what that fork actually changes.
- Their other settings:
  - Unity **6.0**, not 6.3. They say the aras-p plugin doesn't do VR on 6.3. We run 6000.3 and have our own patches, so our experience already contradicts this.
  - URP with **HDR on**, Vulkan.
  - "Multi-view". Note that this contradicts our finding that Single Pass Instanced renders nothing.
- On tiers, they say the **500k tier is "better for standalone VR"** and was measured at "~12fps in Unity" against ~19 fps in PlayCanvas in the Quest browser. They also say it "offers better small details than 2M alternatives in some cases".
- On the **2M tier**, they say it "causes Quest 3 builds to crash when opening".
- Known issue: 500k SPZ throws an "Index out of range" on import. Their workaround is converting to PLY.
- **Takeaway:** World Labs' own Unity number is far below our 50–72 FPS, so our patched pipeline is already ahead of their published guidance. **They publish no Quest splat budget, and no guidance on reducing splat counts.**

### Chisel
- **[OFFICIAL]** The docs cover mechanics only: blocking with boxes, planes and walls, GLB/FBX templates, and camera placement setting where generation starts. https://docs.worldlabs.ai/marble/create/chisel-tools/chisel-basics
  - **I found no official guidance on room size, scale or quality for Chisel.**
- **[OFFICIAL]** The image-prompt tips are for sharper scenes: clear depth layers, defined floors, walls and ceilings, no extreme close-ups, no blur. https://docs.worldlabs.ai/marble/create/prompt-guides/image-prompt.md
- **[COMMUNITY, press]** "Details beyond the image frame are hallucinated", so typical splat artefacts get worse the further you move from the source view. https://www.uploadvr.com/marble-turns-an-image-into-a-webxr-volumetric-scene-in-minutes/
- **[INFERENCE]** This fits our finding that smaller rooms look better. A fixed 500k budget spread over a smaller volume gives more splats per square metre of wall. It also puts the visitor closer to the generation viewpoint.

### Community VR projects on Marble
- **[OFFICIAL case study, 12 Nov 2025]** Daniel Skaale's "Splat World" runs in Unity on Quest 3, standalone and tethered. It is structured around a **10×10 m grid** "to balance frame rate and fidelity" and uses GPU profiling to keep splat counts in check. No counts or FPS are published. https://www.worldlabs.ai/case-studies/1-splat-world
- **I found no Reddit or X threads with measured Marble-on-Quest numbers beyond World Labs' own.** Evidence here is thin.

---

## 2. Gaussian splats on Quest and standalone VR

| Who | Budget / result | Technique | Source (date) |
|---|---|---|---|
| **Meta Spatial SDK** [OFFICIAL] | *"avoid… splat count greater than 150k"*; one splat at a time; Quest 3/3S only; .spz preferred | Built-in renderer | https://developers.meta.com/horizon/documentation/spatial-sdk/spatial-sdk-splats/ (updated 10 Nov 2025) |
| **Spark (World Labs)** [OFFICIAL] | **"Quest 3: 1 million splats or less, not too many splats concentrated in a small area."** For VR, `maxStdDev = sqrt(5)` instead of the default sqrt(8), "perceptually very similar". Splat MSAA is off. | WebGL2 in Three.js; GPU distances, CPU radix sort in a worker | https://sparkjs.dev/docs/performance/ (no date; Spark 2.x era) |
| **Spark 2.0** [OFFICIAL] | LoD budget "defaults to 1.5M on desktop, 500K on mobile" (range 500K–2.5M); streams 100M+ splat worlds | LoD splat tree (voxel-octree merge, base 1.5, or Bhattacharyya pair-merge), `.RAD` streaming, **foveation** (`coneFov`, `coneFoveate` 0.1 means 10× larger splats outside the cone, `behindFoveate`) | https://www.worldlabs.ai/blog/spark-2.0 (14 Apr 2026) · https://sparkjs.dev/docs/new-spark-renderer/ |
| **PlayCanvas / SuperSplat** [OFFICIAL] | Global budget "1 million for mobile, 3+ million for desktop"; turn off antialiasing and device pixel ratio | Streamed SOG LOD (splat-transform decimates into chunked LODs); WebGPU compute renderer (35M splats: 13 to 76 fps on desktop) | https://developer.playcanvas.com/user-manual/gaussian-splatting/building/performance/ · https://radiancefields.com/supersplat-ships-compute-based-webgpu-rendering-and-automatic-streamed-lod (Jun–Jul 2026) |
| **ninjamode Unity VR fork** (the source of aras-p's VR support) [COMMUNITY] | **"72 fps stable until around 400k Gaussians"** on Quest 3 | "Center eye sorting" (sort once for both eyes), Quest-compatible radix sort | https://github.com/ninjamode/Unity-VR-Gaussian-Splatting (undated, "unmaintained") |
| **aras-p issue #145** [COMMUNITY] | 72 fps drops to **~10 fps up close** in VR; closed "not planned" | The cause is fill rate and overdraw from large on-screen splats | https://github.com/aras-p/UnityGaussianSplatting/issues/145 (23 Nov 2024) |
| **GSVision** (Quest viewer) [COMMUNITY, press] | Recommends **700k**, "lag noticeable around 1.2 million"; **Sort Every Nth Frame = 20** for smoothness | Infrequent sorting | https://www.uploadvr.com/the-best-ways-to-view-gaussian-splats-on-quest/ (17 Sep 2026) |
| **Horizon Hyperscape** (Meta) [COMMUNITY, press] | "best combination of quality and smoothness" | **Cloud-rendered and streamed** ("Avalanche"), not on-device | https://roadtovr.com/meta-horizon-hyperscape-photorealistic-app-quest-3-s/ · https://voicesofvr.com/1470-metas-hyperscape-serves-cloud-rendered-photorealistic-gaussian-splat-captures/ |
| **Gracia** [COMMUNITY, press] | Standalone Quest 3 / PICO 4, "tradeoff of some quality" | Its own fast pipeline; WebGPU streaming (Mar 2026) | https://www.uploadvr.com/gracias-dynamic-gaussian-splats-moving-volumetric-scenes-now-on-quest-3/ |
| **Into the Scaniverse** (Niantic) [COMMUNITY] | Smooth, but scenes "lack detail" (small phone captures) | SPZ, about 10× smaller than PLY | https://voicesofvr.com/1525-… · https://www.uploadvr.com/the-best-ways-to-view-gaussian-splats-on-quest/ |
| **AAU Klagenfurt, VCIP 2026** [ACADEMIC] | Quest 3 at 72 Hz: **"Resolution scale is the dominant GPU cost driver"**; "splat density reduction is only an effective performance lever when GPU time is already close to the 72 Hz frame budget" | A 180-condition sweep of resolution 0.5–1.0, density 10–100% and distance | https://athena.itec.aau.at/2026/09/3d-gaussian-splatting-rendering-performance-trade-offs-on-the-meta-quest-3/ (16 Sep 2026) |
| **Atlas** [ACADEMIC] | 70.9 FPS city-scale, but on a **simulated hardware accelerator**, not a shipping Quest | Temporal LoD search, stereo rasterisation, memory offload | https://arxiv.org/abs/2609.02352 (Sep 2026) |
| **VRSplat** [ACADEMIC] | >72 FPS, but rendered on an **RTX 4090** and only displayed on Quest 3 | Foveated rendering plus popping and anti-aliasing fixes | https://arxiv.org/html/2505.10144v1 (May 2025) |
| **SIGGRAPH 2026 op-ed** [COMMUNITY] | "On a Quest running standalone, the practical ceiling is closer to a few hundred thousand" | Points to foveation, compression and slice streaming as the way forward | https://vr.org/articles/gaussian-splatting-vr-siggraph-2026 (Jul 2026) |

### What this means for us
**Our 500k at 50–72 FPS on a 3S is at or above every standalone Unity figure published.** Only Spark claims about 1M on Quest 3, and that comes with the caveat "not too many splats concentrated in a small area" and with LoD and foveation doing the work.

**Nobody publishes a standalone Quest renderer that holds 72 fps on a flat 1M+ splat set.** The ones that go beyond that use LoD, foveation or cloud rendering.

### Tool currency
[OFFICIAL] aras-p **v1.1.1 (9 Apr 2025) is still the latest release**. We are current. https://github.com/aras-p/UnityGaussianSplatting/releases

### Techniques that transfer to Unity
- **`maxStdDev` / quad extent** (Spark recommends sqrt(5) for VR). It cuts the fragment area of every splat by about 37% (5/8), with little visible change. This is a fill-rate lever, and fill rate is the bottleneck the Klagenfurt study and issue #145 both point at.
  - **[INFERENCE]** Check what cutoff our patched aras-p shader uses before assuming there is headroom.
- **A render-once-per-eye-pair sort**: centre-eye sorting in ninjamode, one sort per frame in ours.
- **Infrequent sort**: GSVision uses every 20th frame. We already sort every other frame.
- **LoD and foveation (Spark 2.0 approach).** Outside the view cone, splats are replaced by coarser merged parents. The Quest 3S has no eye tracking, so this would be fixed or cone foveation only.
  - **[INFERENCE]** Quest's own fixed foveated rendering (a Vulkan fragment density map) applies to the swapchain. It probably does **not** reach aras-p's private splat render target, so it is unlikely to help the splat pass as things stand.

---

## 3. Reduction methods: what works without the source images

**The key fact.** Every quality-preserving method below either needs **no images** (pure geometric merging) or needs images **only for a short fine-tune of colour and opacity**.

We don't have Marble's images, but we can render our own "ground truth" from the full-resolution splat. The full export is the best model of the scene we have, and it can play the teacher: render hundreds of views of it along the walkable area, then fine-tune the reduced splat to match them.
- **[ACADEMIC]** LightGaussian already uses "knowledge distillation and pseudo-view augmentation" for its SH stage (NeurIPS 2024). https://arxiv.org/abs/2311.17245
- **[ACADEMIC]** Distilled-3DGS supervises a 12%-size student entirely on teacher-rendered pseudo images (Aug 2025). https://arxiv.org/abs/2508.14037
- **[INFERENCE]** Neither paper tests the "teacher = only data we have" case on generated worlds specifically, but nothing in either method requires real photographs.

| Method | Needs images? | Post-hoc on a finished splat? | Notes / source |
|---|---|---|---|
| **NanoGS** (Mar 2026) [ACADEMIC] | **No**, "training-free" | **Yes**, runs on CPU | Opacity-prune, then a kNN graph, then greedy pairwise **merge by mass-preserving moment matching**. Beats LightGS, PUP-3DGS and GHAP's prune-only stages by +2.4 dB at 10% kept. Says it avoids "sparse high-intensity splats" and "missing-geometry" failures. Code: https://github.com/saliteta/NanoGS (Python), https://github.com/RongLiu-Leo/NanoGS (web). https://arxiv.org/abs/2603.16103 |
| **splat-transform `--decimate` / `--decimate-adaptive`** [OFFICIAL, PlayCanvas] | **No** | **Yes**, CLI | "progressive pairwise merging"; `--decimate-adaptive` "allocates removal by local error". Output must be `.ply`, then convert. Reads **.spz** and writes .spz, .ply and .sog. v3.6–3.7 (Sep 2026). https://github.com/playcanvas/splat-transform · https://developer.playcanvas.com/user-manual/splat-transform/ |
| **Spark Bhatt-LoD / Tiny-LoD** [OFFICIAL] | No | Yes | Bhattacharyya-distance pair merging. It builds a LoD tree rather than a flat target count, but the merge step is the same idea. https://sparkjs.dev/docs/new-spark-renderer/ |
| **GHAP** (NeurIPS 2025) [ACADEMIC] | Geometry step **no**; appearance step **yes** | Yes | Blockwise KD-tree optimal-transport mixture reduction. **Decisive ablation at 10% kept: geometry-only gives PSNR 14.0; after a colour and opacity fine-tune, 23.3.** https://arxiv.org/html/2506.09534v2 · https://github.com/DrunkenPoet/GHAP |
| **PUP-3DGS** (CVPR 2025) [ACADEMIC] | Yes (Hessian sensitivity over views), but **rendered views could substitute** | Yes, "any pretrained 3D-GS model", multi-round prune and refine | https://arxiv.org/abs/2406.10219 |
| **Coreset pruning** (Jul 2026) [ACADEMIC] | Yes (views for sensitivity); rendered views could substitute | Yes. Best **prune-only** results at 90% pruning: 18.2 dB against 14.5–16.8 for the others | https://arxiv.org/html/2607.02721v1 · github.com/waseem-m/3dgs_provable_coresets |
| **LightGaussian** (NeurIPS 2024) [ACADEMIC] | Significance score uses training views; SH distillation uses pseudo-views | Yes, with fine-tune | https://arxiv.org/abs/2311.17245 · https://github.com/VITA-Group/LightGaussian |
| **KISS-GS / POPSpa** (ECCV 2026) [ACADEMIC] | The SOG-XT encoding needs none; POPSpa pruning needs views (about 10k iterations of refinement) | Partly | Code "coming soon". https://fraunhoferhhi.github.io/KISS-GS/ |
| **Mini-Splatting, Compact-3DGS (Lee et al.), EAGLES, Octree-GS, MaskGaussian, Taming-3DGS** [ACADEMIC] | **Yes**, they are training-time methods | No; they need a retrain from images, or from rendered views as a substitute | Mini-Splatting https://arxiv.org/abs/2403.14166; the rest are standard literature (not re-fetched this session) |
| **Retraining at a target count** (e.g. gsplat MCMC with a `cap_max`) [ACADEMIC/tool] | Yes; **rendered views of the full splat work as a dataset** | Initialise from the full or merged splat | gsplat supports MCMC with a hard Gaussian cap. https://docs.gsplat.studio/main/_modules/gsplat/strategy/mcmc.html |

### Ready tools
- **splat-transform** (npm `@playcanvas/splat-transform`): decimate, `--filter-floaters`, `--filter-box` / `--filter-sphere`, `-H` to strip SH bands, `--filter-nan`, and SPZ in and out.
- **NanoGS** (Python).
- **gsplat** (Apache-2.0) for a distillation fine-tune.
- **SuperSplat** for manual work (section 4).

---

## 4. Manual cleanup tools and round-trips

- **SuperSplat 3** [OFFICIAL/press, Sep 2026] is free, open source and moved to WebGPU. https://digitalproduction.com/2026/09/18/supersplat-3-moves-editing-to-webgpu/ · https://developer.playcanvas.com/user-manual/supersplat/editor/import-export/
  - Selection tools: rectangle, brush, lasso, polygon, **sphere and box**, plus Selection Depth and Footprint modifiers. It also has colour grading.
  - **Imports:** ply, compressed.ply, splat, ksplat, **spz**, lcc, sog.
  - **Exports:** PLY, compressed PLY, SOG and **SPZ** (v4 default, v3 legacy).
  - This is the cleanest round-trip for our pipeline: **SPZ in, SPZ or PLY out.**
  - **[INFERENCE]** Check that the aras-p importer reads SPZ **v4**. v1.1.1 predates it, so export v3 or PLY to be safe.
- **splat-transform `--filter-floaters [size,opacity,min]`** [OFFICIAL] removes Gaussians "not contributing to any solid voxel". Defaults are 0.05 / 0.1 / 0.004. PlayCanvas advises running it **before** decimation.
- **Postshot** (Jawset) [COMMUNITY/press] is a trainer first. It has a crop-box editor and exports ply, splat and ksplat. **I found no evidence that it imports a foreign SPZ or PLY for editing**, so it is not a fit without source images. https://radiancefields.com/platforms/postshot
- **Houdini: Nodeconnector "Gaussian Splat Cleanup HDA"** [COMMUNITY, only surfaced in a search summary; not verified] "removes duplicates, floaters, alpha noise and hue outliers, and fills holes".
- **aras-p's in-editor tools** (v0.6–0.9) already give selection, delete, cutouts and splat merging inside Unity. They are the least-effort way to crop what the visitor can never see.
- **Polycam / Luma** are capture services. I found nothing relevant to editing an existing Marble SPZ.

---

## 5. Why reduced splats get dark or coloured blotches and lose detail

**Evidence is thin.** Nobody has written specifically about "blotches after visibility pruning of a generated world". What follows is the mechanism the literature supports, plus our case.

1. **Pruning without refitting.** Surviving Gaussians were optimised to be seen *through* and *alongside* the ones you removed. Colours and opacities in 3DGS are jointly fitted; each Gaussian's colour is only right in combination with its neighbours.
   - **[ACADEMIC]** GHAP's ablation puts a number on it: the right geometry with un-refit appearance scored **PSNR 14.0**, and the same splat after a colour/opacity fine-tune scored **23.3**. https://arxiv.org/html/2506.09534v2
   - **[ACADEMIC]** Every prune-and-refine method (PUP-3DGS, LightGaussian, POPSpa) includes the refine step for this reason.
   - **[INFERENCE]** Deleting surface splats exposes Gaussians behind them that had **never been visible during training**. Their colours are unconstrained: often dark, saturated or wrong, because no gradient ever reached them. Seen up close, they show as dark or coloured patches.
   - **[INFERENCE]** This fits our reports on columns: curved surfaces seen from many angles are where "front" layers most often get removed for one viewpoint and not another.
2. **Pruning cannot move the survivors, but merging can.** NanoGS reports that prune-only baselines leave "sparse high-intensity splats" and "missing-geometry failures". Merging keeps coverage because the merged Gaussian grows to fill the space of the pair. https://arxiv.org/html/2603.16103v1
   - **[INFERENCE]** Our visibility pruning keeps small, high-coverage splats and drops small, low-coverage ones. That leaves gaps that show exactly where fine detail was, which matches the loss of detail up close.
3. **Size-based removal creates holes and haze** [COMMUNITY]: removing large Gaussians leaves "holes that appear as pale haze". https://github.com/kamyy/ai-gaussian-splatter/pull/141
4. **Anti-aliasing and splat-scale mismatches** [OFFICIAL, SPZ spec]:
   - The SPZ header has a flag bit `0x1` meaning "trained with antialiasing". https://developer.playcanvas.com/user-manual/gaussian-splatting/formats/spz/
   - Spark exposes `blurAmount` (~0.3, "with opacity adjustment… when anti-aliasing") separately from `preBlurAmount`.
   - **[INFERENCE, worth a free check]** If Marble's files set that flag and our renderer ignores it, small splats render with the wrong opacity. That gets worse at our 0.6 splat-layer resolution, where more splats fall below a pixel. We could see a speckled, sparkly look on floors, or brightness shifts after reduction changes splat sizes. **Reading the flag byte from our .spz headers costs nothing.**
5. **SH truncation** [INFERENCE]. If the full export carries SH bands above 0 and we render SH 0, any view-dependent colour correction is lost. A reduced set has fewer Gaussians to average that error away. Check the full export's `shDegree` header byte. Our 500k files are sh=0, but the full ones may not be.

### How others avoid it
- **Merge instead of delete:** NanoGS, splat-transform, Spark and GHAP.
- **Refit appearance on views afterwards:** GHAP, PUP-3DGS, LightGaussian and POPSpa.
- **Remove floaters first**, with voxel solidity (splat-transform) or by hand (SuperSplat).

---

## Ranked next steps for us (exported .spz only, Quest 3S, Unity)

1. **Merge-based decimation of the full export down to 750k–1M**, instead of visibility pruning.
   - **How:** `splat-transform in.spz --filter-nan --filter-floaters out_clean.ply`, then `--decimate-adaptive 1000000 out.ply` (or NanoGS). Crop first with `--filter-box` to the walkable region plus its sightlines.
   - **Benefit:** it keeps coverage, so there should be fewer holes and blotches than with pruning. It is the evidence-backed alternative to what failed.
   - **Cost:** hours. The tools are free CLIs and it runs on CPU.
   - **Risk:** low. Merged splats go soft, and fine detail up close will still be lost at 25% kept. Hand the output to the existing bench scene and headset A/B.
2. **Self-distillation fine-tune: the full export as teacher, the reduced splat as student.**
   - **How:** render a few hundred views, both eyes, along the walkable paths and at close range to columns and walls. Use gsplat to fine-tune **colour and opacity only**, and positions optionally, of the step-1 output against those images. Optionally use MCMC with `cap_max` to re-grow detail within a fixed budget.
   - **Benefit:** the largest expected gain. GHAP's ablation shows the refit is worth +9 dB at 10% kept. It directly targets blotches, because hidden Gaussians get recoloured or dropped.
   - **Cost:** 1–3 days to build a Python pipeline (render teacher, then train). About minutes to an hour of GPU per world on the RTX 5090.
   - **Risk:** medium.
     - The teacher's own artefacts get baked in, but they are already there.
     - The student can overfit the chosen viewpoints, so sample off-path views too.
     - Coordinate and SH conventions have to round-trip back into aras-p exactly.
3. **Free checks before any of that:** read each .spz header for the **antialiasing flag** (byte 14, bit 0x1) and **`shDegree`**. Also look at our shader's splat quad extent against Spark's VR recommendation of sqrt(5).
   - **Benefit:** if the AA flag is set and we ignore it, correcting it (Mip-Splatting-style opacity compensation) could fix speckle at every tier. The extent cut could buy fill-rate headroom for more splats.
   - **Cost:** minutes to read; a shader change to test.
   - **Risk:** low. It is a quality/performance A/B, and must be judged on the headset.
4. **Author for density: use 1.1 (not 1.1 Plus) and keep Chisel layouts compact.** Split a large venue into several small rooms, each its own 500k world, loaded per room the way the journey already swaps chapters.
   - **Benefit:** this is consistent with what we saw (small rooms look better) and with the official split between 1.1 and 1.1 Plus. Density per square metre is what reads as quality in VR.
   - **Cost:** generation credits plus layout time.
   - **Risk:** low technically, but design-constrained. **I found no official evidence about Chisel room size.**
5. **Manual cleanup in SuperSplat** before or after decimation. Delete floaters, the undersides of floors, and outside-the-walls splats the visitor never sees, which frees budget for the room.
   - **Benefit:** moderate. Every removed invisible splat is budget for a visible one.
   - **Cost:** 30–60 minutes per world, by hand.
   - **Risk:** low. Export SPZ v3 or PLY for aras-p compatibility.
6. **LoD and foveation in the renderer**, porting the Spark 2.0 or Streamed-SOG idea to our Unity fork: a merged LoD tree, coarse splats outside a central cone and behind the viewer, and a per-frame budget.
   - **Benefit:** the only route anyone has shown to 1M+ source splats on Quest-class hardware at a steady frame rate.
   - **Cost:** weeks. It is a significant renderer change: tree build offline, GPU cut selection per frame.
   - **Risk:** high for a hackathon. Popping and sort interaction are the risks.
7. **Not recommended:** pushing a flat 1M+ set. Our own 1M number (24–36 FPS) matches Spark's "≤1M on Quest 3" and the Klagenfurt study. Without LoD, more splats cost frame rate faster than they add quality, and Adreno's 128 MB storage-buffer ceiling caps us anyway.

### Where the evidence is thin
- No official Marble guidance on VR splat budgets, Chisel room size, or reduction.
- No public reports of anyone fixing Marble exports with distillation.
- The blotch mechanism is inferred from general 3DGS literature, not a Marble-specific source.
- The 150k API tier and the Houdini cleanup HDA came only from search summaries.
