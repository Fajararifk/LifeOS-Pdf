"""
External validation for PDFs produced by LifeOS.Pdf.

Uses three independent implementations so our own writer cannot "agree with itself":
  * qpdf (via pikepdf)  - strict structural check of xref, objects and streams
  * pypdf               - opens the file, walks pages, extracts text
  * PDFium (pypdfium2)  - the engine inside Chrome's PDF viewer; renders every page

Embedded fonts (FontFile2) are additionally checked with:
  * fontTools           - decompiles every table and glyph, recompiles the font
  * OTS                 - the OpenType Sanitizer used by Chrome and Firefox

QR codes and barcodes (--expect-code) are read back with zxing-cpp from the PDFium render.

Usage:
    pip install pikepdf pypdf pypdfium2 fonttools opentype-sanitizer zxing-cpp
    python tools/validate_pdf.py file.pdf [more.pdf ...] [--expect-text "Halaman 1"] [--expect-code "INV-42"] [--render-dir out/png]

Exit code 0 = all files valid.
"""
import argparse
import io
import os
import subprocess
import sys
import tempfile

import ots
import pikepdf
import pypdf
import pypdfium2
from fontTools.ttLib import TTFont


def check_fonts(pdf: pikepdf.Pdf) -> tuple[int, list[str]]:
    """Validates every embedded TrueType program (FontFile2) in the document."""
    problems: list[str] = []
    seen = set()
    count = 0
    for obj in pdf.objects:
        if not isinstance(obj, pikepdf.Dictionary) or obj.get("/Type") != "/FontDescriptor" or "/FontFile2" not in obj:
            continue
        stream = obj["/FontFile2"]
        key = stream.objgen
        if key in seen:
            continue
        seen.add(key)
        count += 1
        name = str(obj.get("/FontName"))
        data = stream.read_bytes()
        if int(stream.get("/Length1", -1)) != len(data):
            problems.append(f"{name}: /Length1 {stream.get('/Length1')} != decoded length {len(data)}")
        try:
            font = TTFont(io.BytesIO(data))
            for tag in font.keys():
                font[tag]  # force decompile of every table
            glyf = font["glyf"]
            for glyph_name in font.getGlyphOrder():
                glyf[glyph_name].expand(glyf)
            font.save(io.BytesIO())  # recompile
        except Exception as ex:  # noqa: BLE001 - report any fontTools failure
            problems.append(f"{name}: fontTools rejected the font: {ex}")
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, "font.ttf")
            with open(src, "wb") as f:
                f.write(data)
            result = ots.sanitize(src, os.path.join(tmp, "out.ttf"), capture_output=True)
            if result.returncode != 0:
                problems.append(f"{name}: OTS rejected the font: {(result.stderr or result.stdout).decode(errors='replace').strip()}")
    return count, problems


def render(path: str, render_dir: str | None) -> list[str]:
    """Renders each page with PDFium; a page that renders fully white is a failure."""
    problems: list[str] = []
    doc = pypdfium2.PdfDocument(path)
    for i in range(len(doc)):
        image = doc[i].render(scale=1.0).to_pil().convert("L")
        if image.getextrema() == (255, 255):
            problems.append(f"PDFium rendered page {i + 1} completely blank")
        if render_dir:
            os.makedirs(render_dir, exist_ok=True)
            stem = os.path.splitext(os.path.basename(path))[0]
            parent = os.path.basename(os.path.dirname(os.path.abspath(path)))
            image.save(os.path.join(render_dir, f"{parent}-{stem}-p{i + 1}.png"))
    return problems


def decode_codes(path: str) -> set[str]:
    """Texts of all QR codes and barcodes zxing-cpp finds on the rendered pages."""
    import zxingcpp  # optional: only needed with --expect-code

    found = set()
    doc = pypdfium2.PdfDocument(path)
    for i in range(len(doc)):
        image = doc[i].render(scale=3).to_pil()
        for result in zxingcpp.read_barcodes(image):
            found.add(result.text)
    return found


def validate(path: str, expected_texts: list[str], render_dir: str | None = None, expected_codes: list[str] | None = None) -> list[str]:
    problems: list[str] = []

    with pikepdf.open(path) as pdf:
        for issue in pdf.check_pdf_syntax() if hasattr(pdf, "check_pdf_syntax") else pdf.check():
            problems.append(f"qpdf: {issue}")
        page_count = len(pdf.pages)
        font_count, font_problems = check_fonts(pdf)
        problems.extend(font_problems)

    reader = pypdf.PdfReader(path, strict=True)
    if len(reader.pages) != page_count:
        problems.append(f"page count mismatch: qpdf={page_count} pypdf={len(reader.pages)}")

    text = "\n".join(page.extract_text() or "" for page in reader.pages)
    for expected in expected_texts:
        if expected not in text:
            problems.append(f"expected text not found: {expected!r}")

    problems.extend(render(path, render_dir))

    if expected_codes:
        found = decode_codes(path)
        for code in expected_codes:
            # UPC-A is reported by zxing as EAN-13 with a leading zero.
            if code not in found and "0" + code not in found:
                problems.append(f"barcode/QR not readable or missing: {code!r} (decoded: {sorted(found)})")

    print(f"{path}: {page_count} page(s), {font_count} embedded font(s), "
          f"qpdf+pypdf+pdfium+fonttools+ots {'OK' if not problems else 'FAILED'}")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("files", nargs="+")
    parser.add_argument("--expect-text", action="append", default=[])
    parser.add_argument("--render-dir", default=None, help="save a PNG of every page here")
    parser.add_argument("--expect-code", action="append", default=[], help="text of a QR/barcode that must be readable")
    args = parser.parse_args()

    failed = False
    for f in args.files:
        for p in validate(f, args.expect_text, args.render_dir, args.expect_code):
            failed = True
            print("  - " + p)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
