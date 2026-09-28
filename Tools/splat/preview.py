"""Preview a Marble .spz before it goes anywhere near Unity.

    python preview.py <world.spz> [<other.spz> ...] [--out sheet.png] [--eye x,z] [--scale 1.7]

Renders each file from the same eye, at the headset's pixel density, looking four ways
(0 / 90 / 180 / 270 degrees) plus up at the ceiling, and lays them out as one sheet: one row
per file, labelled with its name and splat count. Pass the 500k and the full export of the
same world to see what the smaller tier costs, side by side, from identical views.

The eye stands at the capture's centre (or --eye, in Unity metres after --scale) at 1.6 m
above the floor found under it. Coordinates, scale and yaw follow Unity, so a view here is the
view the headset gets at that spot with worldScale --scale.

What this cannot tell you is frame rate or memory. Only the headset measures those.
"""
import argparse
import os

import numpy as np
from PIL import Image, ImageDraw

import splatvis

VIEWS = [(0, 0), (90, 0), (180, 0), (270, 0), (0, -45)]   # (yaw, pitch); negative pitch looks up
W, H = 640, 480


def floor_under(g, x, z, radius=1.5):
    pos = g["pos"].cpu().numpy()
    near = (np.abs(pos[:, 0] - x) < radius) & (np.abs(pos[:, 2] - z) < radius)
    ys = pos[near, 1]
    return float(np.percentile(ys, 10)) if len(ys) > 100 else 0.0


def render_row(path, eye_xz, scale, far):
    g = splatvis.load_spz(path, world_scale=scale)
    x, z = eye_xz
    eye = (x, floor_under(g, x, z) + 1.6, z)
    r = splatvis.Renderer(g, far=far)
    tiles = []
    for yaw, pitch in VIEWS:
        img = r.render(eye, yaw, pitch, 90, W, H, image=True)
        if isinstance(img, tuple):
            img = img[0]
        if hasattr(img, "cpu"):
            img = img.cpu().numpy()
        img = np.asarray(img)
        if img.dtype != np.uint8:
            img = (np.clip(img, 0, 1) * 255).astype(np.uint8)
        tiles.append(Image.fromarray(img))
    return tiles, int(g["n"]), eye


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--out", default=None)
    ap.add_argument("--eye", default="0,0", help="x,z in Unity metres (after --scale)")
    ap.add_argument("--scale", type=float, default=1.7)
    ap.add_argument("--far", type=float, default=250.0)
    a = ap.parse_args()
    eye_xz = tuple(float(v) for v in a.eye.split(","))

    rows = []
    for f in a.files:
        tiles, n, eye = render_row(f, eye_xz, a.scale, a.far)
        rows.append((os.path.basename(f), n, tiles))
        print(f"{os.path.basename(f)}: {n:,} splats, eye {tuple(round(v, 2) for v in eye)}")

    label_h = 28
    sheet = Image.new("RGB", (W * len(VIEWS), (H + label_h) * len(rows)), "black")
    draw = ImageDraw.Draw(sheet)
    for i, (name, n, tiles) in enumerate(rows):
        y0 = i * (H + label_h)
        draw.text((8, y0 + 6), f"{name}  -  {n:,} splats", fill=(255, 230, 120))
        for j, t in enumerate(tiles):
            sheet.paste(t, (j * W, y0 + label_h))
            yaw, pitch = VIEWS[j]
            draw.text((j * W + 8, y0 + label_h + 6), f"yaw {yaw}" + (" up" if pitch < 0 else ""), fill="white")
    out = a.out or os.path.splitext(a.files[0])[0] + "-preview.png"
    sheet.save(out)
    print("sheet:", out)


if __name__ == "__main__":
    main()
