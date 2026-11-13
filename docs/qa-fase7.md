# QA Report: FASE 7 — Ekstraksi Konten

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase7.md & design-fase7.md (termasuk "Revisi saat implementasi")
**Mode:** auto-approve

## Verdict: ⚠️ PASS DENGAN CATATAN

FR-01 s.d. FR-08 terpenuhi. Teks (posisi, font, urutan baca, mode layout, area), pencarian, gambar,
tabel (lattice & stream) → CSV/JSON, PDF → teks/HTML, dan redaksi sungguhan bekerja pada dokumen
kita, file qpdf, dan PDF tulisan tangan dengan font custom (Differences, CID + ToUnicode).

## Temuan

### 🔴 Blocker (ditemukan & diperbaiki selama fase)
- **Redaksi bocor lewat objek yatim**: XObject asli (gambar/form berisi teks rahasia) tetap tersalin
  ke file walau tidak dirujuk lagi. Sekarang XObject yang diganti/dihapus tidak disalin sama sekali.
  Dibuktikan oleh test (`/Subtype /Image` dan teks rahasia tidak ada di byte file).
- **QR vektor masih terbaca setelah redaksi**: redaksi awalnya hanya menutup bentuk vektor.
  Sekarang subpath yang menyentuh area dihapus; zxing tidak lagi menemukan kode.
- **Nama pelanggan tersisa di /Subject**: `RedactText` kini juga membersihkan metadata dan bookmark.

### Ditemukan selama fase
- Deduplikasi teks tebal palsu terlalu longgar ("fallback" → "fal back"); dibatasi 0.06 em.
- List berpoin terbaca sebagai tabel 2 kolom; tabel yang kolom pertamanya hanya penanda list ditolak.
- Escape string (backslash) di skrip edit sempat rusak; diperbaiki, diverifikasi build + test.

### 🟡 Warning
- **CI GitHub masih tidak jalan** (billing). Langkah `--forbid-text` untuk redaksi sudah ditambahkan.
- **Belum dicek manual** di Chrome & reader kedua (`invoice-redacted.pdf`, `extracted/report.html`).
- Redaksi menghapus seluruh garis/bentuk yang menyentuh area (termasuk bagian di luar area) dan
  gambar utuh (tidak per piksel) — redaksi berlebih, bukan bocor. Path kliping tidak diubah.
- Teks tanpa ToUnicode dan dengan encoding/glyph tanpa nama standar keluar sebagai kode atau U+FFFD
  (PDF hasil scan butuh OCR — Fase 10).
- Deteksi kolom (`DetectColumns`) heuristik XY-cut, default mati supaya tabel tidak terbaca per kolom.
- Performa: ±4 ms/halaman untuk teks + tabel (laporan ±45 halaman: 184 ms); belum dioptimalkan sedalam
  penulisan (alokasi ±3 MB/halaman).

### 🔵 Saran
- Ekstraksi zona berbasis template (invoice/struk) di atas `GetWords` — sudah tercatat di Fase 12.
- Membalik glyph TrueType tanpa ToUnicode lewat cmap font tertanam.

## Hasil verifikasi otomatis
- build Release: ✅ netstandard2.0, net5.0–net10.0 — 0 warning
- test: ✅ 512/512 di net8.0, net9.0, net10.0 (17 test baru `ExtractionTests` + fuzz diperluas ke
  ekstraksi teks/tabel/gambar), termasuk posisi kata, Differences + AGL (ﬁ, é), Type0 Identity-H dengan
  bfrange array/emoji/ligatur dan /W, spasi dari TJ/Tz/Tc, halaman /Rotate, area & teks tak terlihat,
  form bersarang + siklus, gambar Indexed + SMask → RGBA, inline 1-bit /Decode, JPEG identik byte,
  tabel lattice (CSV RFC 4180, JSON) & stream (baris lanjutan), HTML, redaksi (posisi teks lain tetap,
  form, gambar, vektor, anotasi, halaman di memori, metadata)
- validator: ✅ 114 PDF (19 × 6 runtime) lolos qpdf + pypdf + PDFium + fontTools + OTS;
  `invoice-redacted.pdf` lolos `--forbid-text` (pypdf, PDFium, byte file) di 6 runtime, QR tidak
  terbaca zxing
- output pembuatan PDF fase 0–6 tetap identik (selain versi di /Producer dan /ID)
- pack: ✅ `LifeOS.Pdf.0.8.0.nupkg`

## Rencana test manual
1. Buka `out/net8.0/invoice-redacted.pdf` di Chrome: kotak hitam di nama pelanggan, e-mail dan QR;
   Ctrl+F "Maju" tidak menemukan apa pun; salin-tempel seluruh halaman tidak memuat nama itu.
2. Buka `out/net8.0/extracted/report.html` di browser: judul, paragraf dan tabel terbaca.
3. Buka `extracted/report-table2.csv` di Excel/LibreOffice: kolom Tanggal/Kategori/Catatan/Nominal.
4. Ulangi 1 di Acrobat/SumatraPDF (fitur pencarian).
