import sys, glob
from PIL import Image
fs = sys.argv[2:]; ims = [Image.open(f).convert("RGB") for f in fs]
H = 800; ims = [i.resize((int(i.width * H / i.height), H)) for i in ims]
c = Image.new("RGB", (sum(i.width for i in ims), H), (255, 255, 255)); x = 0
for i in ims: c.paste(i, (x, 0)); x += i.width
c.save(sys.argv[1], quality=88); print(c.size)
