# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/); versions follow [SemVer](https://semver.org/).
Before v1.0, a minor version may contain breaking changes (always listed here).

## [0.5.0] — Phase 4: QR codes & barcodes

### Added
- `QrCode`: QR Code Model 2 encoder written from ISO/IEC 18004 — numeric, alphanumeric and
  byte (UTF-8) modes, versions 1–40, error correction L/M/Q/H, automatic or fixed mask;
  `ToPng`, `ToSvg`.
- `Barcode`: Code 128 (automatic A/B/C sets), Code 39 (optional mod-43 check), EAN-13, EAN-8,
  UPC-A, ITF, ITF-14 with check digits calculated or verified; `ToPng`, `ToSvg`.
- `PdfGraphics.DrawQrCode` (one vector path, optional logo with ECC Q/H) and
  `PdfGraphics.DrawBarcode` (bars + human-readable text).
- `Qris`: CRC-16/CCITT-FALSE, payload validation and parsing, static → dynamic with an amount.
- Layout engine: `AddQrCode`, `AddBarcode` (`BarcodeBlock`); templates: `<qrcode>`, `<barcode>`.
- Built-in invoice template: optional QRIS payment code (`PaymentQr`).
- Internal PNG writer (used for exports).

## [0.4.0] — Phase 3: Report templates

### Added
- `LifeOS.Pdf.Templates.PdfTemplate`: XML report templates filled with C# objects or JSON
  (`Load`, `Parse`, `FromStream`, `Bind` → `FlowDocument`, `Render` → `PdfDocument`).
  - Every layout feature as markup: sections, header/footer, headings, paragraphs with inline
    styles, lists, tables (spans, repeating header, zebra), images, lines, page fields.
  - `{{ … }}` placeholders with paths, arithmetic, comparisons, logic and filters; `foreach`,
    `if`/`else`, `set` anywhere (blocks, rows, cells, list items, inside paragraphs).
  - Filters: rupiah, currency, number, percent, round, date, terbilang, words, upper, lower,
    title, default, join, count, sum, sumproduct, avg, min, max, first, last, sort, sortdesc,
    where, groupby.
  - Errors as `TemplateException` with line and column; optional strict mode for missing data.
  - Safe for untrusted templates: no DTD/XXE, file paths confined to the template folder,
    no method calls.
- `BuiltInTemplates`: invoice, receipt (kwitansi), payslip, monthly finance report, certificate.
- `TemplateData.FromJson`: dependency-free JSON parser (exact decimals, line/column errors).
- `TextFormats`: Rupiah, number, percent, date (Indonesian & English) and number words
  (Indonesian *terbilang*, English) that do not depend on the machine's culture.

## [0.3.0] — Phase 2: Layout engine

### Added
- `LifeOS.Pdf.Layout.FlowDocument`: documents described as flowing content and paginated automatically.
  - Sections with their own page size, margins and columns; headers and footers (optionally different
    on the first page); page number and page count fields ("Page 3 of 12").
  - Paragraphs with mixed styles (font family, size, bold, italic, color, underline, strikethrough),
    left/center/right/justified alignment, indents, spacing, keep-together, keep-with-next, page breaks.
  - Headings 1–3, nested bulleted/numbered lists, images, horizontal lines, spacers.
  - Tables: fixed/percent/relative/auto columns, column and row spans, header rows repeated on every
    page, zebra rows, cell backgrounds, vertical alignment, nested tables.
  - `FontFamily` (regular/bold/italic/bold italic) and per-character **font fallback**.
  - `LayoutException` with an actionable message when content cannot fit.
- Kerning for TrueType fonts (GPOS `kern` feature and the legacy `kern` table), on by default in the layout engine.

### Fixed
- Characters without a glyph no longer get a ToUnicode entry (copy-paste no longer yields a wrong letter).

### Notes
- `PdfGraphics` is unchanged: still no kerning, so Phase 0–1 output is identical.

## [0.2.0] — Phase 1: Fonts, images & image → PDF

### Added
- `PdfFontFace`: load TrueType fonts (`.ttf`) and collections (`.ttc`) from a file, stream or bytes;
  full Unicode text (including characters above U+FFFF), automatic subsetting, ToUnicode so text can be
  copied and searched. `HasGlyph()` to check characters before drawing.
- Font licensing (OS/2 `fsType`) is honored: "restricted" fonts are rejected, "no subsetting" fonts are embedded whole.
- PNG: every color type and bit depth, palettes, `tRNS`, Adam7 interlacing, transparency via soft masks.
  PNGs without alpha are embedded without re-encoding.
- `PdfImage.Format`, `HasAlpha`, `DpiX`, `DpiY` (from JFIF / pHYs).
- `ImageToPdf.Convert(...)` + `ImageToPdfOptions` (page size, margin, contain/cover, auto-rotate, default DPI).
- `PdfGraphics`: `Save()`/`Restore()` (usable with `using`), `Translate`, `RotateAt`, `ScaleAt`,
  `SetOpacity`, `ClipRectangle`.
- Official Adobe AFM metrics for the 14 standard fonts (all WinAnsi codes 32–255).

### Changed (breaking)
- `PdfFont.Family` (of type `StandardFont`) was removed in favor of `PdfFont.Face` (of type `PdfFontFace`).
  Migration: `font.Family == StandardFont.Helvetica` → `font.Face == PdfFontFace.Standard(StandardFont.Helvetica)`.

### Fixed
- Measuring Times-Italic, Times-BoldItalic, Symbol, ZapfDingbats and characters 128–255 now uses the real widths
  (previously approximated).

## [0.1.0] — Phase 0: Core writer
- First release: documents, pages, text in the 14 standard fonts, lines, rectangles, ellipses, JPEG,
  Flate compression, metadata. netstandard2.0 + .NET 5–10.
