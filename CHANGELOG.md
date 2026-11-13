# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/), versi mengikuti [SemVer](https://semver.org/).
Sebelum v1.0, versi minor boleh berisi perubahan breaking (selalu dicatat di sini).

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
