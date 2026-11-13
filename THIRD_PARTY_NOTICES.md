# Third-Party Notices

The core package `LifeOS.Pdf` contains no third-party code and has no NuGet
dependencies.

## Adobe Core 14 font metrics

`src/LifeOS.Pdf/Fonts/Standard/StandardFontMetrics.Generated.cs` contains glyph
widths and font-wide metrics generated (by `tools/generate_afm_metrics.py`) from
the Adobe Core 14 AFM files. Adobe permits use, copying and distribution of
these metrics files. Only numeric metrics are included; no font programs are
embedded or distributed.

## Noto fonts (test and sample assets only, not in the NuGet package)

`tests/LifeOS.Pdf.Tests/TestAssets/fonts/*` and
`samples/LifeOS.Pdf.Samples/Assets/NotoSans-sample.ttf` are subsets of Noto Sans
and Noto Sans Math, Copyright 2022 The Noto Project Authors, licensed under the
SIL Open Font License 1.1 (see the `OFL.txt` next to them). They were subset
with `tools/make_test_assets.py`.

## Build and test tooling (not shipped in the package)

| Tool | License | Used for |
|---|---|---|
| xUnit | Apache-2.0 | unit tests |
| pikepdf / qpdf | MPL-2.0 / Apache-2.0 | structural validation in `tools/validate_pdf.py` |
| pypdf | BSD-3-Clause | text extraction check |
| pypdfium2 / PDFium | Apache-2.0 / BSD-3-Clause | render check (Chrome's PDF engine) |
| fontTools | MIT | AFM → C# generator, test font subsetting, embedded font validation |
| opentype-sanitizer (OTS) | BSD-3-Clause | embedded font validation (as in Chrome/Firefox) |
| pypng | MIT | PNG test suite and reference pixels |
