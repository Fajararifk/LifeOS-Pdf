# LifeOS.Pdf — Playbook Fase 0–12

Cara pakai: kerjakan fase berurutan. Untuk SETIAP fase, jalankan 4 prompt
(A→B→C→D) di Claude Code. Jangan lanjut fase berikutnya sebelum QA PASS.

---

## TEMPLATE PROMPT (berlaku untuk semua fase)

**Prompt A — Analyst:**
```
Gunakan agent analyst. Baca CLAUDE.md dan docs/design-fase<N-1>.md.
Fase yang dikerjakan: <tempel SPESIFIKASI FASE di bawah>.
Buatkan requirement fungsional & non-fungsional, referensi ISO 32000,
edge cases, dan pertanyaan klarifikasi. JANGAN tulis kode.
Simpan ke docs/requirement-faseN.md.
```
→ jawab pertanyaan → ketik: `requirement disetujui`

**Prompt B — Architect:**
```
Gunakan agent architect. Baca CLAUDE.md dan docs/requirement-faseN.md.
Buatkan API publik, desain internal, struktur file, kompatibilitas target
framework, dan strategi test. Jangan tulis kode implementasi.
Simpan ke docs/design-faseN.md.
```
→ review → ketik: `design disetujui`

**Prompt C — Developer:**
```
Gunakan agent developer. Baca docs/design-faseN.md, implementasikan dengan baik.
```
→ review plan → ketik: `lanjut` → developer eksekusi

**Prompt D — QA:**
```
Gunakan agent qa. Review semua perubahan fase N terhadap
docs/requirement-faseN.md dan docs/design-faseN.md.
Simpan laporan ke docs/qa-faseN.md.
```
→ kalau ada blocker: `Gunakan agent developer. Perbaiki blocker di
docs/qa-faseN.md, plan dulu.` → ulangi QA sampai ✅ PASS → commit:
`git add -A && git commit -m "faseN: <nama fase>"` → tag rilis `v0.N.0`.

---

## Use case → fase

Daftar kebutuhan utama dan di fase mana masing-masing selesai.
"Dari nol" = ditulis sendiri di package inti. "Integrasi" = package terpisah
yang membungkus engine pihak ketiga (lisensi disebut), karena menulisnya dari
nol tidak realistis (browser engine, OCR).

| # | Use case | Fase | Cara |
|---|---|---|---|
| 1 | PDF dari HTML (WkHtmlToPdf) | 11 | Integrasi: `LifeOS.Pdf.Html`, backend wkhtmltopdf **dan** Chromium |
| 2 | PDF dari kode C# (layout, tabel, gambar, header/footer) | 0 ✅ + 1 ✅ + 2 ✅ | Dari nol |
| 3 | Membuat PDF + manipulasi dasar | 0 ✅ + 6 | Dari nol |
| 4 | Buat, ekstrak teks, merge, split, isi form, enkripsi | 6, 7, 8, 9 | Dari nol |
| 5 | Membuat & convert PDF (gambar↔PDF, PDF→teks/CSV, PDF/A) | 1 ✅ (gambar→PDF), 7, 10, 12 | Dari nol + integrasi render |
| 6 | Laporan PDF dari template | 3 ✅ | Dari nol |
| 7 | Gambar QR untuk ditempel ke PDF | 4 ✅ | Dari nol (vektor di PDF + PNG/SVG) |
| 8 | Baca QR/barcode dari gambar | 10 | Integrasi: ZXing.Net (Apache-2.0) |
| 9 | Ambil teks dari PDF hasil scan (OCR) | 10 | Integrasi: Tesseract (Apache-2.0) |
| 10 | Baca barcode/QR + OCR dari PDF | 10 | Render PDFium → ZXing + Tesseract |

Urutan sengaja mendahulukan **laporan** (fase 1–4): itu kebutuhan paling sering
(termasuk export PDF dari LifeOS), dan semuanya bisa dari nol. Fitur yang butuh
parser (merge, ekstrak, form) menyusul, lalu integrasi engine eksternal di akhir.

## Peta fitur vs library lain

| Kemampuan | PDFsharp | iText | ByteScout | LifeOS.Pdf |
|---|---|---|---|---|
| Buat PDF (teks, shape, gambar) | ✓ | ✓ | ✓ | Fase 0 ✅ |
| Font TrueType + Unicode, PNG | ✓ | ✓ | ✓ | Fase 1 ✅ |
| Layout engine (tabel, flow, header/footer) | MigraDoc | ✓ | ✓ | Fase 2 ✅ |
| Laporan dari template | – | pdfHTML | ✓ | Fase 3 ✅ |
| QR & barcode (buat) | – | ✓ | ✓ | Fase 4 ✅ |
| Link, bookmark, lampiran | ✓ | ✓ | ✓ | Fase 5 ✅ |
| Buka/merge/split/watermark/kompres | ✓ | ✓ | ✓ | Fase 6 |
| Ekstrak teks/gambar/tabel, redaksi | – | ✓ | ✓ | Fase 7 |
| Form (AcroForm) | sebagian | ✓ | ✓ | Fase 8 |
| Enkripsi & tanda tangan digital | sebagian | ✓ | ✓ | Fase 9 |
| Render, scan QR/barcode, OCR | – | add-on | ✓ | Fase 10 (integrasi) |
| HTML → PDF | – | pdfHTML | ✓ | Fase 11 (integrasi) |
| PDF/A, PDF/UA, layer, gradien | sebagian | ✓ | sebagian | Fase 12 |

---

## FASE 0 — Core Writer ✅ (v0.1)
SPESIFIKASI:
- Solution multi-target netstandard2.0 + net5–net10, test xUnit, sample, CI.
- Writer PDF 1.7: objek COS, xref klasik, trailer, /ID deterministik.
- Halaman (A3/A4/A5/Letter/Legal/custom, landscape), metadata dokumen.
- Grafis: garis (dash), kotak, kotak rounded, elips, warna RGB, stroke+fill.
- Teks: 14 font standar, WinAnsi, ukur lebar, rata kiri/tengah/kanan,
  text box dengan word-wrap.
- Gambar JPEG (gray/RGB/CMYK) tanpa re-encode, dipakai ulang lintas halaman.
- Kompresi Flate untuk content stream.

## FASE 1 — Font, Gambar & Image→PDF ✅ (v0.2)  · use case 5
Selesai. Kerning dipindah ke Fase 2, PNG encoder ke Fase 4 (lihat design-fase1.md).
SPESIFIKASI:
- Parser TrueType/OpenType (head, hhea, hmtx, cmap, glyf, loca, OS/2, name, post).
- Embedding Type0/CIDFontType2 + Identity-H + subsetting (hanya glyph terpakai).
- ToUnicode CMap supaya teks bisa di-copy & dicari. Kerning dasar (kern/GPOS pair).
  Shaping kompleks (Arab/Devanagari) = batasan tercatat.
- Metrik AFM lengkap untuk 14 font standar (ganti tabel perkiraan Fase 0).
- PNG decoder: semua color type & bit depth, alpha → SMask, palet, interlace.
- PNG encoder (zlib sudah ada) — dipakai Fase 4 untuk export QR ke gambar.
- Konversi gambar → PDF: `ImageToPdf.Convert(files, pageSize, fit)` (JPEG, PNG).
- Transformasi dasar di `PdfGraphics`: rotate, scale, opacity sederhana.

## FASE 2 — Layout Engine ✅ (v0.3)  · use case 2
Selesai, plus kerning & font fallback dari Fase 1. Belum: memotong satu baris tabel
lintas halaman, shaping (GSUB), hyphenation berbasis kamus → backlog.
SPESIFIKASI:
- Model dokumen fluent: Document → Section → Paragraph / Table / Image / List / Spacer.
- Paragraf multi-style (bold/italic/warna dalam satu paragraf), justify,
  page break otomatis, keep-together / keep-with-next.
- Tabel: lebar kolom auto/fixed/persen, border, padding, warna baris
  selang-seling, header berulang di setiap halaman, row span/col span.
- Header/footer per section, "Halaman X dari Y", margin, kolom ganda.
- Deteksi overflow (konten tidak muat) dengan error yang jelas.

## FASE 3 — Template Report ✅ (v0.4)  · use case 6
Selesai. Referensi bahasa template (English): docs/templates.md.
SPESIFIKASI:
- Template laporan berbasis model C# dan JSON: placeholder `{{nama}}`,
  perulangan baris tabel, kondisi, subtotal/total, grup.
- Format bawaan id-ID: Rp1.350.000, tanggal "13 Nov 2026", angka terbilang.
- Template siap pakai: invoice, kwitansi, slip gaji, laporan keuangan
  bulanan (cocok untuk export LifeOS), sertifikat.
- Template disimpan sebagai file (JSON/XML) supaya bisa diedit tanpa compile ulang.

## FASE 4 — QR & Barcode ✅ (v0.5)  · use case 7
Selesai (QR, Code 128/39, EAN-13/8, UPC-A, ITF/ITF-14, QRIS). DataMatrix → Fase 4b (backlog).
SPESIFIKASI:
- Encoder QR dari nol (ISO/IEC 18004: mode numeric/alnum/byte, versi 1–40,
  ECC L/M/Q/H, masking), termasuk QRIS-ready payload string.
- Barcode 1D: Code 128, Code 39, EAN-13/UPC-A, ITF; 2D lanjutan: DataMatrix.
- Output: vektor langsung di halaman PDF (tajam di semua zoom), PNG, SVG.
- QR dengan logo di tengah (ECC H), warna kustom, quiet zone.

## FASE 5 — Navigasi & Interaktif ✅ (v0.6)
Selesai (link, bookmark otomatis dari heading, daftar isi, named destination, page label,
viewer preferences, lampiran, anotasi note/highlight/stamp). Lihat design-fase5.md & qa-fase5.md.
SPESIFIKASI:
- Hyperlink URI, link internal ke halaman/posisi, named destination.
- Bookmark/outline bertingkat (otomatis dari heading layout engine), page label.
- Viewer preferences: open mode, page layout, zoom awal.
- Lampiran file (embedded files) dan anotasi (note, highlight, stamp).

## FASE 6 — Parser & Manipulasi (v0.7)  · use case 3, 4
SPESIFIKASI:
- Lexer + parser COS, xref tabel & xref stream, object stream, incremental
  update, rebuild xref rusak. Aturan input tidak terpercaya (CLAUDE.md #7).
- Buka PDF, baca info/halaman; merge, split (per halaman, per range, per N
  halaman), extract, rotate, reorder, hapus halaman.
- Watermark/stamp teks & gambar, nomor halaman/Bates di PDF yang sudah ada.
- Import halaman sebagai Form XObject (n-up, booklet, background/kop surat).
- Kompres/optimasi: hapus objek tak terpakai, kompres ulang stream, downscale gambar.

## FASE 7 — Ekstraksi Konten (v0.8)  · use case 4, 5
SPESIFIKASI:
- Interpreter content stream: teks + posisi + font, urutan baca, per area.
- Cari teks, ekstrak gambar (JPEG apa adanya, lainnya → PNG).
- Deteksi tabel → export CSV/JSON (mis. mutasi rekening, invoice).
- Convert PDF → teks / HTML sederhana. Redaksi sungguhan (konten dihapus).

## FASE 8 — Form (v0.9)  · use case 4
SPESIFIKASI:
- AcroForm: buat text field, checkbox, radio, combo/list, button, signature field.
- Isi form PDF yang ada, generate appearance stream, flatten.
- Ekspor/impor data form (FDF/XFDF/JSON).

## FASE 9 — Keamanan (v1.0)  · use case 4
SPESIFIKASI:
- Enkripsi standard security handler: RC4-128, AES-128, AES-256 (R6), permission.
- Buka PDF terenkripsi dengan password; hapus password (dengan password benar).
- Package `LifeOS.Pdf.Signing`: tanda tangan digital PAdES (CMS/PKCS#7),
  timestamp RFC 3161, verifikasi tanda tangan, tanda tangan visual.

## FASE 10 — Render, Scan & OCR (v1.1)  · use case 5, 8, 9, 10
Semua package terpisah, package inti tetap tanpa dependency.
SPESIFIKASI:
- `LifeOS.Pdf.Rendering` (PDFium, BSD-3): PDF → PNG/JPEG, thumbnail, DPI kustom.
- `LifeOS.Pdf.Barcode.Scanning` (ZXing.Net, Apache-2.0): baca QR/barcode dari
  file gambar, dari gambar di dalam PDF (pakai ekstraksi Fase 7), dan dari
  halaman vektor (render dulu). Split batch scan berdasarkan halaman QR pemisah.
- `LifeOS.Pdf.Ocr` (Tesseract, Apache-2.0; bahasa `ind` + `eng`): ambil teks
  dari PDF/gambar hasil scan, buat PDF searchable (lapisan teks tak terlihat).
- Pipeline gabungan: satu panggilan untuk barcode + OCR per halaman.

## FASE 11 — HTML → PDF (v1.2)  · use case 1
SPESIFIKASI:
- `LifeOS.Pdf.Html` dengan dua backend di balik satu interface:
  - **wkhtmltopdf** (LGPL-3.0, dipanggil sebagai proses terpisah; binary
    dipasang user, tidak dibundel). Catatan: proyek ini sudah diarsipkan sejak
    2023 dan memakai WebKit lama (CSS modern, flex/grid terbatas). Jangan pakai
    untuk HTML dari user tidak terpercaya: matikan akses file lokal & jaringan.
  - **Chromium headless** (PuppeteerSharp/Playwright, MIT/Apache-2.0) —
    backend yang direkomendasikan untuk HTML/CSS modern.
- Opsi: ukuran kertas, margin, header/footer HTML, orientasi, skala, timeout.
- Hasil HTML bisa digabung dengan dokumen LifeOS.Pdf (merge Fase 6, stamp).

## FASE 12 — Standar & Grafis Lanjut (v1.3)  · use case 5
SPESIFIKASI:
- Transparansi/blend mode, clipping, gradien (axial/radial), pattern, layer (OCG).
- XMP metadata, convert ke PDF/A-2b/3b (validasi veraPDF), tagged PDF / PDF/UA dasar.
- E-invoice: PDF/A-3 dengan XML terlampir (ZUGFeRD/Factur-X).

---

## Backlog fitur lain (belum dijadwalkan)
- Bandingkan dua PDF (teks & visual) dan tandai perbedaannya.
- Export tabel PDF ke Excel (.xlsx) — butuh writer OOXML (package terpisah).
- Ekstraksi data invoice/struk berbasis template zona.
- Kompres PDF hasil scan (JBIG2/CCITT G4 untuk dokumen hitam-putih).
- Mail merge: satu template × banyak data → banyak PDF / satu PDF gabungan.
- CLI `lifeos-pdf` (merge, split, compress, ocr, html2pdf) di atas semua package.
- Contoh integrasi ASP.NET Core (return PDF dari endpoint) dan Blazor.
