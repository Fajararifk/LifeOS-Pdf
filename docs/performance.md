# Performance

Measured with `benchmarks/LifeOS.Pdf.Benchmarks` (Release, .NET 8.0.34, Windows laptop; best of
7 runs after ≥ 1 s warm-up). Two groups:

- **Phases 0–6 — optimization pass.** "Before" is v0.6.0 (commit `e978b42`), "after" is the
  optimized code. Output of every scenario is **byte-identical** before and after (verified by
  hashing 7 fixed documents: graphics, report with TOC/tables/lists, and all 5 built-in templates).
- **Phases 7–10 — baseline.** Text extraction, forms, security, render/scan/OCR and CSV/XLSX
  data. These features have not had a dedicated optimization pass yet.
- **Phase 11 — baseline.** HTML → PDF through a headless Chromium browser.

## Phases 0–6: optimization pass

| Scenario | Before ms | After ms | Speed-up | Alloc before → after (MB) |
|---|---:|---:|---:|---:|
| F0 graphics: 200 pages × 120 text + shapes | 229 | 117 | 2.0× | 96 → 33 |
| F0 write: 2000 small pages | 73 | 30 | 2.5× | 23 → 11 |
| F1 TrueType: 100 pages Unicode text | 61 | 38 | 1.6× | 63 → 16 |
| F2 layout: 400 paragraphs + 1500-row table | 648 | 134–176 | 3.7–4.8× | 255 → 35 |
| F2 layout: 3000 justified paragraphs | 920 | 168–205 | 4.5–5.5× | 484 → 65 |
| F3 template: invoice with 1500 items | 575 | 187 | 3.1× | 234 → 51 |
| F4 QR: encode 2000 codes | 688 | 314 | 2.2× | 7.5 → 7.5 |
| F4 QR: 300 codes drawn in a PDF | 166 | 79 | 2.1× | 25 → 8 |
| F5 TOC + 300 headings | 37 | 20–30 | 1.2–1.8× | 35 → 13 |
| F6 open a 2000-page file | — | 13 | | 6.6 |
| F6 open + save a 2000-page file | — | 22 | | 15 |

Ranges show run-to-run noise on the laptop.

## Phases 7–10 and CSV/XLSX: baseline

Same machine, same method. "Out KB" is the produced PDF/PNG; "—" means the scenario returns a
count instead. `Fixtures:` the layout report is 58 pages (126 KB), built from 400 paragraphs and
a 1500-row table; the image PDF contains 600 images (300 PNG + 300 JPEG).

| Scenario | ms | Alloc MB | Out KB |
|---|---:|---:|---:|
| F3 CSV parse: 5000 rows × 6 columns | 3.3 | 4.5 | — |
| F3 table report: 5000-row CSV | 840.6 | 166.2 | 559 |
| F3 XLSX parse: 2000 rows × 5 columns | 19.2 | 8.9 | — |
| F7 extract text + tables (58 pages) | 174.2 | 145.0 | — |
| F7 PDF → HTML (58 pages) | 181.5 | 146.4 | — |
| F7 search "Rp1.924.500" (58 pages) | 164.9 | 146.4 | — |
| F7 redact a value on every page | 196.6 | 169.9 | — |
| F7 read the images of a 600-image PDF | 1.0 | 1.2 | — |
| F8 fill 500 fields + flatten | 5.8 | 3.1 | 148 |
| F8 fill 500 fields, keep editable | 7.4 | 4.2 | 245 |
| F9 encrypt AES-128 (126 KB) | 1.6 | 2.1 | 129 |
| F9 encrypt RC4-128 (126 KB) | 2.1 | 2.0 | 126 |
| F9 open AES-128 file | 0.4 | 0.3 | — |
| F9 sign + verify a one-page contract | 4.4 | 0.8 | 17 |
| F10 render A4 @150 DPI | 55.7 | 24.7 | 312 |
| F10 render 10 pages @100 DPI | 278.8 | 112.3 | — |
| F10 read 20 QR codes from a page | 166.2 | 29.9 | — |
| F10 OCR A4 text @200 DPI | 1250.3 | ~0 | — |
| F10 make 3 pages searchable @150 DPI | 3641.3 | 73.6 | 7 |
| F10 scan page: render + OCR @150 DPI | 1115.9 | 24.4 | — |

What the numbers say:

- **Tabular data is cheap; layout is not.** Parsing 5000 CSV rows takes 3.3 ms and 2000 XLSX rows
  19 ms (zip + XML), while laying those 5000 rows out as a 559 KB report takes 0.84 s — the same
  cost per row as any other table.
- **Extraction is linear in pages and the biggest managed allocator**: ~2.5 MB per page
  (58 pages → 145 MB) because each page rebuilds glyph objects and strings. HTML conversion and
  search cost about the same as extraction, so the pipeline — not the converter — dominates.
  Reading an image's metadata is nearly free (1 ms for 600 images); pixel data is decoded lazily.
- **Security is negligible.** Encrypting or opening a 58-page document costs 0.4–2.1 ms, and
  AES-128 vs RC4-128 is within noise. Signing (RSA-2048, CMS/PKCS#7, whole-file `/ByteRange`)
  plus verification takes 4.4 ms.
- **Forms:** creating, filling and serializing 500 fields over 13 pages costs 6–8 ms. Flattening
  shrinks the file (245 → 148 KB) because the appearances move into the page content.
- **Rendering is native-bound**: 56 ms per A4 page at 150 DPI (312 KB PNG), ~28 ms per page at
  100 DPI.
- **OCR dominates any scan pipeline**: 1.25 s for one A4 page at 200 DPI, ~1.2 s per page for
  `MakeSearchable` at 150 DPI. `DocumentScanner.ScanPage` (render + OCR, barcode off) is 1.1 s, so
  the render is noise next to Tesseract. The allocation column shows ~0 MB for OCR because
  Tesseract allocates natively, outside the managed counter.
- **Barcodes are cheap**: 20 QR codes on one rendered page in 166 ms (~8 ms per code, including
  zxing-cpp's detection over the whole image).

Run it yourself:

```bash
dotnet run -c Release --project benchmarks/LifeOS.Pdf.Benchmarks           # all scenarios
dotnet run -c Release --project benchmarks/LifeOS.Pdf.Benchmarks -- layout # filter by name
dotnet run -c Release --project benchmarks/LifeOS.Pdf.Benchmarks -- --quick # 1 run, smoke test
```

Phase-10 scenarios need the native PDFium library next to the executable (`pdfium.dll` on
Windows); the OCR ones also need Tesseract's language data, pointed to by `LIFEOS_TESSDATA`.
Without them those scenarios print `skipped` instead of failing.

## What changed

- **Content streams** (`PdfGraphics`): operators and numbers are appended straight into the
  buffer. Numbers are formatted with integer arithmetic (same rounding as
  `Math.Round(v, 4, AwayFromZero)` + `"0.####"`, proven equal on 200 000 values in
  `FormatEquivalenceTests`), hex without per-byte strings; content bytes are copied from the
  buffer without an intermediate string.
- **Writer**: buffered `PdfOutput`; names, integers, reals, references and literal strings are
  written without temporary strings; Adler-32 uses the zlib block trick (one modulo per 5552
  bytes).
- **Text mapping**: standard-font `Map` builds arrays directly; `TextRun` no longer allocates an
  all-zero kerning array.
- **Layout**: no per-character `Substring`; measured words/spaces are cached per font
  (`TextRun` is immutable); paragraph atoms are built once (column widths and line breaking
  share them); struct cache keys instead of string concatenation; reusable scratch lists when
  drawing lines; and the document is laid out a second time only when the **body** shows the
  page count (a "page X of Y" footer no longer doubles the work).
- **QR**: flat module arrays, cached mask patterns per version, cheaper penalty bookkeeping.
- **Parser**: integers are read directly from bytes, names/keywords are interned per file,
  cross-reference entries are structs.

Phases 7–10 and the CSV/XLSX reader have no optimization pass yet: the table above is the
baseline to measure against when one happens.

## Speed vs. size

`PdfSaveOptions.CompressionLevel = CompressionLevel.Fastest` saves several times faster with
files typically 10–30% larger. The default stays `Optimal`.

## Rule for future changes

Performance work must not change output bytes. Re-run the benchmark and compare document hashes
against the previous release before merging.

## Phase 11: HTML → PDF (Chromium) — baseline

Same method (best of 7 after warm-up), measured on a Windows 11 machine with .NET 8.0.27 and
**Microsoft Edge 157** (headless, Chromium) through `LifeOS.Pdf.Html`. The F2 row was re-run on the
same machine so the two are directly comparable.
"Warm" means the converter is created once and reused; every conversion still gets its own fresh,
isolated browser context. The managed allocation column is not meaningful here: the work happens in
the browser's own processes and on async threads the counter does not see, so it is omitted.

| Scenario | ms | Out KB |
|---|---:|---:|
| Cold start: launch browser + first page | 1593 | 32 |
| HTML 1 page (warm) | 187 | 32 |
| HTML 1 page, `PostProcess = false` | 184 | 33 |
| Invoice: CSS + `data:` image + footer page numbers | 207 | 57 |
| Report: 1500-row table | 419 | 142 |
| 20 invoices, `MaxConcurrency = 1` | 4407 (220 / doc) | — |
| 20 invoices, `MaxConcurrency = 4` | 3709 (185 / doc) | — |
| Post-processing only (open + metadata + save) | 1.3 | 142 |
| *For comparison — F2 layout engine: 400 paragraphs + 1500-row table* | *152* | *126* |

Latency when requests arrive with a pause between them (measured separately: 10 conversions of the
1-page HTML, 500 ms apart): **best 132 ms, median 158 ms**.

What the numbers say:

- **Start the browser once.** Launching Edge costs ~1.6 s; a reused converter answers in ~190 ms.
  Create one `ChromiumConverter` per application (or use the `HtmlToPdf` facade) and call
  `WarmUpAsync` at start-up on servers.
- **The fixed cost is isolation.** Every conversion runs in a new browser context, which starts a
  renderer process (~100 ms of browser CPU). The converter opens spare contexts ahead of time, so with
  any pause between requests that cost is hidden (132–158 ms). Back-to-back, the browser has to create
  them anyway, which is why the warm number stays near 190 ms.
- **Concurrency helps only a little** (220 → 185 ms per document with 4 tabs): context creation and
  PDF generation are largely serialized in the browser process. For high volume, run several converters
  (each with its own browser) rather than raising `MaxConcurrency`.
- **Post-processing is free.** Re-saving with the LifeOS.Pdf writer (metadata, content-derived `/ID`,
  reproducible output) costs ~1 ms even for 142 KB; `PostProcess = false` saves nothing measurable.
- **Large documents are cheap per page.** 1500 table rows cost ~420 ms in total, ~230 ms more than a
  single page. The F2 layout engine renders the same table in ~150 ms without a browser, so code-built
  reports remain the fastest path; HTML is for when the template already exists as HTML/CSS.
- **Byte-identical output** needs the same input, the same `CreationDate` and the same browser version;
  a browser update may change the bytes (Skia's PDF output differs between versions).

Reproduce: `dotnet run -c Release --project benchmarks/LifeOS.Pdf.Benchmarks -- F11`. The scenarios are
skipped with a message when no Chrome, Edge or Chromium is installed (`LIFEOS_PDF_CHROMIUM` points to one).
