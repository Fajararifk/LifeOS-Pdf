# Performance

Measured with `benchmarks/LifeOS.Pdf.Benchmarks` (Release, .NET 8.0.34, Windows laptop; best of
7 runs after ≥ 1 s warm-up). "Before" is v0.6.0 (commit `e978b42`), "after" is the optimized
code. Output of every phase-0…5 scenario is **byte-identical** before and after (verified by
hashing 7 fixed documents: graphics, report with TOC/tables/lists, and all 5 built-in templates).

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

Ranges show run-to-run noise on the laptop. Run it yourself:

```bash
dotnet run -c Release --project benchmarks/LifeOS.Pdf.Benchmarks          # all scenarios
dotnet run -c Release --project benchmarks/LifeOS.Pdf.Benchmarks -- layout # filter by name
```

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

## Speed vs. size

`PdfSaveOptions.CompressionLevel = CompressionLevel.Fastest` saves several times faster with
files typically 10–30% larger. The default stays `Optimal`.

## Rule for future changes

Performance work must not change output bytes. Re-run the benchmark and compare document hashes
against the previous release before merging.
