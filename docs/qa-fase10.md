# QA Report: FASE 10 — Render, Scan & OCR

**Tanggal:** 2026-11-14
**Review terhadap:** requirement-fase10.md & design-fase10.md
**Mode:** developer implementasi → QA read-only review.

> **Addendum (14 Nov):** `LifeOS.Pdf.Barcode.Scanning` di-upgrade dari `ZXing.Net` ke **`ZXingCpp`**
> (zxing-cpp, native). Alasannya: ZXing.Net gagal membaca QR padat pada dokumen nyata (faktur pajak
> e-Faktur) di 150–400 DPI + TryHarder, sedangkan zxing-cpp berhasil. Setelah upgrade, 2 QR faktur
> terbaca (`QrCode`), round-trip QR/Code128 tetap hijau, 535/535 test pass.

## Verdict: ✅ PASS (dengan catatan)

Empat package terpisah (Rendering, Barcode.Scanning, Ocr, Scanning) terimplementasi, build hijau, dan
lolos unit test. Rendering & OCR **teruji dengan native library asli** (PDFium win-x64 + Tesseract +
tessdata). Barcode.Scanning murni managed. Core `LifeOS.Pdf` tetap tanpa dependency.

## Temuan

### 🔴 Blocker (ditemukan & diperbaiki selama review)
- **Render PDFium semua hitam**: PDFium `FPDFBitmap_BGRA` merender background **transparan** (alpha 0),
  dan konversi BGRA→RGB mengabaikan alpha → seluruh gambar jadi hitam. Diperbaiki dengan **composite
  over white** memakai alpha. Dibuktikan: render 595×841 punya `min=0 max=255`; setelah fix, OCR
  mengenali "Hello World".
- **Namespace `LifeOS.Pdf.Barcode.Scanning` tabrakan** dengan class `Barcode` (`LifeOS.Pdf.Barcodes`):
  memperkenalkan namespace `LifeOS.Pdf.Barcode` (singular) yang mengaburkan class. Diperbaiki menjadi
  `LifeOS.Pdf.BarcodeScanning`.
- **`PDFiumCore` tidak membundel native** dan namanya bukan `PdfiumCore` (melainkan `PDFiumCore`, raw
  P/Invoke `FPDF_*`); binding memakai `FPDFBitmapCreateEx` (tanpa underscore) dan handle bertipe class
  dengan `__Instance`. Kode diadaptasi.

### 🟡 Warning
- **Native binary & tessdata tidak dibundel** (ukuran besar, platform-specific). `PDFium` dan
  `Tesseract` native serta `eng/ind.traineddata` harus disediakan/diunduh saat deploy; didokumentasikan
  di `THIRD_PARTY_NOTICES.md` + README.
- **Rendering/Ocr tidak mendukung netstandard2.0** (PDFiumCore = netstandard2.1; Tesseract net5.0
  bermasalah) → target `net5.0+` (Rendering) dan `net6.0+` (Ocr/Scanning). Barcode.Scanning tetap
  netstandard2.0+.
- **Tessdata_fast** dipakai untuk test (akurasi di bawah tessdata penuh); produksi disarankan pakai
  tessdata lengkap.
- Test OCR/rendering melewati assertion bila native lib/tessdata tidak ada (skip diam-diam) — perlu
  penanda `[Fact]`/skip eksplisit di CI agar tidak menyesatkan.

### 🔵 Saran
- Tambah `PdfRenderer.Render` output JPEG + `RenderedImage` raw RGBA (untuk pipeline tanpa re-encode).
- `BarcodeReader.ReadPdf` (langsung dari PDF, pakai `GetImages` + render) + `SplitOnSeparator` (batch)
  masih pending — lanjutkan.
- `OcrEngine.MakeSearchable` (PDF searchable dengan lapisan teks tak terlihat) masih pending.
- Pakai tessdata penuh untuk akurasi OCR produksi.

## Rencana test manual
1. Render `showcase.pdf` → PNG terlihat benar (bukan hitam), teks terbaca.
2. `qpdf`/viewer buka hasil render; barcode di halaman bisa di-scan ulang.
3. OCR dokumen scan nyata (bahasa `ind`+`eng`) → teks terbaca; `MakeSearchable` (setelah ada) teks bisa
   di-copy.

## Hasil verifikasi otomatis
- build Release: ✅ core netstandard2.0+net5–10, Signing net8.0, Rendering net5–10, Ocr/Scanning
  net6/8/9/10, Barcode.Scanning netstandard2.0+net5–10 — 0 warning / 0 error.
- test: ✅ **535/535** di net8.0/net9.0/net10.0 (6 test baru: render PNG, scan QR/Code128, OCR,
  DocumentScanner).
- render: ✅ A4 → 595×841 @72dpi, PNG valid; OCR `Hello World`/`Invoice` dikenali (native PDFium +
  Tesseract + tessdata_fast dimuat).
