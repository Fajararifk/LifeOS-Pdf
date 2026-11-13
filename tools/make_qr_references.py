"""
Reference QR matrices from two independent encoders for the QR tests. Developer-only;
the output JSON is committed.

    pip install segno qrcode
    python tools/make_qr_references.py

Byte-mode references come from the `qrcode` package: segno always appends
8 - (length % 8) padding bits, i.e. a whole extra 0x00 codeword when the bit stream
already ends on a byte boundary (always the case in byte mode). ISO/IEC 18004 §7.4.10
only pads "if the bit stream … does not end at a codeword boundary", so segno's
byte-mode symbols differ (while still decoding to the same text). Numeric and
alphanumeric references come from segno.

Writes tests/LifeOS.Pdf.Tests/TestAssets/qr/references.json:
  fixed:  exact matrices for given text / error level / version / mask (rows as hex, MSB = left)
  auto:   the version and mask segno chooses on its own (no error-level boosting)
"""
import json
import os

import qrcode
import segno

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "tests", "LifeOS.Pdf.Tests", "TestAssets", "qr", "references.json")


def rows_hex_matrix(matrix):
    rows = []
    for row in matrix:
        bits = "".join("1" if v else "0" for v in row)
        bits += "0" * (-len(bits) % 4)
        rows.append("%0*X" % (len(bits) // 4, int(bits, 2)))
    return rows


def content_for(text, mode):
    # Byte mode gets UTF-8 bytes explicitly: segno would otherwise prefer ISO-8859-1.
    return text.encode("utf-8") if mode == "byte" else text


fixed = []


QRCODE_LEVELS = {"l": qrcode.constants.ERROR_CORRECT_L, "m": qrcode.constants.ERROR_CORRECT_M,
                 "q": qrcode.constants.ERROR_CORRECT_Q, "h": qrcode.constants.ERROR_CORRECT_H}


def qrcode_matrix(data, error, version=None, mask=None):
    q = qrcode.QRCode(version=version, error_correction=QRCODE_LEVELS[error], mask_pattern=mask, border=0)
    q.add_data(qrcode.util.QRData(data, mode=qrcode.util.MODE_8BIT_BYTE))
    q.make(fit=version is None)
    return q


def add(text, error, version, mask, mode):
    if mode == "byte":
        q = qrcode_matrix(text.encode("utf-8"), error, version, mask)
        rows = rows_hex_matrix(q.get_matrix())
    else:
        qr = segno.make(text, error=error, version=version, mask=mask, mode=mode, boost_error=False, micro=False)
        assert qr.version == version and qr.mask == mask
        rows = rows_hex_matrix(qr.matrix)
    fixed.append({"text": text, "error": error.upper(), "version": version, "mask": mask, "rows": rows})


for v in range(1, 41):
    add("v%d" % v, "m", v, v % 8, "byte")
for error in ("l", "q", "h"):
    for v in (1, 2, 3, 5, 7, 10, 14, 20, 27, 33, 40):
        add("%s%d" % (error, v), error, v, (v * 3) % 8, "byte")
for mask in range(8):
    add("0123456789012345678", "q", 2, mask, "numeric")
    add("HELLO WORLD $%*+-./:", "m", 2, mask, "alphanumeric")
add("Tiếng Việt 😀 Rp1.350.000", "l", 3, 1, "byte")
add("8", "h", 1, 6, "numeric")
add("12", "l", 1, 0, "numeric")
add("A", "l", 1, 4, "alphanumeric")

auto = []
samples = [
    "https://lifeos.app/inv/2026-0042",
    "HELLO WORLD",
    "0123456789" * 7,
    "Laporan keuangan November 2026 — Rp7.482.500",
    "00020101021126570011ID.DANA.WWW011893600915012345678902091234567890303UMI51440014ID.CO.QRIS.WWW0215ID10200000000000303UMI5204581253033605802ID5916WARUNG FAJAR ARIF6015JAKARTA SELATAN61051216062070703A0163041D8A",
    "x" * 300,
]
for text in samples:
    is_byte = any(ord(c) > 127 or c.islower() for c in text)
    for error in ("l", "m", "q", "h"):
        entry = {"text": text, "error": error.upper()}
        if is_byte:
            q = qrcode_matrix(text.encode("utf-8"), error)
            entry["version"] = q.version
            entry["qrcodeMask"] = q.mask_pattern if q.mask_pattern is not None else q.best_mask_pattern()
        else:
            s_qr = segno.make(text, error=error, boost_error=False, micro=False)
            entry["version"] = s_qr.version
            entry["segnoMask"] = s_qr.mask
        auto.append(entry)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump({"generator": "segno " + segno.__version__, "fixed": fixed, "auto": auto}, f, ensure_ascii=False, separators=(",", ":"))
print("wrote", os.path.relpath(OUT, ROOT), os.path.getsize(OUT), "bytes,", len(fixed), "fixed,", len(auto), "auto")
