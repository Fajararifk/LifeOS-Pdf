# QA Report: FASE 1 — Font, Gambar & Image→PDF

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase1.md & design-fase1.md

## Verdict: ⚠️ PASS DENGAN CATATAN

FR-01 s.d. FR-18 terpenuhi dan teruji. Semua file sample lolos lima validator
independen di enam runtime. Catatan di bawah tidak memblokir rilis v0.2.

## Temuan

### 🔴 Blocker
- Tidak ada.

### 🟡 Warning
- **Belum dicek manual di reader non-PDFium** (Adobe Acrobat / SumatraPDF / Firefox).
  PDFium (Chrome/Edge lama) sudah dicek visual; pdf.js dan Acrobat belum untuk Fase 1.
  Wajib dilakukan user sebelum tag rilis (lihat rencana test manual no. 1–3).
- **Desain berubah saat implementasi:** subset font sekarang menyertakan `cmap` kecil
  karena OTS menolak font tanpa `cmap` (dicatat di design-fase1.md §2.3). Perubahan
  ini menambah kompatibilitas, bukan mengurangi.
- `Fonts/Embedding/FontUsage.cs` — beberapa karakter yang tidak punya glyph semuanya
  dipetakan ke glyph 0; ToUnicode hanya mencatat karakter pertama, jadi copy-paste
  dari kotak `.notdef` menghasilkan karakter yang salah. Dampak kecil (teksnya memang
  tidak tampil), tapi lebih baik tidak menulis entri ToUnicode untuk glyph 0 → Fase 2.
- `Images/Png/PngImage.cs` — batas 200 juta piksel tetap bisa berarti alokasi
  ±1,8 GB untuk RGBA 16-bit. Aman dari bom dekompresi (ukuran dihitung dari IHDR),
  tapi bisa OOM di mesin kecil. Pertimbangkan decode per baris (streaming) di Fase 6.
- Target `net10.0` dikompilasi & dites di CI (SDK 10); lokal hanya dijalankan via
  runtime .NET 10.0.15 (roll-forward). Sama seperti Fase 0.

### 🔵 Saran
- Kerning & shaping (GSUB/GPOS) di Fase 2 sesuai rencana; aksara Arab/Devanagari
  saat ini tampil tidak tersambung — sudah didokumentasikan.
- `PdfFontFace` menyimpan seluruh file font di memori (MS Gothic 8,7 MB). Wajar untuk
  sekarang; bisa diubah ke lazy loading bila nanti dibutuhkan.

## Hasil verifikasi otomatis
- build Release (bersih): ✅ netstandard2.0, net5.0–net9.0 — 0 warning, 0 error
- test: ✅ 142/142 di net8.0 dan net9.0, termasuk:
  - 41 PNG (semua color type × bit depth × interlace, tRNS, 1×1) identik per piksel dengan pypng
  - 300 font rusak (fuzz berbiji tetap) → hanya `FormatException`/`NotSupportedException`
  - subset: GID tetap, komponen glyph komposit ikut, checksum `0xB1B0AFBA`, cmap BMP & astral
- sample: ✅ .NET 5.0.17, 6.0.36, 7.0.20, 8.0.34, 9.0.4, 10.0.15 → `showcase.pdf` (3 hal.) + `images.pdf` (2 hal.)
- validator: ✅ qpdf + pypdf + PDFium + fontTools + OTS untuk ke-12 file; teks Vietnam,
  Yunani, Rusia, Jepang, simbol (™ № ‰) ter-extract persis
- NFR: ✅ 100 karakter Noto Sans → PDF 9 KB (< 40 KB); load font 607 KB 1,7 ms (< 100 ms);
  subset + tulis 13 ms (< 200 ms); MS Gothic 8,7 MB / 22.291 glyph load 10,6 ms
- deterministik: ✅ test byte-identik; tag subset dari hash glyph
- pack: ✅ `LifeOS.Pdf.0.2.0.nupkg` (6 target)

## Rencana test manual
1. Buka `out/net8.0/showcase.pdf` di **Chrome** dan **Adobe Acrobat Reader / SumatraPDF**:
   halaman 3 menampilkan teks Vietnam, Yunani, Rusia (dan Jepang bila MS Gothic ada),
   PNG centang dengan tepi transparan di atas garis-garis, kipas teks berputar, watermark "CONTOH".
2. Pilih & copy baris "Tiếng Việt …" lalu tempel di Notepad → teks sama persis.
3. Ctrl+F "Привет" di viewer → ditemukan.
4. File → Properties → Fonts: `XXXXXX+NotoSans-Regular (Embedded Subset)`.
5. Buka `out/net8.0/images.pdf`: halaman 1 landscape (foto), halaman 2 portrait (lencana).
