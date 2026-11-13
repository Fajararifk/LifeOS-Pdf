# Requirement Fase 10: Render, Scan & OCR

## 1. Ringkasan & batas scope

Tiga package terpisah (package inti `LifeOS.Pdf` **tetap tanpa dependency**), membangun kemampuan
"membaca" PDF hasil scan dan merender halaman:

- `LifeOS.Pdf.Rendering` — render halaman PDF → PNG/JPEG (PDFium, BSD-3).
- `LifeOS.Pdf.Barcode.Scanning` — baca QR & barcode 1D/2D (ZXing.Net, Apache-2.0).
- `LifeOS.Pdf.Ocr` — OCR teks dari gambar/PDF scan + PDF searchable (Tesseract, Apache-2.0; `ind`+`eng`).

**Tidak masuk:** encoding barcode baru (sudah Fase 4); decoding JPEG/JPEG2000/JBIG2 dari nol (tetap lewat
engine pihak ketiga); layout recognition penuh (hanya ekstraksi teks + posisi).

**Catatan ukuran:** 3 package dengan **native dependency** (PDFium & Tesseract = library C++ lewat binding
.NET; ZXing.Net = managed). Diusulkan urutan: Rendering → Barcode.Scanning → Ocr (keduanya butuh Rendering
untuk render halaman). Bisa dipecah jadi 10a/10b/10c bila native setup menyulitkan (lihat §6).

## 2. Requirement fungsional

### `LifeOS.Pdf.Rendering` (PDFium)
- FR-01 `PdfRenderer.Render(byte[]|Stream|path, pageIndex, options)` → `RenderedPage` (PNG/JPEG, DPI/scale,
  ukuran piksel, metadata).
- FR-02 Render semua halaman (enumerable) + thumbnail (ukuran kecil).
- FR-03 DPI kustom (mis. 72/150/300) → ukuran piksel sesuai; opsi render page yang sudah ada (dibuka via
  `PdfDocument.Open`) atau langsung dari bytes.

### `LifeOS.Pdf.Barcode.Scanning` (ZXing.Net)
- FR-04 Baca QR/barcode dari **gambar** (byte[] PNG/JPEG) → `BarcodeScanResult` (teks, format, titik).
- FR-05 Baca dari **gambar di dalam PDF** (pakai ekstraksi Fase 7 `PdfPage.GetImages`) → hasil per halaman.
- FR-06 Baca dari **halaman vektor** (render halaman via Rendering, lalu scan) → hasil per halaman.
- FR-07 **Batch scan**: pisahkan dokumen berdasarkan halaman QR pemisah (mis. QR berisi `LIFEOS-SPLIT-...`).
- FR-08 Format yang didukung: QR (ISO/IEC 18004), Code 128, Code 39, EAN-13/8, UPC-A, ITF (minimal; sisanya
  sesuai ZXing.Net).

### `LifeOS.Pdf.Ocr` (Tesseract)
- FR-09 OCR teks dari **gambar** (byte[]) → teks + posisi (kata/kotak).
- FR-10 OCR dari **PDF hasil scan** (render tiap halaman → OCR) → teks per halaman.
- FR-11 **PDF searchable**: tulis PDF asli + lapisan teks tak terlihat (invisible text) hasil OCR → teks bisa
  di-copy/dicari, tampilan tak berubah.
- FR-12 Bahasa `ind` (Indonesia) + `eng` (Inggris); konfigurasi path tessdata.

### Pipeline gabungan
- FR-13 Satu panggilan per halaman: render → (opsional) barcode + OCR → hasil gabungan.

## 3. Requirement non-fungsional

- NFR-01 Package inti **tetap tanpa dependency**; semua modul di package terpisah.
- NFR-02 Lisensi dependency dicatat di `THIRD_PARTY_NOTICES.md`: PDFium (BSD-3), ZXing.Net (Apache-2.0),
  Tesseract (Apache-2.0), binding .NET masing-masing.
- NFR-03 Jalan di net8.0+ (native binding PDFium/Tesseract umumnya net6.0+); netstandard2.0 bila binding
  memungkinkan — dicek saat desain.
- NFR-04 Aman untuk input tidak terpercaya: batas ukuran gambar/DPI, file PDF rusak → exception jelas
  (bukan crash/hang).
- NFR-05 Hasil deterministik bila mungkin (render PDFium bisa berbeda antar versi — dicatat).
- NFR-06 Perlu di-document: cara setup native binary (PDFium) + tessdata di README.

## 4. Referensi spesifikasi

- ISO/IEC 18004 (QR Code) — untuk scan QR; ISO/IEC 15420/16390/15417 (barcode 1D).
- ISO 32000-1 §8.9 (gambar) untuk ekstraksi gambar (Fase 7) yang jadi input scan.
- Tesseract OCR (Apache-2.0); PDFium (BSD-3) sebagai render engine.

## 5. Edge cases

- Halaman kosong / tanpa gambar; gambar terenkripsi (Fase 9) → buka dulu dengan password.
- Gambar QR miring/skala kecil → ZXing.Net perlu `TryHarder`/multi-scale (render DPI lebih tinggi).
- Barcode multi-halaman / QR pemisah tidak ditemukan → fallback satu dokumen.
- Halaman /Rotate ≠ 0 saat render (PDFium menangani; posisi hasil scan harus konsisten).
- OCR: halaman gelap/kontras rendah; bahasa campuran ind+eng; halaman tanpa teks → kosong.
- PDF searchable: lapisan teks harus **tidak terlihat** (rendering mode 3 `Tr`) dan teks posisi cocok
  dengan hasil OCR (biar copy/search akurat).
- Native binary hilang / platform tak didukung → exception yang jelas saat inisialisasi.

## 6. Pertanyaan klarifikasi

1. **Pemecahan** — setujui urutan Rendering → Barcode.Scanning → Ocr (bisa dikerjakan bertahap 10a/10b/10c)?
2. **Binding PDFium** — pilih `PdfiumCore` (MIT, P/Invoke modern) atau `PdfiumViewer` (Apache-2.0, lama)?
   (Saran: PdfiumCore; native binary via NuGet `PdfiumViewer.Native.*` atau sejenis.)
3. **Tessdata** — bundel file bahasa `ind`+`eng` ke repo/package, atau minta user sediakan path?
   (Saran: dokumentasikan path, tidak dibundel — ukuran tessdata besar.)
4. **Target framework modul** — net8.0 saja (konsisten dengan Signing), atau net6.0+?
5. **Format barcode minimal** — cukup QR + Code128/39 + EAN/UPC + ITF (sesuai encoder Fase 4), atau semua
   yang didukung ZXing.Net?
6. **Pipeline gabungan** — cukup API per package + contoh komposisi, atau class `DocumentScanner` yang
   menggabungkan render+barcode+OCR dalam satu panggilan?
