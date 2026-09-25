"""Measure pixel-cost options before building them: work saved vs how much the image changes.

    python pixel_options.py <world.spz> <out_dir> [--views 16]

Options, each against the full-resolution uncapped render of the same view:
  rtX  - splats rendered at X times the eye resolution, upscaled bilinearly (the composite would
         have to sample instead of Load; not in the package yet)
  capN - each splat axis capped at N pixels (package cap is 4096, i.e. never)
"Fragments" counts blended fragments (alpha >= 1/255), which is what the blending bandwidth pays for.
"""
import argparse
import os

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image

import splatvis

W, H, FOV = 1344, 1408, 96.0      # Quest 3S eye texture at renderScale 0.8; approximate vertical FOV


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("spz")
    ap.add_argument("out_dir")
    ap.add_argument("--views", type=int, default=16)
    a = ap.parse_args()
    os.makedirs(a.out_dir, exist_ok=True)
    g = splatvis.load_spz(a.spz)
    r = splatvis.Renderer(g)
    rng = np.random.default_rng(2026)
    options = [("rt0.75", 0.75, None), ("rt0.6", 0.6, None), ("rt0.5", 0.5, None),
               ("cap128", 1.0, 128), ("cap64", 1.0, 64), ("cap32", 1.0, 32)]
    acc = {k: {"frag": 0, "mean": [], "p99": [], "o8": [], "o16": []} for k, _, _ in options}
    base_frag = 0
    for i in range(a.views):
        ang, rad = rng.uniform(0, 2 * np.pi), 0.3 * np.sqrt(rng.uniform())
        eye = (rad * np.cos(ang), rng.uniform(1.2, 1.9), rad * np.sin(ang))
        yaw, pitch = rng.uniform(0, 360), rng.uniform(-60, 40)
        base, st = r.render(eye, yaw, pitch, FOV, W, H, image=True)
        base_frag += st["fragments"]
        row = [base.cpu().numpy()]
        for key, scale, cap in options:
            w, h = round(W * scale), round(H * scale)
            img, st = r.render(eye, yaw, pitch, FOV, w, h, image=True, max_axis_px=cap)
            if scale != 1.0:
                img = F.interpolate(img.permute(2, 0, 1)[None], size=(H, W), mode="bilinear",
                                    align_corners=False)[0].permute(1, 2, 0)
            img = img.cpu().numpy()
            d = np.abs(img - row[0]).max(2) * 255
            acc[key]["frag"] += st["fragments"]
            acc[key]["mean"].append(d.mean()); acc[key]["p99"].append(np.percentile(d, 99))
            acc[key]["o8"].append((d > 8).mean() * 100); acc[key]["o16"].append((d > 16).mean() * 100)
            row.append(img)
        if i < 4:
            strip = np.concatenate([np.concatenate(row[:4], 1), np.concatenate([row[0]] + row[4:], 1)], 0)
            Image.fromarray((np.clip(strip, 0, 1) * 255 + .5).astype(np.uint8)).resize(
                (strip.shape[1] // 2, strip.shape[0] // 2)).save(os.path.join(a.out_dir, f"options_v{i}.png"))
    print(f"{a.views} views at {W}x{H}; baseline {base_frag / a.views / 1e6:.1f}M blended fragments per eye-view")
    print(f"{'option':8s} {'fragments':>10s} {'mean diff':>10s} {'p99':>6s} {'px>8':>7s} {'px>16':>7s}")
    for key, _, _ in options:
        v = acc[key]
        print(f"{key:8s} {100 * v['frag'] / base_frag:9.1f}% {np.mean(v['mean']):9.2f} {np.mean(v['p99']):6.1f} "
              f"{np.mean(v['o8']):6.2f}% {np.mean(v['o16']):6.2f}%")


if __name__ == "__main__":
    main()
