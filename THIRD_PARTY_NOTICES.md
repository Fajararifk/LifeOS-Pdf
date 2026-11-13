# Third-Party Notices

The core package `LifeOS.Pdf` contains no third-party code and has no NuGet
dependencies.

## Adobe Core 14 font metrics

`src/LifeOS.Pdf/Fonts/StandardFontMetrics.cs` contains glyph widths taken from
the Adobe Core 14 AFM files (Helvetica, Helvetica-Bold, Times-Roman,
Times-Bold, Courier). Adobe permits use, copying and distribution of these
metrics files. Only numeric widths are included; no font programs are
embedded or distributed.

## Build and test tooling (not shipped in the package)

| Tool | License | Used for |
|---|---|---|
| xUnit | Apache-2.0 | unit tests |
| pikepdf / qpdf | MPL-2.0 / Apache-2.0 | structural validation in `tools/validate_pdf.py` |
| pypdf | BSD-3-Clause | text extraction check |
| pypdfium2 / PDFium | Apache-2.0 / BSD-3-Clause | render check (Chrome's PDF engine) |
