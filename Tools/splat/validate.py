"""Check a pruned world against the original on views the pruning never rendered.

    python validate.py <orig.spz> <pruned.spz> <out_dir> [--views 40] [--radius 0.3]
                       [--heights 1.2,1.9] [--bad-cut]

Held-out views are random: head position anywhere in the region (not only the sampled points),
any yaw, pitch -80..80, a different random seed from nothing. Reports per view and overall:
mean difference, 99th percentile, and the share of pixels off by more than 8/255 and 16/255 —
and writes original | pruned | 8x-amplified difference strips so a person can look.

--bad-cut is the control: it deletes a random 10% of the splats the pruned file KEPT (i.e. ones
judged visible). If the numbers for that cut are not clearly worse, the check is blind and none
of its passes mean anything.
"""
import argparse
import os

import numpy as np
import torch
from PIL import Image

import splatvis

FOV, RES = 90.0, 1024


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("orig")
    ap.add_argument("pruned")
    ap.add_argument("out_dir")
    ap.add_argument("--views", type=int, default=40)
    ap.add_argument("--radius", type=float, default=0.3)
    ap.add_argument("--heights", default="1.2,1.9")
    ap.add_argument("--seed", type=int, default=12345)
    ap.add_argument("--bad-cut", action="store_true")
    a = ap.parse_args()
    os.makedirs(a.out_dir, exist_ok=True)

    go = splatvis.load_spz(a.orig)
    gp = splatvis.load_spz(a.pruned)
    label = os.path.basename(a.pruned)[:-4]
    if a.bad_cut:
        gen = torch.Generator(device="cuda").manual_seed(7)
        drop = torch.rand(gp["n"], device="cuda", generator=gen) < 0.10
        for k in ("pos", "scale", "quat", "opacity", "color"):
            gp[k] = gp[k][~drop]
        gp["n"] = int((~drop).sum())
        label += "-BADCUT"
    ro, rp = splatvis.Renderer(go), splatvis.Renderer(gp)

    rng = np.random.default_rng(a.seed)
    lo, hi = [float(h) for h in a.heights.split(",")]
    rows, worst = [], []
    for i in range(a.views):
        ang, rad = rng.uniform(0, 2 * np.pi), a.radius * np.sqrt(rng.uniform())
        eye = (rad * np.cos(ang), rng.uniform(lo, hi), rad * np.sin(ang))
        yaw, pitch = rng.uniform(0, 360), rng.uniform(-80, 80)
        io, _ = ro.render(eye, yaw, pitch, FOV, RES, RES, image=True)
        ip, _ = rp.render(eye, yaw, pitch, FOV, RES, RES, image=True)
        io, ip = io.cpu().numpy(), ip.cpu().numpy()
        d = np.abs(io - ip).max(2)
        row = {"view": i, "eye": [round(float(v), 2) for v in eye], "yaw": round(yaw), "pitch": round(pitch),
               "mean": float(d.mean() * 255), "p99": float(np.percentile(d, 99) * 255),
               "over8": float((d > 8 / 255).mean() * 100), "over16": float((d > 16 / 255).mean() * 100)}
        rows.append(row)
        strip = np.concatenate([io, ip, np.repeat(np.clip(d * 8, 0, 1)[..., None], 3, 2)], 1)
        Image.fromarray((strip * 255 + 0.5).astype(np.uint8)).save(
            os.path.join(a.out_dir, f"{label}_v{i:02d}.png"))
    m = lambda k: np.mean([r[k] for r in rows])
    mx = lambda k: np.max([r[k] for r in rows])
    print(f"{label}: {gp['n']:,} of {go['n']:,} splats, {a.views} held-out views")
    print(f"  mean diff   {m('mean'):.3f}/255 (worst view {mx('mean'):.3f})")
    print(f"  p99 diff    {m('p99'):.2f}/255 (worst view {mx('p99'):.2f})")
    print(f"  px >8/255   {m('over8'):.3f}% (worst view {mx('over8'):.3f}%)")
    print(f"  px >16/255  {m('over16'):.4f}% (worst view {mx('over16'):.4f}%)")
    worst = sorted(rows, key=lambda r: -r["over8"])[:3]
    print("  worst views:", [(r["view"], round(r["over8"], 3)) for r in worst])


if __name__ == "__main__":
    main()
