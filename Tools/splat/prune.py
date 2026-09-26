"""Visibility-prune a splat world for a region the visitor's head can occupy.

    python prune.py <in.spz> <out_dir> [--radius 0.3] [--heights 1.2,1.6,1.9] [--yaw-step 30]

Renders the world from every head position in the region, in every direction, at the headset's
pixel density, and records the most each splat ever adds to any pixel (alpha * transmittance).
Writes, for each threshold, a pruned .spz (the kept records copied byte-for-byte, so nothing is
re-quantised) and a JSON summary. The region is in Unity world metres, around the spawn at the
origin; the intro world has no locomotion, so its region is head movement only.

Nothing here decides what is safe. validate.py does that, on views this script never rendered.
"""
import argparse
import gzip
import json
import os
import struct
import time

import numpy as np
import torch

import splatvis

# Quest 3S: ~1832x1920 per eye over ~100 degrees, at renderScale 0.8 -> ~14.6 px/degree.
FOV = 90.0
RES = 1312          # 90 degrees * 14.6 px/degree
THRESHOLDS = [0.25, 0.5, 1.0, 2.0, 4.0]   # in 1/255 units of contribution


def head_positions(radius, heights):
    offs = [(0, 0), (radius, 0), (-radius, 0), (0, radius), (0, -radius),
            (radius * .7, radius * .7), (-radius * .7, -radius * .7)]
    return [(x, h, z) for h in heights for x, z in offs]


def directions(yaw_step):
    views = [(y, p) for p in (-60, -30, 0, 30, 60) for y in range(0, 360, yaw_step)]
    return views + [(0, -90), (0, 90)]


def write_spz_subset(src, dst, keep):
    raw = gzip.open(src).read()
    magic, ver, n, sh, fb, flags, res = struct.unpack("<IIIBBBB", raw[:16])
    keep = np.asarray(keep, bool)
    assert len(keep) == n
    o, parts = 16, []
    for stride in (9, 1, 3, 3, 3):          # pos, alpha, colour, scale, rotation
        block = np.frombuffer(raw, np.uint8, n * stride, o).reshape(n, stride)
        parts.append(block[keep].tobytes())
        o += n * stride
    assert o == len(raw)
    header = struct.pack("<IIIBBBB", magic, ver, int(keep.sum()), sh, fb, flags, res)
    with gzip.open(dst, "wb") as f:
        f.write(header + b"".join(parts))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("out_dir")
    ap.add_argument("--radius", type=float, default=0.3)
    ap.add_argument("--heights", default="1.2,1.6,1.9")
    ap.add_argument("--yaw-step", type=int, default=30)
    ap.add_argument("--far", type=float, default=400.0, help="the world's cameraFar in WorldCatalog")
    a = ap.parse_args()
    os.makedirs(a.out_dir, exist_ok=True)

    g = splatvis.load_spz(a.src)
    r = splatvis.Renderer(g, far=a.far)
    eyes = head_positions(a.radius, [float(h) for h in a.heights.split(",")])
    dirs = directions(a.yaw_step)
    t0, done = time.time(), 0
    for eye in eyes:
        for yaw, pitch in dirs:
            r.render(eye, yaw, pitch, FOV, RES, RES)
            done += 1
        print(f"{done}/{len(eyes) * len(dirs)} views, {time.time() - t0:.0f}s, "
              f"ever visible so far: {int((r.max_contrib > 0).sum()):,}", flush=True)

    mc = r.max_contrib.cpu().numpy()
    np.save(os.path.join(a.out_dir, "max_contrib.npy"), mc)
    base = os.path.basename(a.src)[:-4]
    summary = {"src": a.src, "splats": int(g["n"]), "views": done, "eyes": eyes,
               "directions": dirs, "fov": FOV, "res": RES,
               "never_drawn": int((mc == 0).sum()), "thresholds": {}}
    for t in THRESHOLDS:
        keep = mc >= t / 255.0
        path = os.path.join(a.out_dir, f"{base}-keep{t:g}.spz")
        write_spz_subset(a.src, path, keep)
        summary["thresholds"][f"{t:g}"] = {"kept": int(keep.sum()),
                                           "kept_pct": round(100 * keep.mean(), 2),
                                           "file": path}
        print(f"threshold {t:g}/255: keep {keep.sum():,} of {g['n']:,} ({100 * keep.mean():.1f}%)")
    with open(os.path.join(a.out_dir, "summary.json"), "w") as f:
        json.dump(summary, f, indent=1)


if __name__ == "__main__":
    main()
