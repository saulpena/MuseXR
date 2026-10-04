"""Lay out captures as one labelled contact sheet: python contact_sheet.py out.png cols img1 img2 ..."""
import sys, os
from PIL import Image, ImageDraw, ImageFont
out, cols, files = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
W = 300
tiles = []
for f in files:
    im = Image.open(f).convert("RGB")
    h = int(im.height * W / im.width)
    im = im.resize((W, h))
    tiles.append((os.path.basename(f), im))
H = max(t[1].height for t in tiles) + 34
rows = (len(tiles) + cols - 1) // cols
sheet = Image.new("RGB", (cols * W, rows * H), (20, 20, 20))
try: font = ImageFont.truetype("arial.ttf", 13)
except Exception: font = ImageFont.load_default()
d = ImageDraw.Draw(sheet)
for i, (name, im) in enumerate(tiles):
    x, y = (i % cols) * W, (i // cols) * H
    sheet.paste(im, (x, y + 34))
    d.text((x + 4, y + 2), name[:44], fill=(255, 230, 120), font=font)
    d.text((x + 4, y + 17), name[44:88], fill=(255, 230, 120), font=font)
sheet.save(out)
print(out, sheet.size)
