"""The museum's pictures, made Android-compressible without blurring them.

On Android, Unity will not compress a non-power-of-two texture that has mipmaps, so each painting shipped as
RGBA32 (17.5 MB apiece, 226 MB for the set). Letting Unity stretch them to a power of two fixes the size but
blurs them: its resample kept 42-66% of the close-up detail, against 95-98% for a Lanczos upscale (measured
5 Oct 2026, same render, Laplacian variance against the source). So the upscale is done here.

The originals are the masters and live in Tools/artworks/source/ (outside Assets, so they never ship); this
writes the upscaled copies over Assets/Museum/Artworks/ with the same names, so every reference holds. Their
true proportions are read from the masters by PictureAspectTable.

    python make_pot.py
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "source")
DST = os.path.join(HERE, "..", "..", "Assets", "Museum", "Artworks")
MAX = 2048  # the import's size cap


def pot(n):
    return 1 << (n - 1).bit_length()


for name in sorted(os.listdir(SRC)):
    ext = name.rsplit(".", 1)[-1].lower()
    if ext not in ("jpg", "png"):
        continue
    im = Image.open(os.path.join(SRC, name))
    im = im.convert("RGBA" if ext == "png" and im.mode in ("RGBA", "LA", "P") else "RGB")
    w, h = im.size
    s = min(1.0, MAX / max(w, h))
    w2, h2 = round(w * s), round(h * s)
    out = im.resize((min(MAX, pot(w2)), min(MAX, pot(h2))), Image.LANCZOS)
    path = os.path.join(DST, name)
    if ext == "jpg":
        out.save(path, quality=95, subsampling=0)
    else:
        out.save(path, optimize=True)
    print(f"{name}: {w}x{h} -> {out.size[0]}x{out.size[1]}")
