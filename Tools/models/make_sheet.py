"""Stitch render_turntable.py output into one labelled contact sheet.

python make_sheet.py <render_dir> <title> [reference_front.png]
"""
import sys, pathlib
from PIL import Image, ImageDraw, ImageFont

d = pathlib.Path(sys.argv[1]); title = sys.argv[2]
ref = pathlib.Path(sys.argv[3]) if len(sys.argv) > 3 else None
try:
    font = ImageFont.truetype("arial.ttf", 22); big = ImageFont.truetype("arial.ttf", 30)
except OSError:
    font = big = ImageFont.load_default()

def tile(p, w, h, label):
    im = Image.open(p).convert("RGB"); im.thumbnail((w, h))
    t = Image.new("RGB", (w, h + 30), (20, 20, 20)); t.paste(im, ((w - im.width) // 2, 30))
    ImageDraw.Draw(t).text((6, 3), label, fill=(255, 255, 120), font=font)
    return t

row1 = [tile(d / f"az{a:03d}.png", 350, 500, f"az {a}") for a in range(0, 360, 45)]
row2 = [tile(d / f"{n}.png", 400, 400, n) for n in ("face_front", "face_34", "face_side", "hand_L", "hand_R")]
if ref and ref.exists():
    row2.insert(0, tile(ref, 200, 400, "reference"))
stats = (d / "stats.txt").read_text().replace("\n", "   ") if (d / "stats.txt").exists() else ""
W = max(sum(t.width for t in row1), sum(t.width for t in row2))
sheet = Image.new("RGB", (W, 50 + row1[0].height + row2[0].height), (20, 20, 20))
dr = ImageDraw.Draw(sheet); dr.text((10, 8), f"{title}    {stats}", fill=(255, 255, 255), font=big)
x = 0
for t in row1: sheet.paste(t, (x, 50)); x += t.width
x = 0
for t in row2: sheet.paste(t, (x, 50 + row1[0].height)); x += t.width
sheet.save(d / "sheet.jpg", quality=88)
print(d / "sheet.jpg", sheet.size)
