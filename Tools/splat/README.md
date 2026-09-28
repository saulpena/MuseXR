# Tools/splat — visibility pruning for splat worlds

Deletes the splats a visitor can never see from where they are allowed to stand, and proves it.

## Setup (once)

```
py -3.12 -m venv Tools/splat/.venv
Tools/splat/.venv/Scripts/python -m pip install torch --index-url https://download.pytorch.org/whl/cu128
Tools/splat/.venv/Scripts/python -m pip install numpy pillow
```

CUDA 12.8 wheels are required for an RTX 50-series (compute capability 12.0). No CUDA Toolkit or
compiler is needed — gsplat's prebuilt Windows wheels stop at PyTorch 2.4 / CUDA 12.4 and cannot
run on Blackwell, which is why `splatvis.py` is a pure-PyTorch renderer instead.

## The three steps

1. **Trust the renderer.** `compare_unity.py` renders one view and diffs it against a Unity capture
   of the same camera. Measured on the threshold world: mean 2.2/255 and 2.0/255 at two different
   yaw/pitch views. Re-run this if the splat package or its shaders change.
   Note: an Editor camera capture at renderScale 0.8 shows the 0.8 buffer in the bottom-left of
   the image and black elsewhere; the script crops to it.
2. **Prune.** `prune.py <in.spz> <out_dir>` renders from every head position in the region, in every
   direction, at headset pixel density, and writes one `.spz` per threshold — kept records copied
   byte-for-byte, SPZ v2, which is all the importer reads.
3. **Validate on views the pruning never saw.** `validate.py <orig> <pruned> <dir>` renders random
   held-out views and reports difference statistics; `--bad-cut` removes a random 10% of the kept
   splats as a control. If the control does not fail loudly, the check is blind.

Then convert the chosen `.spz` in Unity at Medium (Norm11 / Norm11 / Norm8x4 / Norm6), and name it
`<base>-500k` so `AddressableWorldSetup` puts it in `SmallWorlds`.

## Measured on grand-conservatory-garden-path (25 Sep 2026)

Region: head within 0.3 m of the spawn, 1.2–1.9 m high. 1,302 views, 4.6 minutes on an RTX 5090.

| Kept when contribution ≥ | Splats | Held-out px > 8/255 |
|---|---|---|
| never drawn removed only | 404,305 | — |
| 1/255 (shipped as `…-cut-500k`) | 339,578 | 0.000% |
| 4/255 | 305,125 | 0.000% |
| bad-cut control | 305,442 | **13%** |

In Unity the 1/255 cut differs from the original by 2.1% of pixels > 8/255 — thin edge outlines, no
missing objects. A control that removes only never-drawn splats (a visual no-op) already produces
0.8%, from Norm11 re-quantisation when the importer re-chunks. The rest is not yet explained.

`out/` is scratch output and is not committed.

## Walking worlds: `--region` (Hall of the Great Buddha, 28 Sep 2026)

```
prune.py <in.spz> <out_dir> --region=-9,9,-9,18 --floor=-0.25 --spacing 1.414 --dirs cube --far 250 [--budgets 1500000,1000000,750000]
validate.py <orig> <pruned> <dir> --region=-9,9,-9,18 --floor=-0.25 --far 250 [--bad-cut]
compare_views.py <dir> label=file.spz ...      # fixed temple views for eyes
```

Frame: splatvis renders in Unity world metres — `.spz` x worldScale, no rotation or offset, which
is what WorldCycler does (`localScale = SplatScale`, object at the origin). Floor = groundY x
worldScale. Eyes on a 14 x 21 grid x 3 heights = 882 eyes, no point further than 0.97 m from one.
`--dirs cube` = 6 faces per eye, turned by a golden-angle yaw per eye (a fixed cube missed splats
the 62-view sphere found, from sort-order changes as the head turns).

| | Splats | Held-out px > 8/255 | Render |
|---|---|---|---|
| 500k keep1 | 453,051 of 500,000 | 0.000% (control 8.4%) | 34 min |
| full keep1 | 3,796,970 of 4,320,000 | 0.000% (control 5.0%) | 66 min |
| full top-1500k | 1,500,000 | 16.9% | |
| full top-1000k / 750k | | 41.9% / 62.3% — visible black holes | |
| 500k original vs full | | 42.1% (a different reconstruction, not a crop) | |

Walking the nave sees ~90% of the hall, so pruning is cleanup, not a budget. Top-N by MAX
contribution is a poor budget rule: it drops large background splats that fill walls and ceiling.

## Budgets by accumulated coverage: `--rank coverage` (Ornate Temple Hall, chisel, 28 Sep 2026)

```
prune.py <full.spz> <dir> --region=-4.4,4.4,-7.0,7.0 --floor 0 --dirs cube --far 250 --budgets 1000000,750000 --rank all --no-thresholds
compare_views.py <dir> --preset chisel label=file.spz ...
```

`--rank coverage` ranks by the SUM over every pixel of every view of alpha x transmittance
(`sum_contrib.npy`, pixel units), not the max. 264 eyes x 6 = 1,584 views, 17.4 min. Held-out, 40 views:

| vs full (4.32M) | mean | p99 | px > 8/255 | px > 16/255 |
|---|---|---|---|---|
| cov-1000k | 6.79 | 59.2 | 22.2% | 10.8% |
| cov-750k | 11.37 | 97.9 | 31.5% | 18.2% |
| Marble 500k export | 11.36 | 58.8 | 44.4% | 20.9% |
| max top-1000k / 750k | 17.6 / 31.5 | 117 / 163 | 52.1% / 68.8% | 32.6% / 51.2% |
| bad-cut control on cov-1000k / 750k | 8.92 / 13.90 | 71.7 / 109.7 | 29.0% / 37.9% | 15.0% / 22.7% |

Coverage is much better than max at the same N. `--rank hybrid` (keep max >= 16/255 first) is
the same set here: 3.08M splats clear 16/255, far more than either budget. Coverage's residual failure
is near-field detail on walls at the edge of the walk region (seen from few eyes, so little accumulated
coverage): dark green-black blotches on the carved gold panels, looking up from ~2 m.

## `--scale`: worlds not at worldScale 1.7 (Ornate Golden Temple, buddha-hall-chisel, 28 Sep 2026)

`prune.py`, `validate.py` and `compare_views.py` take `--scale` (default 1.7, so earlier runs
reproduce). This world sits at worldScale 2.63; floor y ~0 near the centre.

```
prune.py ../marble/fullres/buddha-hall-chisel-full.spz out/buddha-final --region=-8,8,-17,8 --floor 0 --scale 2.63 --dirs cube --far 250 --budgets 1000000,750000 --rank coverage --no-thresholds
validate.py <full> <file> out/buddha-validate --region=-8,8,-17,8 --floor 0 --scale 2.63 --far 250 --views 40 [--bad-cut]
compare_views.py out/buddha-compare --preset buddha-chisel --scale 2.63 label=file.spz ...
```

13 x 19 x 3 = 741 eyes, 4,446 views, 40.5 min. 3,864,191 ever drawn. Held-out, 40 views:

| vs full (4.32M) | mean | p99 | px > 8/255 | px > 16/255 |
|---|---|---|---|---|
| cov-1000k (89.1% of coverage kept) | 4.13 | 34.2 | 14.3% | 5.6% |
| cov-750k (83.9%) | 7.12 | 58.7 | 23.9% | 12.2% |
| Marble 500k export | 9.76 | 51.0 | 39.2% | 17.9% |
| bad-cut control on cov-1000k / 750k | 6.10 / 9.31 | 47.0 / 70.7 | 21.5% / 29.9% | 9.6% / 16.4% |
| splat-transform `--decimate-adaptive` 1000k (merging) | 7.12 | 42.3 | 27.5% | 12.6% |
| splat-transform `--decimate-adaptive` 750k (merging) | 8.95 | 49.4 | 34.0% | 17.5% |

The two splat-transform rows (same 40 views, verified identical eyes/yaw/pitch) come from
`out/buddha-st/*.ply`; renders sit in `out/buddha-compare/` as `st-adaptive-{1000k,750k}`.

### PLY input

`splatvis.load_spz` dispatches `.ply` to `splatvis.load_ply` (standard 3DGS binary little-endian,
SH degree 0, as `@playcanvas/splat-transform` writes), so every script here takes `.ply` wherever
it takes `.spz`. Raw positions share the `.spz` frame (no flip); `scale_*` are log sigma,
`opacity` a logit, `f_dc_*` the SH DC term, `rot_0..3` = w,x,y,z. Proof
(`out/buddha-st/loader-proof/`): the Marble 500k `.spz` converted with
`npx -y @playcanvas/splat-transform@3.7.0 in.spz out.ply` loads to identical positions (max diff 0)
and attributes within 1e-6, and view A renders to a mean 0.0001/255, max 2/255 difference
(0.02% of pixels differ); the same PLY with y flipped differs by 53.6/255, so the check can fail.

## Enclosed Garden Courtyard (garden-courtyard-chisel, 28 Sep 2026)

Full export is 1,920,000 splats (not 4.32M). worldScale 1.7, floor y ~0 at the centre. Walk region kept
~3 m clear of the side walls and planting-bed trees.

```
prune.py ../marble/fullres/garden-courtyard-chisel-full.spz out/garden-final --region=-7,7,-13,13 --floor 0 --scale 1.7 --dirs cube --far 250 --budgets 1000000 --rank coverage --no-thresholds
validate.py <full> <file> out/garden-validate --region=-7,7,-13,13 --floor 0 --scale 1.7 --far 250 --views 40 [--bad-cut]
compare_views.py out/garden-compare --preset garden-chisel --scale 1.7 label=file.spz ...
```

11 x 20 x 3 = 660 eyes, 3,960 views, 36.7 min (GPU shared with other apps). 1,642,629 ever drawn;
1,280,973 clear 16/255. Held-out, 40 views (the same 40 for every row):

| vs full (1.92M) | mean | p99 | px > 8/255 | px > 16/255 |
|---|---|---|---|---|
| cov-1000k (99.16% of coverage kept) | 0.23 | 3.26 | 0.23% | 0.05% |
| bad-cut control on cov-1000k | 1.75 | 19.4 | 4.64% | 1.48% |
| Marble 500k export | 6.69 | 41.8 | 25.0% | 10.9% |

## Chinese Imperial Temple Hall, and `--avoid` for free-standing columns (28 Sep 2026)

Full export 4,320,000 splats, worldScale 1.7, floor y ~0. 24 free-standing columns, 4 per row at
x ~ -9.1 / -3.6 / 3.6 / 9.0, rows at z ~ 7.3 / 1.8 / -3.5 / -9.2 / -15.1 / -21.1 (top-down density
of splats at y 0.5-3 m: `out/prompt-final/colmap.png`, region drawn on `colmap_region.png`). Side
walls x -15.2 / +14.7, altar front z ~10, far end z ~-23. Eyes near a free-standing column caused a
dark-red blotch down the columns on another hall, so `prune.py` and `validate.py` take
`--avoid=x,z,r;x,z,r` (use the `=` form: the value starts with `-`). No `--avoid` = unchanged.

```
prune.py ../marble/fullres/chinese-imperial-temple-hall-prompt-full.spz out/prompt-final --region=-13,12.5,-20,8 --floor 0 --scale 1.7 --dirs cube --far 250 --budgets 1000000 --rank coverage --no-thresholds "--avoid=$(cat out/prompt-final/avoid.txt)"
validate.py <full> <file> out/prompt-validate --region=-13,12.5,-20,8 --floor 0 --scale 1.7 --far 250 --views 40 "--avoid=..." [--bad-cut]
compare_views.py out/prompt-compare --preset prompt-temple --scale 1.7 label=file.spz ...
```

Avoid: r 1.5 m round each column, r 2.0 round the shrine at (0, 3.6), r 1.5 round a prop at
(11.4, 4.0). 1,260 grid eyes, 285 removed -> 975 eyes, 5,850 views, 91 min (GPU shared).
3,829,123 ever drawn; 3,043,291 clear 16/255. Held-out, 40 views from the same allowed region:

| vs full (4.32M) | mean | p99 | px > 8/255 | px > 16/255 |
|---|---|---|---|---|
| cov-1000k (93.3% of coverage kept) | 1.52 | 14.7 | 3.68% | 1.06% |
| bad-cut control on cov-1000k | 3.37 | 26.2 | 10.37% | 3.21% |
| Marble 500k export | 12.21 | 56.5 | 50.8% | 25.3% |
