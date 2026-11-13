# QA Report: FASE 2 — Layout Engine

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase2.md & design-fase2.md
**Mode:** auto-approve (keputusan diambil oleh tim sesuai "ambil yang terbaik")

## Verdict: ⚠️ PASS DENGAN CATATAN

FR-01 s.d. FR-21 terpenuhi dan teruji. Semua sample (showcase, images, report) lolos
lima validator di enam runtime. Catatan di bawah tidak memblokir rilis v0.3.

## Temuan

### 🔴 Blocker
- Tidak ada. (Satu blocker ditemukan dan sudah diperbaiki saat development — lihat di bawah.)

### Diperbaiki selama fase
- **Performa kuadratik di tabel panjang** (`Layout/Engine/TableLayouter.cs`): cache model
  tabel memakai nomor halaman sebagai kunci, sehingga tabel 1.500 baris di-layout ulang di
  setiap halaman (8,5 detik). Kunci sekarang lebar + jumlah halaman → 607 ms. Konsekuensi
  kecil: lebar field nomor halaman *di dalam sel tabel* diukur dari halaman awal tabel
  (nilainya tetap benar saat digambar).

### 🟡 Warning
- **CI GitHub tidak jalan**: akun GitHub terkunci karena masalah billing ("The job was not
  started because your account is locked due to a billing issue"). Build/test/validasi di
  CI — termasuk kompilasi `net10.0` — belum pernah berjalan. Perlu diselesaikan pemilik akun
  di GitHub → Settings → Billing.
- Belum dicek manual di reader non-PDFium (Acrobat/SumatraPDF/Firefox) — sama seperti Fase 1.
- Kerning tidak tersedia untuk 14 font standar (keputusan analyst #4).
- Baris tabel tidak pernah dipotong; isi sel yang lebih tinggi dari halaman →
  `LayoutException` (keputusan analyst #2, didokumentasikan).
- Tanpa shaping GSUB: ligature dan aksara Arab/India belum benar (backlog).

### 🔵 Saran
- API untuk memberi gaya pada isi sel yang dibuat `AddRow(params string[])` masih perlu
  cast `((Paragraph)row.Cells[i].Blocks[0]).Style = …`. Pertimbangkan
  `TableRow.AddRow(style, …)` atau `TableCell.Style` di Fase 3 (template).
- Bookmark/outline otomatis dari heading → Fase 5 (data level heading sudah disimpan).

## Hasil verifikasi otomatis
- build Release bersih: ✅ netstandard2.0, net5.0–net9.0 — 0 warning, 0 error
- test: ✅ 188/188 di net8.0 dan net9.0 (46 test baru: kerning, line breaking, paragraf,
  paginasi, kolom, section, header/footer, tabel, overflow, performa)
- kerning: ✅ nilai GPOS (AV −40, To −70, Ta −80) dan tabel `kern` sama dengan fontTools;
  GPOS rusak → kerning mati, font tetap jalan
- sample: ✅ .NET 5.0.17, 6.0.36, 7.0.20, 8.0.34, 9.0.4, 10.0.15 → showcase (3 hal.),
  images (2 hal.), report (6 hal.)
- validator: ✅ qpdf + pypdf + PDFium + fontTools + OTS untuk 18 file; "Halaman 6 dari 6",
  "Rp7.482.500", "Tiếng Việt" ter-extract
- visual (PDFium): ✅ justify, mixed styles, daftar bertingkat, fallback Vietnam/Yunani/Rusia,
  rowspan (rata tengah vertikal), colspan, header tabel berulang, zebra, gambar terpusat,
  section landscape dua kolom, keep-with-next judul, "Halaman X dari Y"
- NFR-04: ✅ 1.500 baris → 40 halaman, 86 KB, 607 ms (target < 1 s)
- pack: ✅ `LifeOS.Pdf.0.3.0.nupkg`

## Rencana test manual
1. Buka `out/net8.0/report.pdf` di Chrome dan Adobe Reader: 6 halaman, footer "Halaman X dari 6".
2. Halaman 2–4: header tabel "Tanggal / Kategori / Catatan / Nominal" ada di setiap halaman.
3. Copy paragraf "Ringkasan" (justify) → teks ter-copy dengan spasi normal, tanpa spasi ganda.
4. Halaman 5–6 (landscape): teks mengalir kolom kiri → kanan → halaman berikutnya.
