# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/), versi mengikuti [SemVer](https://semver.org/).
Sebelum v1.0, versi minor boleh berisi perubahan breaking (selalu dicatat di sini).

## [0.3.0] — Fase 2: Layout Engine

### Added
- `LifeOS.Pdf.Layout.FlowDocument`: dokumen sebagai alur konten yang dipaginasi otomatis.
  - Section dengan ukuran kertas, margin, kolom ganda, header & footer (beda di halaman pertama),
    field nomor halaman dan jumlah halaman ("Halaman 3 dari 12").
  - Paragraf dengan gaya campuran (font family, ukuran, tebal, miring, warna, garis bawah, coret),
    perataan kiri/tengah/kanan/justify, indent, spasi, keep-together, keep-with-next, page break.
  - Heading 1–3, daftar bullet/bernomor bertingkat, gambar, garis horizontal, spasi.
  - Tabel: kolom Fixed/Percent/Relative/Auto, colspan, rowspan, header berulang di setiap halaman,
    baris zebra, latar sel, perataan vertikal, tabel bersarang.
  - `FontFamily` (Regular/Bold/Italic/BoldItalic) dan **font fallback** per karakter.
  - `LayoutException` dengan pesan jelas bila konten tidak mungkin muat.
- Kerning untuk font TrueType (GPOS `kern` dan tabel `kern`), aktif di layout engine.

### Fixed
- Karakter yang tidak punya glyph tidak lagi diberi entri ToUnicode (copy-paste tidak menghasilkan huruf yang salah).

### Notes
- `PdfGraphics` tidak berubah: tetap tanpa kerning, output Fase 0–1 identik.

## [0.2.0] — Fase 1: Font, Gambar & Image→PDF

### Added
- `PdfFontFace`: muat font TrueType (`.ttf`) dan koleksi (`.ttc`) dari file, stream, atau byte;
  teks Unicode penuh (termasuk karakter di atas U+FFFF), subsetting otomatis, ToUnicode
  supaya teks bisa di-copy/dicari. `HasGlyph()` untuk cek karakter sebelum digambar.
- Izin lisensi font (OS/2 `fsType`) dihormati: font "restricted" ditolak, "no subsetting" ditanam utuh.
- PNG: semua color type & bit depth, palet, `tRNS`, interlace Adam7, transparansi via soft mask.
  PNG tanpa alpha ditanam tanpa decode ulang.
- `PdfImage.Format`, `HasAlpha`, `DpiX`, `DpiY` (dari JFIF / pHYs).
- `ImageToPdf.Convert(...)` + `ImageToPdfOptions` (ukuran halaman, margin, Contain/Cover, auto-rotate, DPI default).
- `PdfGraphics`: `Save()`/`Restore()` (bisa dipakai dengan `using`), `Translate`, `RotateAt`,
  `ScaleAt`, `SetOpacity`, `ClipRectangle`.
- Metrik resmi Adobe AFM untuk 14 font standar (semua kode WinAnsi 32–255).

### Changed (breaking)
- `PdfFont.Family` (tipe `StandardFont`) dihapus, diganti `PdfFont.Face` (tipe `PdfFontFace`).
  Migrasi: `font.Family == StandardFont.Helvetica` → `font.Face == PdfFontFace.Standard(StandardFont.Helvetica)`.

### Fixed
- Pengukuran teks Times-Italic, Times-BoldItalic, Symbol, ZapfDingbats, dan karakter 128–255
  sekarang memakai lebar asli (sebelumnya perkiraan).

## [0.1.0] — Fase 0: Core Writer
- Rilis pertama: dokumen, halaman, teks 14 font standar, garis, kotak, elips, JPEG,
  kompresi Flate, metadata. netstandard2.0 + .NET 5–10.
