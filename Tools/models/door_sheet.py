"""door_sheet.py <finish_out_dir> <title> [concept.png] -> <dir>/sheet.jpg, all renders labelled."""
import sys, pathlib
from PIL import Image, ImageDraw, ImageFont
d = pathlib.Path(sys.argv[1]); title = sys.argv[2]; ref = sys.argv[3] if len(sys.argv) > 3 else None
try: f = ImageFont.truetype("arial.ttf", 20); big = ImageFont.truetype("arial.ttf", 24)
except OSError: f = big = ImageFont.load_default()
names = ["front", "front34", "side", "back34", "back", "low", "high", "detail_top", "detail_bottom", "through_front", "through_eye", "through_back"]
tiles = []
if ref:
    names = ["concept"] + names
for n in names:
    p = pathlib.Path(ref) if n == "concept" else d / "renders" / f"{n}.png"
    im = Image.open(p).convert("RGB"); im.thumbnail((420, 420))
    t = Image.new("RGB", (420, 448), (25, 25, 25)); t.paste(im, ((420 - im.width) // 2, 28))
    ImageDraw.Draw(t).text((5, 3), n, fill=(255, 255, 90), font=f); tiles.append(t)
cols = 5; rows = (len(tiles) + cols - 1) // cols
meas = (d / "measure.txt").read_text().splitlines() if (d / "measure.txt").exists() else []
head = 34 + 22 * len(meas)
s = Image.new("RGB", (cols * 420, head + rows * 448), (25, 25, 25)); dr = ImageDraw.Draw(s)
dr.text((8, 5), title, fill=(255, 255, 255), font=big)
for i, l in enumerate(meas): dr.text((8, 34 + 22 * i), l[:190], fill=(200, 200, 200), font=f)
for i, t in enumerate(tiles): s.paste(t, ((i % cols) * 420, head + (i // cols) * 448))
s.save(d / "sheet.jpg", quality=86); print(d / "sheet.jpg", s.size)
