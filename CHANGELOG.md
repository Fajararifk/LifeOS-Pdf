# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/); versions follow [SemVer](https://semver.org/).
Before v1.0, a minor version may contain breaking changes (always listed here).

## [1.1.0] — Phase 10: Render, Scan & OCR

### Added
- `LifeOS.Pdf.Rendering` (`PdfRenderer`): render PDF pages to PNG at a chosen DPI using PDFium
  (Apache-2.0). Needs the native PDFium library.
- `LifeOS.Pdf.Barcode.Scanning` (`BarcodeReader`): read QR codes and 1D barcodes from raw pixels or
  PNG using zxing-cpp (Apache-2.0, via `ZXingCpp`). Round-trips with the Fase 4 encoders; handles
  dense document QR codes (e.g. e-Faktur) that the older ZXing.Net port missed.
- `LifeOS.Pdf.Ocr` (`OcrEngine`): OCR text (with word boxes) from images using Tesseract
  (Apache-2.0); needs the `tessdata` language files.
- `LifeOS.Pdf.Scanning` (`DocumentScanner`): one call renders a page and runs barcode + OCR.

## [1.0.0] — Phase 9: Security (encryption)

### Added
- Standard security handler (§7.6.3): password encryption with **RC4-128** and **AES-128**
  (write and read), cross-validated against qpdf/pikepdf. AES-256 (Revision 6) is recognized and
  reported, not yet written.
- `PdfSaveOptions.Encryption` → `PdfEncryptionOptions` (user/owner password, `PdfEncryptionAlgorithm`,
  `PdfPermissions`, `EncryptMetadata`); `PdfReadOptions.Password`; `PdfDocument.IsEncrypted` and
  `PdfDocument.Permissions`.
- `PdfPermissions` flags (`Print`, `Modify`, `Copy`, `Annotate`, `FillForms`, `ExtractAccessibility`,
  `Assemble`, `PrintHighQuality`) and `PdfEncryptionAlgorithm` (`RC4_128`, `AES_128`, `AES_256`).
- Opening encrypted files with the user or owner password (including an empty user password);
  removing protection by re-saving without `Encryption`.
- RC4 implemented from scratch (removed from .NET); AES and the hashes use `System.Security.Cryptography`
  (no new dependency).

### Added (signing — `LifeOS.Pdf.Signing`, .NET 8+)
- `PdfSigner`: adds a CMS/PKCS#7 detached signature (PAdES B-B) to a signature field; the signature
  covers the whole file via `/ByteRange`; `Reason`, `Location`, `ContactInfo`.
- `PdfSignatureValidator`: verifies the signature and reports whether it covers the whole document.
- `PdfSignatureVerificationResult` (`IsValid`, `SignerName`, `CoversWholeDocument`).
- Dependency: `System.Security.Cryptography.Pkcs` (Microsoft).

### Notes
- Deferred to a later phase: RFC 3161 timestamps (TSA), OCSP/CRL/LTV, multiple signatures,
  certification signatures, and incremental save (`PdfSaveOptions.IncrementalUpdate`).

## [0.9.0] — Phase 8: Forms (AcroForm)

### Added
- `PdfDocument.Form` (`LifeOS.Pdf.Forms.PdfForm`): the interactive form of a document.
- `PdfPage.AddTextField`, `AddCheckBox`, `AddRadioButton` (a group shares one field name),
  `AddComboBox`, `AddListBox`, `AddButton`, `AddSignatureField` (placeholder; signing is Phase 9).
- Field types with explicit appearance streams: `PdfTextField` (multiline, password, comb,
  max length, alignment, font, colors), `PdfCheckBox` (on-value, check style), `PdfRadioButton`,
  `PdfChoiceField` (options, multi-select, editable), `PdfButton` (caption, optional ResetForm
  action), `PdfSignatureField`. Border and background colors via `/AP`; `/DA` written for viewers.
- Filling: `PdfForm.GetField`, `SetValue(name, string | bool | string[])` on an opened form
  regenerates the field's appearance. Read-only fields refuse changes.
- `PdfForm.Flatten`: bakes every widget's appearance into the page content (as a form XObject)
  and removes `/AcroForm` and the widgets.
- `PdfForm.ExportJson` / `ImportJson`: field values as a flat JSON object.
- `samples`/`FormSample`: fillable form + its flattened twin.

### Notes
- No new dependency; forms share the core writer/parser. JavaScript is never emitted (only the
  safe ResetForm action). FDF/XFDF, XFA flattening and digital signatures are deferred to Phase 8b/9.

## [0.8.0] — Phase 7: Content extraction

### Added
- Content-stream interpreter (graphics and text state, form XObjects with depth/cycle/work limits,
  inline images, ruling lines) and font decoding: ToUnicode CMaps, Standard/WinAnsi/MacRoman
  encodings with /Differences and the Adobe Glyph List, Type0/CID (/W, /DW), Type3, standard-14 widths.
- `PdfPage.ExtractText`, `GetTextLines`, `GetWords`, `GetGlyphs` (text with boxes, font, size,
  bold/italic; reading order; `TextExtractionOptions`: area, layout mode, column detection,
  invisible text); `PdfDocument.ExtractText`.
- `PdfDocument.Search` / `PdfPage.Search` (across words and lines, case-insensitive).
- `PdfPage.GetImages` → `ExtractedImage` (JPEG/JPEG 2000 as stored; Flate/LZW/RunLength images in
  Gray/RGB/CMYK/Indexed/ICCBased/Separation, 1–16 bit, /Decode, image masks and soft masks → PNG).
- `PdfPage.ExtractTables` → `ExtractedTable` (lattice: ruled grids with merged cells; stream:
  column-aligned text with continuation lines) with `ToCsv` and `ToJson`.
- `PdfTextConverter.ToText` / `ToHtml` (headings, paragraphs, bold/italic, tables).
- `PdfRedactor.Redact` / `RedactText`: glyphs, images, vector shapes, form XObject content and
  annotations inside the area are removed from the file; metadata and bookmarks are scrubbed.
- `PdfRect` (top-left rectangle used by extraction and redaction).
- `tools/validate_pdf.py --forbid-text` (redaction check with pypdf, PDFium and the raw bytes).

## [0.7.0] — Phase 6: Open & edit existing PDFs, performance

### Added
- `PdfDocument.Open(path | Stream | byte[], PdfReadOptions)`: parser for classic and
  cross-reference-stream files, object streams, incremental updates, linearized files and
  damaged files (cross-reference rebuilt); Flate/LZW (with predictors), ASCII85, ASCIIHex and
  RunLength decoding. Info, page sizes, rotation and bookmarks are read; `PdfVersion`.
- Page operations: `ImportPage(s)`, `InsertImportedPage`, `InsertPage`, `RemovePage`, `MovePage`,
  `PdfPage.Rotation`, `PdfPage.IsImported`; drawing, links and annotations on opened pages.
- `PdfGraphics.DrawPage`: any page as a vector form XObject (n-up, letterheads).
- `LifeOS.Pdf.Manipulation`: `PdfMerger` (bookmark per document), `PdfSplitter` (every N pages,
  ranges, extract), `PdfStamper` (text/image watermark, page numbers, Bates numbers — placed by
  displayed orientation), `PdfImposition` (n-up, booklet, overlay/letterhead behind content).
- `PageRange` (`"1-3,5,8-"`, `"last"`, descending ranges).
- `PdfSaveOptions.UseObjectStreams` (object streams + cross-reference stream, smaller files).
- Safety for untrusted input: `PdfReadOptions` limits (nesting depth, decompressed stream size,
  object count, pages), cycle-safe page/outline/name trees, `/Prev` loop detection;
  `PdfReadException`, `PdfEncryptedException`. JavaScript, launch and form-submit actions and
  `/AA` are not copied from source pages.

### Performance
- 2–5× faster generation with 3–7× fewer allocations across graphics, TrueType text, the layout
  engine, templates and QR codes; output bytes are unchanged. Details and numbers in
  [docs/performance.md](docs/performance.md).
- Layout no longer runs a second pass when only headers/footers show the page count.

### Added (tooling)
- `PdfSaveOptions.CompressionLevel` (`Optimal` by default, `Fastest` for quicker saves).
- `benchmarks/LifeOS.Pdf.Benchmarks`: repeatable timings per phase.

## [0.6.0] — Phase 5: Navigation & interactive features

### Added
- Links: `PdfPage.AddLink(x, y, w, h, uri)` (http, https, mailto, tel only — `javascript:`
  and other schemes are refused) and `AddLink(…, PdfDestination)` to a page/position or a
  named destination. `PdfDestination.ToPage/FitPage/FitWidth/Named`.
- `PdfDocument.Outline`: nested bookmarks with open/closed state, bold/italic and color.
- `PdfDocument.NamedDestinations`, `PageLabels` (decimal, roman, letters, prefix, start number),
  `ViewerPreferences` (page mode, page layout, initial zoom/page, hide toolbar/menubar, …).
- `PdfDocument.Attachments`: embedded files with MIME type, description, size and MD5 checksum.
- Annotations with their own appearance streams (identical in every viewer):
  `AddNote` (sticky note), `AddHighlight`, `AddStamp` ("LUNAS", "DRAFT", …).
- Layout engine: `Paragraph.AddLink(text, "https://…" | "#anchor")`, `Paragraph.Anchor`,
  bookmarks generated from headings (`FlowDocument.GenerateOutline`, default on),
  `AddTableOfContents(title, maxLevel)` with dot leaders, page numbers and clickable entries,
  `DocumentStyles.Link`, `FlowDocument.ViewerPreferences` and `Attachments`.
- Templates: `<a href>`, `anchor` on `p`/`h1`–`h3`, `<toc>`, `<attachment>`,
  `<styles><link/>`, and `outline`, `pageMode`, `pageLayout`, `zoom` on `<document>`.
- `tools/validate_pdf.py`: `--expect-outline`, `--expect-link`, `--expect-attachment`; bookmarks
  must resolve to a page and links to defined named destinations.

### Changed
- A document with bookmarks opens with the bookmark panel (`/PageMode /UseOutlines`) unless
  `ViewerPreferences.PageMode` says otherwise.

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
