"""Render one view with splatvis and compare it against a Unity capture of the same camera.

    python compare_unity.py <world.spz> <unity.png> <yaw> [pitch] [eye_y] [fov_v]

Writes <unity>_py.png beside the capture and a side-by-side <unity>_cmp.png, and prints the
mean and 99th-percentile per-channel difference. A renderer that disagrees with Unity cannot be
trusted to say what Unity would draw, so this runs before any pruning decision.
"""
import sys
import time

import numpy as np
import torch
from PIL import Image

import splatvis

spz, png = sys.argv[1], sys.argv[2]
yaw = float(sys.argv[3])
pitch = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
eye_y = float(sys.argv[5]) if len(sys.argv) > 5 else 1.6
fov = float(sys.argv[6]) if len(sys.argv) > 6 else 90.0

ref = np.asarray(Image.open(png).convert("RGB")).astype(np.float32) / 255.0
# The Editor's camera capture shows URP's renderScale buffer (0.8) in the bottom-left corner of
# the image without scaling it up; everything else is black. Compare against that region only.
RENDER_SCALE = 0.8
H, W = ref.shape[:2]
h, w = round(H * RENDER_SCALE), round(W * RENDER_SCALE)
ref = ref[H - h:, :w]
g = splatvis.load_spz(spz)
r = splatvis.Renderer(g)
t = time.time()
img, stats = r.render((0.0, eye_y, 0.0), yaw, pitch, fov, w, h, image=True)
torch.cuda.synchronize()
print(f"rendered {w}x{h} in {time.time() - t:.2f}s  {stats}")
out = img.cpu().numpy()
diff = np.abs(out - ref)
print(f"mean |diff| {diff.mean() * 255:.2f}/255   p99 {np.percentile(diff, 99) * 255:.1f}/255   "
      f"pixels off by >16/255: {(diff.max(2) > 16 / 255).mean() * 100:.2f}%")
base = png[:-4]
Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(base + "_py.png")
Image.fromarray(np.concatenate([(ref * 255).astype(np.uint8), (out * 255 + 0.5).astype(np.uint8)], 1)).save(base + "_cmp.png")
