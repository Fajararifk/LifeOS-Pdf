"""Builds PDFs written by other software (qpdf via pikepdf) for the parser tests.

Run from the repository root after building the samples:
    python tools/make_parser_assets.py out/net8.0/navigation.pdf tests/LifeOS.Pdf.Tests/TestAssets/pdf
"""
import sys
import pikepdf

src, out = sys.argv[1], sys.argv[2]
with pikepdf.open(src) as pdf:
    # Object streams + cross-reference stream (qpdf uses a PNG predictor for it).
    pdf.save(f"{out}/qpdf-objstm.pdf", object_stream_mode=pikepdf.ObjectStreamMode.generate,
             compress_streams=True, deterministic_id=True)
    # Linearized ("fast web view"): hint stream and two cross-reference sections.
    pdf.save(f"{out}/qpdf-linearized.pdf", linearize=True, deterministic_id=True)
    # Uncompressed classic file with every stream decoded.
    pdf.save(f"{out}/qpdf-plain.pdf", object_stream_mode=pikepdf.ObjectStreamMode.disable,
             stream_decode_level=pikepdf.StreamDecodeLevel.generalized, compress_streams=False,
             deterministic_id=True)
print("ok")
