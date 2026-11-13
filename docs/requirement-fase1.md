# Requirement Fase 1: Font, Gambar & Image→PDF

## 1. Ringkasan & batas scope
Fase 0 hanya mendukung 14 font standar (WinAnsi, Latin saja) dan JPEG. Fase 1
membuat library layak dipakai untuk dokumen nyata:
1. **Font TrueType tertanam** dengan Unicode penuh: teks Indonesia, Vietnam,
   Yunani, Sirilik, CJK (dari font TrueType), emoji/simbol di luar BMP bila font
   menyediakan glyph-nya. Teks tetap bisa di-copy dan dicari.
2. **PNG** lengkap, termasuk transparansi.
3. **Konversi gambar → PDF** (use case 5).
4. **Transformasi & opacity** di `PdfGraphics` (rotate, scale, translate, transparansi).
5. **Metrik AFM lengkap** untuk 14 font standar (menutup catatan QA Fase 0).

**Tidak masuk** (dipindah):
- Kerning, ligature, dan text shaping (GSUB/GPOS; Arab, Devanagari, Thai) → Fase 2 (layout engine).
  Konsekuensi: aksara yang butuh shaping tampil sebagai glyph terpisah.
- Font OpenType berbasis CFF (`.otf` dengan outline PostScript) → backlog;
  di Fase 1 ditolak dengan pesan jelas.
- Font fallback otomatis antar-font → Fase 2.
- PNG encoder & export gambar → Fase 4 (dipakai untuk QR ke PNG).
- Profil warna (iCCP, gAMA, sRGB chunk) → Fase 12; Fase 1 mengabaikannya.
- TIFF, BMP, GIF, WebP → backlog.

## 2. Requirement fungsional

### Font TrueType
- FR-01 Muat font dari file, `Stream`, atau `byte[]`: `.ttf` dan `.ttc`
  (pilih index dalam koleksi). Font yang sama dipakai di banyak ukuran dan halaman.
- FR-02 Gambar teks Unicode apa pun yang glyph-nya ada di font, termasuk
  surrogate pair (U+10000 ke atas).
- FR-03 Font di-*subset*: hanya glyph yang dipakai di seluruh dokumen yang
  ditanam. Font dengan izin "no subsetting" ditanam utuh.
- FR-04 Teks hasil font tertanam bisa di-copy/di-search dengan benar di viewer
  dan ter-extract oleh pypdf (ToUnicode CMap).
- FR-05 `MeasureWidth`, `DrawText` (alignment), dan `DrawTextBox` (word-wrap)
  bekerja sama persis untuk font standar maupun tertanam.
- FR-06 Hormati izin lisensi font (OS/2 `fsType`): font "Restricted License"
  ditolak dengan exception yang menjelaskan alasannya.
- FR-07 Karakter yang tidak punya glyph: digambar sebagai glyph `.notdef`
  (kotak), dan bisa dicek lebih dulu lewat `HasGlyph(...)`.
- FR-08 Informasi font tersedia: nama PostScript, nama family, bold/italic,
  ascent/descent, jumlah glyph.

### Font standar
- FR-09 Lebar glyph 14 font standar diambil dari AFM resmi untuk seluruh kode
  WinAnsi 32–255 (dan encoding bawaan Symbol/ZapfDingbats); ascent/descent dari AFM.

### Gambar
- FR-10 PNG: color type 0, 2, 3, 4, 6; bit depth 1/2/4/8/16; palet + `tRNS`;
  interlace Adam7. Alpha → soft mask (SMask) sehingga transparansi tampil benar.
- FR-11 PNG tanpa alpha & tanpa interlace ditanam tanpa decode ulang
  (IDAT langsung + predictor PNG) supaya cepat dan file kecil.
- FR-12 `PdfImage` otomatis mengenali JPEG atau PNG dari isi file (bukan ekstensi).
- FR-13 Resolusi asli gambar (JPEG JFIF density, PNG `pHYs`) tersedia sebagai DPI.

### Konversi gambar → PDF
- FR-14 Ubah satu atau banyak gambar (JPEG/PNG) menjadi PDF, satu gambar per
  halaman. Opsi: ukuran halaman tetap (mis. A4) atau mengikuti ukuran gambar
  (berdasarkan DPI), margin, mode `Fit` (contain, tidak terpotong) atau
  `Fill` (cover, boleh terpotong), orientasi otomatis mengikuti gambar.
- FR-15 Hasil konversi berupa `PdfDocument` biasa (bisa ditambah halaman/teks
  sebelum disimpan).

### Grafis
- FR-16 `PdfGraphics`: simpan/pulihkan state (bisa dipakai dengan `using`),
  translate, rotate (derajat, searah jarum jam seperti di layar) di titik
  tertentu, scale di titik tertentu.
- FR-17 Opacity 0–1 untuk fill dan stroke (berlaku juga ke teks dan gambar).
- FR-18 State yang tidak dipulihkan saat dokumen disimpan → exception jelas.

## 3. Requirement non-fungsional
- NFR-01 Semua output lolos qpdf, pypdf, PDFium; teks Unicode ter-extract persis
  oleh pypdf; tampil benar di Chrome dan satu reader lain.
- NFR-02 Tetap netstandard2.0 + net5–net10, package inti tanpa dependency.
- NFR-03 Deterministik: input sama → byte sama (termasuk tag subset `ABCDEF+`).
- NFR-04 Ukuran: dokumen 1 halaman berisi 100 karakter Latin dengan Noto Sans
  ter-subset < 40 KB. Gambar PNG non-alpha tidak membesar > 2% dari file asli.
- NFR-05 Performa: memuat font 1 MB < 100 ms; subset + tulis < 200 ms
  (mesin dev, Release).
- NFR-06 **Input tidak terpercaya**: parser TrueType dan PNG memeriksa batas di
  setiap pembacaan. File rusak/berbahaya → `FormatException`/`NotSupportedException`,
  tidak pernah hang, `IndexOutOfRangeException`, stack overflow, atau alokasi raksasa
  (batas: dimensi PNG ≤ 30.000 px per sisi dan ≤ 200 juta piksel; kedalaman
  glyph komposit ≤ 16).
- NFR-07 API Fase 0 tetap jalan; satu-satunya perubahan breaking dicatat di
  design + CHANGELOG.

## 4. Referensi spesifikasi
- ISO 32000-1 §9.7 (Type0 & CIDFont), §9.7.4 CIDFontType2, §9.7.5.2 Identity-H,
  §9.8 Font descriptor (Flags, FontFile2), §9.10.3 ToUnicode CMap,
  §9.9 embedding & subset tag (§9.6.4), §8.4.5 ExtGState (`CA`, `ca`),
  §8.3.4 matriks transformasi (`cm`), §8.9.5 image dict, §11.6.5.3 SMask,
  §7.4.4.4 predictor PNG (`DecodeParms`), Annex C batas implementasi (q ≤ 28)
- Apple TrueType Reference & OpenType spec 1.9: tabel head, hhea, hmtx, maxp,
  loca, glyf (glyph komposit), cmap (format 0, 4, 6, 12), OS/2 (`fsType`), name, post; TTC header
- PNG Specification (W3C, 3rd ed.): chunk, CRC-32, filter 0–4, Adam7
- Adobe Core14 AFM; Adobe Glyph List (untuk memetakan WinAnsi → nama glyph)

## 5. Edge cases
- Font: `.otf` CFF, `.woff/.woff2`, file terpotong, offset tabel di luar file,
  `loca` tidak monoton, glyph komposit melingkar (A → B → A), numGlyphs 0,
  tidak ada cmap Unicode (hanya symbol 3,0), `fsType` restricted / no-subset /
  bitmap-only, index TTC di luar jangkauan.
- Teks: string kosong, hanya spasi, surrogate pair rusak (high tanpa low),
  karakter tanpa glyph, campuran font standar & tertanam di satu halaman,
  satu font tertanam di 100 halaman (harus satu objek font).
- PNG: CRC salah, IDAT kosong/terpotong, chunk kritis tidak dikenal, palet
  tanpa PLTE, `tRNS` untuk gray/RGB (color key), bit depth 16 dengan alpha,
  lebar 1 px, interlace dengan gambar lebih kecil dari 8×8, dimensi raksasa.
- Grafis: rotate 0/360/-90, scale 0 atau negatif (cermin), opacity < 0 / > 1 / NaN,
  `Restore` tanpa `Save`, nesting > 28.
- Konversi: daftar gambar kosong, campuran portrait & landscape, gambar tanpa
  info DPI (anggap 72/96?), gambar lebih besar dari halaman 14400 pt.

## 6. Pertanyaan klarifikasi (dengan usulan jawaban analyst)
1. **DPI default** untuk gambar tanpa info resolusi saat mode "ukuran halaman =
   ukuran gambar"? → Usul **96 DPI** (asumsi gambar layar/screenshot).
2. **Karakter tanpa glyph**: tampil kotak `.notdef` (usulan) atau lempar exception?
   → Usul kotak, plus `HasGlyph` untuk validasi sebelumnya.
3. **Font uji di repo**: Noto Sans (SIL OFL 1.1) di-subset seperlunya agar repo
   kecil. Font Windows (Arial, MS Gothic) hanya untuk uji manual lokal, tidak di-commit.
4. **Breaking change** `PdfFont.Family` (lihat design) diterima karena masih v0.x?
