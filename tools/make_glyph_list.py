"""Writes the Adobe Glyph List (name -> Unicode) used for text extraction.

Source: fontTools' copy of the AGL (glyphlist.txt, BSD-3-Clause, Adobe). Output is a gzip of
"name hex[ hex…]" lines, embedded in LifeOS.Pdf. Usage:
    python tools/make_glyph_list.py src/LifeOS.Pdf/Extraction/Engine/Resources/glyphlist.txt.gz
"""
import gzip, sys
from fontTools import agl

lines = []
for name, uv in sorted(agl.LEGACY_AGL2UV.items()):
    values = uv if isinstance(uv, (list, tuple)) else (uv,)
    values = values[:1]  # legacy names with several values: the first is the canonical one
    lines.append(name + " " + " ".join("%04X" % v for v in values))
data = ("\n".join(lines) + "\n").encode("ascii")
with gzip.GzipFile(sys.argv[1], "wb", mtime=0) as f:
    f.write(data)
print(len(lines), "glyph names")
