"""Visibility-prune a splat world for a region the visitor's head can occupy.

    python prune.py <in.spz> <out_dir> [--radius 0.3] [--heights 1.2,1.6,1.9] [--yaw-step 30]
    python prune.py <in.spz> <out_dir> --region X0,X1,Z0,Z1 --floor F [--spacing 1.414]
                    [--dirs cube] [--far 250] [--budgets 1000000,750000] [--avoid x,z,r;x,z,r]

Default (no --region): head positions within --radius of the origin, heights absolute. That is
the conservatory run of 25 Sep 2026 and it still reproduces exactly.

--region: a WALKING visitor. Eyes on a regular XZ grid over the box, in Unity world metres (the
frame splatvis renders in: the .spz scaled by worldScale, no rotation, no offset), heights taken
above --floor (the world's groundY * worldScale). --spacing is the largest grid step; the grid
is fitted so no point of the box is further than spacing/sqrt(2) horizontally from a sample.
--dirs cube renders the six 90-degree faces of a cube per eye instead of the 62-view sphere —
full coverage at no less than the same pixel density, ~10x fewer views.
--budgets additionally writes the top-N splats by max contribution (<base>-top<N/1000>k.spz).
--rank coverage ranks the budgets by ACCUMULATED coverage instead: the sum over every rendered
pixel of every view of alpha * transmittance (pixel units), saved as sum_contrib.npy, written as
<base>-cov-<N/1000>k.spz. --rank hybrid keeps every splat whose max contribution reaches
--hybrid-floor/255 first and fills the rest of N by coverage (<base>-hyb-<N/1000>k.spz).
--rank all writes all three. Max ranking drops large low-peak background splats (walls, ceiling)
that each add little to any one pixel but cover many; coverage keeps them.

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


def grid_positions(region, spacing, heights, floor):
    x0, x1, z0, z1 = region
    nx = max(2, int(np.ceil((x1 - x0) / spacing)) + 1)
    nz = max(2, int(np.ceil((z1 - z0) / spacing)) + 1)
    xs, zs = np.linspace(x0, x1, nx), np.linspace(z0, z1, nz)
    worst = 0.5 * np.hypot(xs[1] - xs[0], zs[1] - zs[0])
    return [(float(x), floor + h, float(z)) for h in heights for z in zs for x in xs], (nx, nz, worst)


def parse_avoid(spec):
    """'x,z,r;x,z,r' -> [(x, z, r), ...]; empty string -> []."""
    return [tuple(float(v) for v in c.split(",")) for c in spec.split(";") if c.strip()]


def avoided(x, z, avoid):
    return any((x - cx) ** 2 + (z - cz) ** 2 < r * r for cx, cz, r in avoid)


def cube_directions(yaw_offset=0.0):
    o = yaw_offset
    return [(o, 0), (o + 90, 0), (o + 180, 0), (o + 270, 0), (o, -90), (o, 90)]


def eye_directions(mode, yaw_step, i):
    """Per-eye view set. The cube turns by a golden-angle yaw per eye: the splat package sorts by
    view-space depth, so layered surfaces change which splat is in front as the head turns, and a
    fixed cube only ever shows six sort orders. Measured on the temple, 4 eyes: a fixed cube kept
    13,935 fewer splats at 1/255 than the 62-view sphere, 559 of them worth >= 16/255, mostly
    ceiling structure 6-14 m away seen 30 degrees up. Neighbouring eyes at different yaws recover
    that variety; validate.py on held-out random views is what says whether it is enough."""
    if mode == "cube":
        return cube_directions((i * 137.50776) % 90.0)
    return directions(yaw_step)


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
    ap.add_argument("--region", default=None, help="X0,X1,Z0,Z1 in Unity world metres: walk grid")
    ap.add_argument("--floor", type=float, default=0.0, help="floor y the heights stand on (with --region)")
    ap.add_argument("--spacing", type=float, default=1.414, help="max grid step in metres (with --region)")
    ap.add_argument("--dirs", choices=["sphere", "cube"], default="sphere")
    ap.add_argument("--budgets", default="", help="comma list of top-N splat counts to also write")
    ap.add_argument("--rank", choices=["max", "coverage", "hybrid", "all"], default="max",
                    help="how --budgets ranks splats (default max: the original behaviour)")
    ap.add_argument("--hybrid-floor", type=float, default=16.0,
                    help="with --rank hybrid: always keep max contribution >= this /255")
    ap.add_argument("--no-thresholds", action="store_true", help="skip the per-threshold files")
    ap.add_argument("--scale", type=float, default=1.7,
                    help="the world's worldScale in Unity (.spz metres x this = Unity metres)")
    ap.add_argument("--reuse", action="store_true", help="reuse out_dir/max_contrib.npy, render nothing")
    ap.add_argument("--avoid", default="", help="x,z,r;x,z,r;... (Unity metres): drop every --region eye "
                    "within r horizontally of (x,z) - free-standing columns and props (default: none)")
    a = ap.parse_args()
    os.makedirs(a.out_dir, exist_ok=True)

    g = splatvis.load_spz(a.src, world_scale=a.scale)
    heights = [float(h) for h in a.heights.split(",")]
    grid = None
    if a.region:
        region = [float(v) for v in a.region.split(",")]
        eyes, grid = grid_positions(region, a.spacing, heights, a.floor)
        avoid = parse_avoid(a.avoid)
        if avoid:
            before = len(eyes)
            eyes = [e for e in eyes if not avoided(e[0], e[2], avoid)]
            print(f"--avoid: {len(avoid)} circles removed {before - len(eyes)} of {before} eyes")
        print(f"walk grid {grid[0]} x {grid[1]} x {len(heights)} heights = {len(eyes)} eyes, "
              f"no point of the box further than {grid[2]:.3f} m (horizontal) from a sample")
    else:
        eyes = head_positions(a.radius, heights)
    dirs = eye_directions(a.dirs, a.yaw_step, 0)
    t0, done = time.time(), 0
    mc_path = os.path.join(a.out_dir, "max_contrib.npy")
    sc_path = os.path.join(a.out_dir, "sum_contrib.npy")
    want_sum = a.rank != "max"
    sc = None
    if a.reuse:
        mc = np.load(mc_path)
        sc = np.load(sc_path) if want_sum else None
        done = None
    else:
        r = splatvis.Renderer(g, far=a.far, track_sum=want_sum)
        for i, eye in enumerate(eyes):
            for yaw, pitch in eye_directions(a.dirs, a.yaw_step, i):
                r.render(eye, yaw, pitch, FOV, RES, RES)
                done += 1
            if not a.region or i % 20 == 19 or i == len(eyes) - 1:
                print(f"{done}/{len(eyes) * len(dirs)} views, {time.time() - t0:.0f}s, "
                      f"ever visible so far: {int((r.max_contrib > 0).sum()):,}", flush=True)
        mc = r.max_contrib.cpu().numpy()
        np.save(mc_path, mc)
        if want_sum:
            sc = r.sum_contrib.cpu().numpy()
            np.save(sc_path, sc)
    elapsed = time.time() - t0
    base = os.path.basename(a.src)[:-4]
    summary = {"src": a.src, "splats": int(g["n"]), "views": done, "eyes": eyes,
               "directions": dirs if a.dirs == "sphere" else "cube, yaw offset (i*137.50776) mod 90 per eye i", "fov": FOV, "res": RES, "far": a.far, "scale": a.scale,
               "region": a.region, "avoid": a.avoid, "floor": a.floor, "spacing": a.spacing,
               "grid": grid, "render_seconds": None if a.reuse else round(elapsed, 1),
               "never_drawn": int((mc == 0).sum()), "rank": a.rank, "thresholds": {}, "budgets": {}}
    budgets = [int(v) for v in a.budgets.split(",") if v]
    if want_sum:
        floor_keep = mc >= a.hybrid_floor / 255.0
        summary["hybrid_floor_255"] = a.hybrid_floor
        summary["above_hybrid_floor"] = int(floor_keep.sum())
        print(f"splats with max contribution >= {a.hybrid_floor:g}/255: {int(floor_keep.sum()):,}")
        cov_order = np.argsort(-sc, kind="stable")
        for n in [b for b in budgets if b < g["n"]]:
            modes = {"coverage": ["cov"], "hybrid": ["hyb"], "all": ["cov", "hyb"]}[a.rank]
            for mode in modes:
                keep = np.zeros(len(mc), bool)
                if mode == "cov":
                    keep[cov_order[:n]] = True
                else:
                    if floor_keep.sum() >= n:   # more above the floor than fit: best-covered of those
                        keep[cov_order[floor_keep[cov_order]][:n]] = True
                    else:
                        keep |= floor_keep
                        rest = cov_order[~floor_keep[cov_order]]
                        keep[rest[:n - int(floor_keep.sum())]] = True
                assert keep.sum() == n
                path = os.path.join(a.out_dir, f"{base}-{mode}-{n // 1000}k.spz")
                write_spz_subset(a.src, path, keep)
                summary["budgets"][f"{mode}-{n}"] = {
                    "kept": n, "file": path,
                    "min_kept_coverage_px": round(float(sc[keep].min()), 4),
                    "coverage_kept_pct": round(100 * float(sc[keep].sum() / sc.sum()), 3),
                    "kept_above_floor": int((keep & floor_keep).sum()),
                    "dropped_above_floor": int((~keep & floor_keep).sum())}
                print(f"{mode} budget {n:,}: {summary['budgets'][f'{mode}-{n}']}")
    for n in budgets if a.rank == "max" else []:
        if n >= g["n"]:
            continue
        order = np.argsort(-mc, kind="stable")
        keep = np.zeros(len(mc), bool)
        keep[order[:n]] = True
        path = os.path.join(a.out_dir, f"{base}-top{n // 1000}k.spz")
        write_spz_subset(a.src, path, keep)
        cut = float(mc[order[n - 1]] * 255)
        summary["budgets"][str(n)] = {"kept": n, "min_kept_contrib_255": round(cut, 4), "file": path}
        print(f"budget {n:,}: smallest kept max-contribution {cut:.3f}/255")
    for t in ([] if a.no_thresholds else THRESHOLDS):
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
