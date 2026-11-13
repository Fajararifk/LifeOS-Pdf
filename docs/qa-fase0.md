# QA Report: FASE 0 — Core Writer

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase0.md & design-fase0.md

## Verdict: ⚠️ PASS DENGAN CATATAN

Semua requirement fungsional terpenuhi dan file lolos tiga engine PDF
independen di enam runtime. Catatan di bawah tidak memblokir rilis v0.1, tapi
wajib ditangani di Fase 1.

## Temuan

### 🔴 Blocker
- Tidak ada.

### 🟡 Warning
- `Fonts/StandardFontMetrics.cs` — lebar Times-Italic/BoldItalic memakai lebar
  tegak, Symbol/ZapfDingbats memakai lebar rata-rata, kode 128–255 memakai
  huruf dasar. Akibatnya perataan kanan/tengah & word-wrap bisa meleset beberapa
  point untuk font tersebut. Tampilan glyph tetap benar. → Fase 1: impor AFM lengkap.
- WinAnsi hanya mencakup Latin; aksara lain menjadi '?'. → Fase 1 (TrueType + ToUnicode).
- Target `net10.0` belum dikompilasi secara lokal (mesin dev memakai SDK 9).
  Library sudah dijalankan di runtime .NET 10.0.15 (roll-forward) dan lolos
  validasi; kompilasi net10.0 + unit test net10.0 berjalan di CI (SDK 10).
- Cek manual di Adobe Acrobat belum dilakukan. Sudah dicek: PDFium (engine
  Chrome), pdf.js (engine Firefox), qpdf, pypdf.

### 🔵 Saran
- Sample showcase sempat menumpuk paragraf dengan footer (ditemukan dari
  render PDFium, sudah diperbaiki). Layout engine Fase 2 sebaiknya punya
  deteksi overflow.
- Pertimbangkan `PdfGraphics.MeasureTextBox` (tinggi tanpa menggambar) untuk
  layout manual sebelum Fase 2.

## Rencana test manual
1. Buka `out/net8.0/showcase.pdf` di Chrome → 2 halaman, judul tab
   "LifeOS.Pdf — Showcase Fase 0", gambar dan 14 font tampil.
2. Buka file yang sama di Adobe Acrobat Reader / SumatraPDF → tidak ada pesan
   "file rusak", Document Properties menampilkan Title/Author/Producer.
3. Copy teks tabel di halaman 2 → nominal "Rp450.000" ter-copy utuh.
4. Zoom 400% → garis, elips, dan rounded rectangle tetap tajam (vektor).
5. Cetak ke printer/PDF printer → tidak ada halaman kosong.

## Hasil verifikasi otomatis
- build Release: ✅ netstandard2.0, net5.0, net6.0, net7.0, net8.0, net9.0 — 0 warning, 0 error
- test: ✅ 50/50 di net8.0 dan 50/50 di net9.0
- sample: ✅ dijalankan di .NET 5.0.17, 6.0.36, 7.0.20, 8.0.34, 9.0.4, 10.0.15
- validator (`tools/validate_pdf.py`): ✅ qpdf + pypdf + PDFium OK untuk keenam file,
  teks "Halaman 2 dari 2" dan "Rp1.924.500" ter-extract
- viewer: ✅ PDFium render (cek visual 2 halaman), ✅ pdf.js 3.11 render 2 halaman, judul & versi 1.7 terbaca
- pack: ✅ `LifeOS.Pdf.0.1.0.nupkg` berisi lib untuk 6 target + README
