"""prop_sheet.py <finish_out_dir> <title> [concept.png] -> <dir>/sheet.jpg, every render in renders/ labelled."""
import sys, pathlib
from PIL import Image, ImageDraw, ImageFont
d = pathlib.Path(sys.argv[1]); title = sys.argv[2]; ref = sys.argv[3] if len(sys.argv) > 3 else None
try: f = ImageFont.truetype("arial.ttf", 18); big = ImageFont.truetype("arial.ttf", 22)
except OSError: f = big = ImageFont.load_default()
order = [f"az{a:03d}" for a in range(0, 360, 45)] + ["top", "low", "close_top", "close_mid", "close_base",
         "rake_L", "rake_R", "rake_top", "clay_rake_L", "clay_rake_R", "clay_rake_34", "clay_face"]
paths = ([("concept", pathlib.Path(ref))] if ref else []) + [(n, d / "renders" / f"{n}.png") for n in order if (d / "renders" / f"{n}.png").exists()]
T = 360; tiles = []
for n, p in paths:
    im = Image.open(p).convert("RGB"); im.thumbnail((T, T))
    t = Image.new("RGB", (T, T + 24), (25, 25, 25)); t.paste(im, ((T - im.width) // 2, 24))
    ImageDraw.Draw(t).text((5, 2), n, fill=(255, 255, 90), font=f); tiles.append(t)
cols = 6; rows = (len(tiles) + cols - 1) // cols
meas = (d / "measure.txt").read_text().splitlines() if (d / "measure.txt").exists() else []
head = 32 + 20 * len(meas)
s = Image.new("RGB", (cols * T, head + rows * (T + 24)), (25, 25, 25)); dr = ImageDraw.Draw(s)
dr.text((8, 4), title, fill=(255, 255, 255), font=big)
for i, l in enumerate(meas): dr.text((8, 32 + 20 * i), l[:200], fill=(200, 200, 200), font=f)
for i, t in enumerate(tiles): s.paste(t, ((i % cols) * T, head + (i // cols) * (T + 24)))
s.save(d / "sheet.jpg", quality=86); print(d / "sheet.jpg", s.size)
