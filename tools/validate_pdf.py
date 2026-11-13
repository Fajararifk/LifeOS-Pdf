"""
External validation for PDFs produced by LifeOS.Pdf.

Uses three independent implementations so our own writer cannot "agree with itself":
  * qpdf (via pikepdf)  - strict structural check of xref, objects and streams
  * pypdf               - opens the file, walks pages, extracts text
  * PDFium (pypdfium2)  - the engine inside Chrome's PDF viewer; renders every page

Usage:
    pip install pikepdf pypdf pypdfium2
    python tools/validate_pdf.py file.pdf [more.pdf ...] [--expect-text "Halaman 1"] [--render-dir out/png]

Exit code 0 = all files valid.
"""
import argparse
import os
import sys

import pikepdf
import pypdf
import pypdfium2


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


def validate(path: str, expected_texts: list[str], render_dir: str | None = None) -> list[str]:
    problems: list[str] = []

    with pikepdf.open(path) as pdf:
        for issue in pdf.check_pdf_syntax() if hasattr(pdf, "check_pdf_syntax") else pdf.check():
            problems.append(f"qpdf: {issue}")
        page_count = len(pdf.pages)

    reader = pypdf.PdfReader(path, strict=True)
    if len(reader.pages) != page_count:
        problems.append(f"page count mismatch: qpdf={page_count} pypdf={len(reader.pages)}")

    text = "\n".join(page.extract_text() or "" for page in reader.pages)
    for expected in expected_texts:
        if expected not in text:
            problems.append(f"expected text not found: {expected!r}")

    problems.extend(render(path, render_dir))

    print(f"{path}: {page_count} page(s), qpdf+pypdf+pdfium {'OK' if not problems else 'FAILED'}")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("files", nargs="+")
    parser.add_argument("--expect-text", action="append", default=[])
    parser.add_argument("--render-dir", default=None, help="save a PNG of every page here")
    args = parser.parse_args()

    failed = False
    for f in args.files:
        for p in validate(f, args.expect_text, args.render_dir):
            failed = True
            print("  - " + p)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
