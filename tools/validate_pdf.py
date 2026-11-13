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


def check_navigation(reader: "pypdf.PdfReader", outlines: list[str], links: list[str], attachments: list[str]) -> list[str]:
    """Bookmarks resolve to pages, named destinations exist, expected links/attachments are present."""
    problems: list[str] = []
    try:
        flat = []
        def walk(items):
            for item in items:
                if isinstance(item, list):
                    walk(item)
                else:
                    flat.append(item)
        walk(reader.outline)
        for item in flat:
            if reader.get_destination_page_number(item) is None:
                problems.append(f"bookmark {item.title!r} does not resolve to a page")
        titles = [i.title for i in flat]
    except Exception as e:  # noqa: BLE001
        return [f"pypdf could not read the outline: {e}"]
    for t in outlines:
        if t not in titles:
            problems.append(f"bookmark not found: {t!r} (have {titles[:12]})")

    names = reader.named_destinations
    uris: list[str] = []
    for page in reader.pages:
        for ref in page.get("/Annots") or []:
            annot = ref.get_object()
            if annot.get("/Subtype") != "/Link":
                continue
            action = annot.get("/A")
            if action is not None and action.get("/S") == "/URI":
                uris.append(str(action["/URI"]))
            dest = annot.get("/Dest")
            if isinstance(dest, (str, bytes)) and str(dest) not in names:
                problems.append(f"link to undefined named destination {dest!r}")
    for link in links:
        if link not in uris:
            problems.append(f"link not found: {link!r} (have {sorted(set(uris))})")

    embedded = reader.attachments
    for name in attachments:
        if name not in embedded:
            problems.append(f"attachment not found: {name!r} (have {sorted(embedded)})")
    return problems


def validate(path: str, expected_texts: list[str], render_dir: str | None = None, expected_codes: list[str] | None = None,
             outlines: list[str] | None = None, links: list[str] | None = None, attachments: list[str] | None = None,
             forbidden: list[str] | None = None) -> list[str]:
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

    # Redaction check: neither pypdf nor PDFium may find the text, and it must not be in the file.
    if forbidden:
        import pypdfium2
        pdfium_text = ""
        doc = pypdfium2.PdfDocument(path)
        for i in range(len(doc)):
            pdfium_text += doc[i].get_textpage().get_text_range() + "\n"
        raw = open(path, "rb").read()
        for word in forbidden:
            if word in text or word in pdfium_text:
                problems.append(f"forbidden text is still extractable: {word!r}")
            if word.encode("latin-1", "ignore") in raw:
                problems.append(f"forbidden text is still in the file bytes: {word!r}")

    problems.extend(check_navigation(reader, outlines or [], links or [], attachments or []))
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
    parser.add_argument("--expect-outline", action="append", default=[], help="bookmark title that must exist")
    parser.add_argument("--expect-link", action="append", default=[], help="URI of a link annotation that must exist")
    parser.add_argument("--expect-attachment", action="append", default=[], help="name of an embedded file that must exist")
    parser.add_argument("--forbid-text", action="append", default=[], help="text that must NOT be extractable (redaction check)")
    args = parser.parse_args()

    failed = False
    for f in args.files:
        for p in validate(f, args.expect_text, args.render_dir, args.expect_code,
                          args.expect_outline, args.expect_link, args.expect_attachment, args.forbid_text):
            failed = True
            print("  - " + p)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
