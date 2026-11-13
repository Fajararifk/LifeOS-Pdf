"""
Builds the binary test assets for Fase 1. Developer-only; the outputs are committed.

    pip install fonttools pypng
    python tools/make_test_assets.py <folder with NotoSans-Regular.ttf, NotoSansMath-Regular.ttf, OFL.txt>

Fonts (SIL OFL 1.1, subset so the repo stays small):
  tests/.../TestAssets/fonts/NotoSans-subset.ttf      Latin, Vietnamese, Greek, Cyrillic
  tests/.../TestAssets/fonts/NotoSansMath-astral.ttf  ASCII + U+1D400–U+1D433 (cmap format 12)
  tests/.../TestAssets/fonts/test.ttc                 the two fonts above as a collection
  samples/.../Assets/NotoSans-sample.ttf               wider subset for the showcase

PNG suite (pypng writes every colour type / bit depth / interlace combination):
  tests/.../TestAssets/png/<name>.png  plus  <name>.ref = the expected decoded output:
  colour samples (gray: 1 channel, otherwise RGB; 16-bit kept big-endian, sub-byte gray
  scaled to 0-255) followed by one 8-bit alpha byte per pixel.
"""
import os
import shutil
import sys

import png
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.ttLib.ttCollection import TTCollection

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), ".."))
TEST_ASSETS = os.path.join(ROOT, "tests", "LifeOS.Pdf.Tests", "TestAssets")
SAMPLE_ASSETS = os.path.join(ROOT, "samples", "LifeOS.Pdf.Samples", "Assets")


def make_subset(source, target, unicodes, keep_hinting=True):
    options = subset.Options()
    options.layout_features = []          # no GSUB/GPOS: shaping is Fase 2
    options.notdef_outline = True
    options.glyph_names = False
    options.hinting = keep_hinting        # keep cvt/fpgm/prep so the subsetter copies them
    options.name_IDs = [0, 1, 2, 3, 4, 5, 6, 16, 17]
    font = TTFont(source, recalcTimestamp=False)  # deterministic output
    s = subset.Subsetter(options)
    s.populate(unicodes=unicodes)
    s.subset(font)
    font.save(target)
    print(f"{os.path.relpath(target, ROOT)}: {os.path.getsize(target)} bytes, {len(font.getGlyphOrder())} glyphs")


def fonts(src):
    out = os.path.join(TEST_ASSETS, "fonts")
    os.makedirs(out, exist_ok=True)
    latin = list(range(0x20, 0x7F)) + list(range(0xA0, 0x180)) + list(range(0x1EA0, 0x1EFA)) \
        + list(range(0x370, 0x400)) + list(range(0x400, 0x460)) \
        + [0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2026, 0x20AC, 0x20AB]
    make_subset(os.path.join(src, "NotoSans-Regular.ttf"), os.path.join(out, "NotoSans-subset.ttf"), latin)
    make_subset(os.path.join(src, "NotoSansMath-Regular.ttf"), os.path.join(out, "NotoSansMath-astral.ttf"),
                list(range(0x20, 0x7F)) + list(range(0x1D400, 0x1D434)), keep_hinting=False)

    ttc = TTCollection()
    ttc.fonts = [TTFont(os.path.join(out, "NotoSans-subset.ttf"), recalcTimestamp=False),
                 TTFont(os.path.join(out, "NotoSansMath-astral.ttf"), recalcTimestamp=False)]
    ttc.save(os.path.join(out, "test.ttc"))
    print("tests/.../fonts/test.ttc:", os.path.getsize(os.path.join(out, "test.ttc")), "bytes")
    shutil.copy(os.path.join(src, "OFL.txt"), os.path.join(out, "OFL.txt"))

    os.makedirs(SAMPLE_ASSETS, exist_ok=True)
    make_subset(os.path.join(src, "NotoSans-Regular.ttf"), os.path.join(SAMPLE_ASSETS, "NotoSans-sample.ttf"),
                latin + [0x2122, 0x2116, 0x2030])  # ™ № ‰ (Noto Sans has no arrows/math: see sample)
    shutil.copy(os.path.join(src, "OFL.txt"), os.path.join(SAMPLE_ASSETS, "OFL.txt"))


# ---------------------------------------------------------------- PNG suite

W, H = 13, 7  # odd sizes, smaller than one Adam7 block in height


def pixel_value(x, y, c, maxval):
    return (x * 37 + y * 91 + c * 53) % (maxval + 1)


def write_png(name, rows, width, height, **kw):
    path = os.path.join(TEST_ASSETS, "png", name + ".png")
    with open(path, "wb") as f:
        png.Writer(width, height, **kw).write(f, rows)
    return path


def reference(path):
    """Expected output of PngImage's decode path, computed from pypng's decoding."""
    r = png.Reader(filename=path)
    width, height, rows, info = r.read()
    rows = [list(row) for row in rows]
    bitdepth = info["bitdepth"]
    planes = info["planes"]
    palette = info.get("palette")
    trns = info.get("transparent")
    out_color, out_alpha = bytearray(), bytearray()
    maxval = (1 << bitdepth) - 1
    for row in rows:
        for x in range(width):
            px = row[x * planes:(x + 1) * planes]
            if palette:
                entry = palette[px[0]]
                out_color += bytes(entry[:3])
                out_alpha.append(entry[3] if len(entry) == 4 else 255)
                continue
            color = px[:1] if planes in (1, 2) else px[:3]
            for v in color:
                if bitdepth == 16:
                    out_color += bytes([v >> 8, v & 0xFF])
                elif bitdepth == 8:
                    out_color.append(v)
                else:
                    out_color.append(v * 255 // maxval)
            if planes in (2, 4):
                a = px[-1]
                out_alpha.append(a >> 8 if bitdepth == 16 else a)
            elif trns is not None:
                key = trns if isinstance(trns, tuple) else (trns,)
                out_alpha.append(0 if tuple(color) == tuple(key) else 255)
            else:
                out_alpha.append(255)
    with open(path[:-4] + ".ref", "wb") as f:
        f.write(bytes(out_color) + bytes(out_alpha))


def png_suite():
    out = os.path.join(TEST_ASSETS, "png")
    os.makedirs(out, exist_ok=True)
    made = []
    for interlace in (False, True):
        suffix = "-i" if interlace else ""
        for depth in (1, 2, 4, 8, 16):
            mv = (1 << depth) - 1
            rows = [[pixel_value(x, y, 0, mv) for x in range(W)] for y in range(H)]
            made.append(write_png(f"gray{depth}{suffix}", rows, W, H, greyscale=True, bitdepth=depth, interlace=interlace))
        for depth in (8, 16):
            mv = (1 << depth) - 1
            rows = [[pixel_value(x, y, c, mv) for x in range(W) for c in range(3)] for y in range(H)]
            made.append(write_png(f"rgb{depth}{suffix}", rows, W, H, greyscale=False, bitdepth=depth, interlace=interlace))
            rows = [[pixel_value(x, y, c, mv) for x in range(W) for c in range(2)] for y in range(H)]
            made.append(write_png(f"graya{depth}{suffix}", rows, W, H, greyscale=True, alpha=True, bitdepth=depth, interlace=interlace))
            rows = [[pixel_value(x, y, c, mv) for x in range(W) for c in range(4)] for y in range(H)]
            made.append(write_png(f"rgba{depth}{suffix}", rows, W, H, greyscale=False, alpha=True, bitdepth=depth, interlace=interlace))
        for depth in (1, 2, 4, 8):
            n = 1 << depth
            palette = [((i * 67) % 256, (i * 131) % 256, (i * 29) % 256) for i in range(n)]
            rows = [[pixel_value(x, y, 0, n - 1) for x in range(W)] for y in range(H)]
            made.append(write_png(f"pal{depth}{suffix}", rows, W, H, palette=palette, bitdepth=depth, interlace=interlace))
            palette_a = [p + ((i * 97) % 256,) for i, p in enumerate(palette)]
            made.append(write_png(f"pala{depth}{suffix}", rows, W, H, palette=palette_a, bitdepth=depth, interlace=interlace))
    # Colour-key transparency (tRNS for gray / RGB).
    rows = [[pixel_value(x, y, 0, 255) for x in range(W)] for y in range(H)]
    made.append(write_png("gray8-trns", rows, W, H, greyscale=True, bitdepth=8, transparent=pixel_value(3, 2, 0, 255)))
    rows = [[pixel_value(x, y, c, 255) for x in range(W) for c in range(3)] for y in range(H)]
    made.append(write_png("rgb8-trns", rows, W, H, greyscale=False, bitdepth=8,
                          transparent=tuple(pixel_value(3, 2, c, 255) for c in range(3))))
    # 1×1 edge case.
    made.append(write_png("one-pixel", [[200, 100, 50, 128]], 1, 1, greyscale=False, alpha=True, bitdepth=8))
    for path in made:
        reference(path)
    print(f"PNG suite: {len(made)} images in {os.path.relpath(out, ROOT)}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    fonts(sys.argv[1])
    png_suite()
