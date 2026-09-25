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
