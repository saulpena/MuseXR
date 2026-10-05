"""Render the same fixed views of several versions of a world, for a person to compare by eye.

    python compare_views.py <out_dir> <label>=<file.spz> [<label>=<file.spz> ...] [--far 250] [--preset P] [--scale 1.7]

Views are Unity world metres and Unity angles (yaw about +Y, positive pitch looks DOWN), eye
height taken above the floor, exactly as splatvis/prune.py use them. The defaults are the Hall of
the Great Buddha (empty-chinese-imperial-temple-hall: floor y -0.25 = groundY * worldScale,
spawn (0, 16) facing yaw 180; the 12 m Buddha prop stands at the origin and is a MESH, so it is
not in any of these renders). Writes <label>__<view>.png at headset density (1312 px over 90 deg)
and, per view, one sheet of the centre third of every version side by side at 1:1
(sheet__<view>.png), where sharpness differences show.
"""
import argparse
import os

import numpy as np
from PIL import Image, ImageDraw

import splatvis

FLOOR, EYE = -0.25, 1.6
VIEWS = {
    "A-spawn-facing-buddha": ((0.0, FLOOR + EYE, 16.0), 180.0, 0.0),
    "B-beside-buddha-to-east-wall": ((5.0, FLOOR + EYE, 0.0), 90.0, 0.0),
    "C-behind-buddha-to-entrance": ((0.0, FLOOR + EYE, -8.0), 0.0, 0.0),
}
# Ornate Temple Hall Interior (Marble Chisel): floor y ~0 (measured -0.05 near the centre),
# spawn at the origin, yaw 180 faces the carved dragon screen on its plinth, yaw 0 the entrance.
CHISEL = {
    "A-spawn-facing-screen": ((0.0, 1.6, 0.0), 180.0, 0.0),
    "B-east-wall-looking-along-room": ((3.8, 1.6, 5.0), 180.0, 0.0),
    "C-by-screen-back-to-entrance": ((0.0, 1.6, -3.2), 0.0, 0.0),
}
# Ornate Golden Temple Interior (buddha-hall-chisel, Marble Chisel) at worldScale 2.63: floor y ~0
# near the centre, entrance doors ~z +10 (yaw 0), gilded dragon relief back wall ~z -20 (yaw 180),
# side walls ~x +-10. The 12 m statue (a MESH, not in these renders) stands around z -8.
# Render with --scale 2.63.
BUDDHA_CHISEL = {
    "A-z6-facing-back-wall": ((0.0, 1.6, 6.0), 180.0, 0.0),
    "B-side-wall-x7-looking-along-room": ((7.0, 1.6, -2.0), 180.0, 0.0),
    "C-z-15-back-to-entrance": ((0.0, 1.6, -15.0), 0.0, 0.0),
}
# Enclosed Garden Courtyard (garden-courtyard-chisel, Marble Chisel) at worldScale 1.7: floor y ~0
# at the centre, open-air courtyard ~20 m wide x ~32 m deep, flagstone path along z between dark
# pools, ivy walls with flowering trees along x ~ +-10, a white baroque facade at each end
# (yaw 0 = +z faces one). Walk region x -7..7, z -13..13.
GARDEN_CHISEL = {
    "A-centre-facing-facade-yaw0": ((0.0, 1.6, 0.0), 0.0, 0.0),
    "B-x6-toward-side-wall-trees-yaw90": ((6.0, 1.6, 0.0), 90.0, 0.0),
    "C-z-12-down-the-path-yaw0": ((0.0, 1.6, -12.0), 0.0, 0.0),
}
# Chinese Imperial Temple Hall (chinese-imperial-temple-hall-prompt, Marble text prompt) at worldScale
# 1.7: floor y ~0, side walls x -15.2 / +14.7, altar front z ~10..13 (yaw 0 = +z), hall runs back to
# z ~-23. 24 free-standing columns, 4 per row at x ~-9.1/-3.6/3.6/9.0, rows at z ~7.3/1.8/-3.5/-9.2/
# -15.1/-21.1. Walk region x -13..12.5, z -20..8, no eye within 1.5 m of a column. Spawn (0,-3) yaw 0.
# B stands 1.54 m from the column at (-3.54,-3.54), looking down the hall past it (column row behind).
PROMPT_TEMPLE = {
    "A-spawn-0-m3-facing-altar-yaw0": ((0.0, 1.6, -3.0), 0.0, 0.0),
    "B-1p5m-from-column-m3p5-m3p5-looking-down-hall-yaw180": ((-3.0, 1.6, -2.1), 180.0, 0.0),
    "C-far-end-z-19p5-looking-back-yaw0": ((0.0, 1.6, -19.5), 0.0, 0.0),
}
# The Gate (GateWorld, grand-conservatory-with-lush-gardens-500k at worldScale 1.7, the object at the origin):
# the visitor's floor is y 0 (rig height in GateWorld), the walk runs from the gate at z 0 down the pool-side
# path to the Palace door at z -30.5, yaw 180 faces down it. Positive pitch looks DOWN.
GATE = {
    "A-gate-down-the-walk": ((0.0, 1.6, 0.0), 180.0, 0.0),
    "B-mid-walk-to-palace": ((0.0, 1.6, -14.0), 180.0, 0.0),
    "C-at-palace-looking-back": ((0.0, 1.6, -28.0), 0.0, 0.0),
    "D-left-flowers": ((-5.0, 1.6, -8.0), 270.0, 5.0),
    "E-right-flowers": ((5.0, 1.6, -8.0), 90.0, 5.0),
    "F-glass-roof": ((0.0, 1.6, -8.0), 180.0, -35.0),
    "G-path-at-feet": ((0.0, 1.6, -6.0), 180.0, 55.0),
    "H-region-edge": ((8.0, 1.6, -20.0), 315.0, 0.0),
}
PRESETS = {"temple": VIEWS, "chisel": CHISEL, "buddha-chisel": BUDDHA_CHISEL,
           "garden-chisel": GARDEN_CHISEL, "prompt-temple": PROMPT_TEMPLE, "gate": GATE}
FOV, RES = 90.0, 1312


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir")
    ap.add_argument("versions", nargs="+")
    ap.add_argument("--far", type=float, default=250.0)
    ap.add_argument("--scale", type=float, default=1.7, help="the world's worldScale in Unity")
    ap.add_argument("--preset", choices=sorted(PRESETS), default="temple")
    a = ap.parse_args()
    VIEWS = PRESETS[a.preset]
    os.makedirs(a.out_dir, exist_ok=True)
    fulls = {name: [] for name in VIEWS}
    crops = {name: [] for name in VIEWS}
    for v in a.versions:
        label, path = v.split("=", 1)
        g = splatvis.load_spz(path, world_scale=a.scale)
        r = splatvis.Renderer(g, far=a.far)
        for name, (eye, yaw, pitch) in VIEWS.items():
            img, _ = r.render(eye, yaw, pitch, FOV, RES, RES, image=True)
            im = (img.cpu().numpy() * 255 + 0.5).astype(np.uint8)
            Image.fromarray(im).save(os.path.join(a.out_dir, f"{label}__{name}.png"))
            c0, c1 = RES // 3, 2 * RES // 3
            crops[name].append((label, im[c0:c1, c0:c1].copy()))
            Image.fromarray(im[c0:c1, c0:c1]).save(os.path.join(a.out_dir, f"crop1to1__{label}__{name}.png"))
            fulls[name].append((label, np.asarray(Image.fromarray(im).resize((656, 656), Image.LANCZOS))))
        print(f"{label}: {g['n']:,} splats", flush=True)
        del r, g
    for prefix, table in (("sheet", crops), ("fullsheet", fulls)):
        write_sheets(a.out_dir, prefix, table)


def write_sheets(out_dir, prefix, table):
    for name, items in table.items():
        cols = min(4, len(items))
        rows = (len(items) + cols - 1) // cols
        h, w = items[0][1].shape[:2]
        sheet = Image.new("RGB", (cols * w, rows * (h + 24)), (0, 0, 0))
        d = ImageDraw.Draw(sheet)
        for k, (label, im) in enumerate(items):
            x, y = (k % cols) * w, (k // cols) * (h + 24)
            d.text((x + 4, y + 4), label, fill=(255, 255, 0))
            sheet.paste(Image.fromarray(im), (x, y + 24))
        sheet.save(os.path.join(out_dir, f"{prefix}__{name}.png"))


if __name__ == "__main__":
    main()
