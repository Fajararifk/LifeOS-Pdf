# LifeOS.Pdf

An open-source PDF library for .NET, **written from scratch** in C# with no
dependencies. Runs on **.NET Standard 2.0 and .NET 5, 6, 7, 8, 9 and 10**
(and .NET Framework 4.6.1+ through netstandard2.0).

Every generated file is validated with qpdf, pypdf and PDFium (the engine behind
Chrome's PDF viewer), and embedded fonts with fontTools and OTS (the font
sanitizer used by Chrome and Firefox), so output opens in Chrome, Edge, Firefox,
Adobe Acrobat and other PDF readers.

<p align="center">
  <img src="docs/images/showcase-p1.png" width="380" alt="Sample page: text in the 14 standard fonts, shapes, JPEG image">
  <img src="docs/images/showcase-p3.png" width="380" alt="Sample page: embedded Unicode fonts, transparent PNG, rotation and watermark">
  <img src="docs/images/report-p1.png" width="380" alt="Multi-page finance report from the layout engine: justified paragraphs, lists, tables with row and column spans">
  <img src="docs/images/template-invoice.png" width="380" alt="Invoice rendered from the built-in template with JSON data: line items, discount, VAT, total in words">
</p>

## Installation

```bash
dotnet add package LifeOS.Pdf
```

Nothing else to install, on a developer machine or a server: the package is
100% managed code with no native libraries and no `System.Drawing`, so it runs
on Linux, Windows, macOS, Docker and serverless hosts alike. The 14 standard
fonts need no font files; for TrueType fonts you ship the `.ttf` with your app.

## Status

| Phase | Scope | Status |
|---|---|---|
| 0 | Core writer: pages, text in the 14 standard fonts, shapes, JPEG, compression, metadata | ✅ v0.1 |
| 1 | TrueType fonts with full Unicode, transparent PNG, image → PDF, rotation & opacity | ✅ v0.2 |
| 2 | Layout engine: paragraphs, multi-page tables, header/footer, kerning, font fallback | ✅ v0.3 |
| 3 | Reports from templates (invoice, receipt, payslip, finance report, certificate) | ✅ v0.4 |
| 4 | QR codes & barcodes (vector, PNG, SVG), QRIS | ✅ v0.5 |
| 5 | Links, bookmarks, attachments, annotations | next |
| 6 | Open existing PDFs: merge, split, watermark, compress | planned |
| 7 | Extract text, images and tables (CSV) | planned |
| 8 | Forms: create, fill, flatten | planned |
| 9 | Encryption & digital signatures | planned |
| 10 | Render to images, read QR/barcodes, OCR (separate packages) | planned |
| 11 | HTML → PDF (wkhtmltopdf & Chromium, separate package) | planned |
| 12 | PDF/A, PDF/UA, gradients, layers, e-invoicing | planned |

The full roadmap and use-case mapping is in [docs/PHASES.md](docs/PHASES.md).

## Quick start

```csharp
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;

var doc = new PdfDocument();
doc.Info.Title = "Monthly report";

var page = doc.AddPage(PageSize.A4);
var g = page.Graphics; // origin (0,0) at the top-left, units in points (1/72 inch)

g.DrawText("Hello world", new PdfFont(StandardFont.HelveticaBold, 24), 72, 72);
g.DrawTextBox("Long paragraphs are wrapped automatically to the width of the box.",
    new PdfFont(StandardFont.TimesRoman, 12), 72, 120, width: 300);
g.DrawRoundedRectangle(new PdfPen(PdfColor.Blue, 1), PdfColor.FromHex("#DBEAFE"), 72, 200, 200, 60, radius: 8);
g.DrawText("$1,350.00", new PdfFont(StandardFont.Courier, 12), 272, 220, align: TextAlign.Right);
g.DrawImage(PdfImage.FromFile("photo.jpg"), 72, 300, width: 250); // height keeps the aspect ratio

doc.Save("report.pdf");
```

### TrueType fonts & Unicode, PNG, watermark

```csharp
var noto = PdfFontFace.FromFile("NotoSans-Regular.ttf");   // .ttf or .ttc
g.DrawText("Tiếng Việt · Ελληνικά · Русский · 日本語*", new PdfFont(noto, 12), 72, 400);
// *as long as the font has the glyphs — check first with noto.HasGlyph("日本語")

g.DrawImage(PdfImage.FromFile("logo.png"), 400, 40, 120);   // transparent PNG

using (g.Save())
{
    g.RotateAt(-35, page.Width / 2, page.Height / 2);
    g.SetOpacity(0.1);
    g.DrawText("DRAFT", new PdfFont(StandardFont.HelveticaBold, 120), page.Width / 2, page.Height / 2, align: TextAlign.Center);
}
```

Fonts are subset automatically (only the glyphs you use are embedded) and text
stays selectable and searchable in viewers.

### Multi-page reports (layout engine)

No coordinates needed: describe the content, and the library breaks lines,
creates pages, repeats table headers and fills in "Page X of Y".

```csharp
using LifeOS.Pdf.Layout;

var doc = new FlowDocument();
doc.Styles.Normal = new TextStyle { Family = FontFamily.Helvetica, Size = 10,
    Fallbacks = { FontFamily.FromFiles("NotoSans-Regular.ttf") } };   // for non-Latin characters

var section = doc.AddSection(new PageSetup { PageSize = PageSize.A4 });
section.Footer.AddParagraph().AddText("Page ").AddPageNumber().AddText(" of ").AddPageCount()
    .Format.Alignment = ParagraphAlignment.Center;

section.AddHeading("Finance report — November 2026");
section.AddParagraph()
    .AddText("Total spending ")
    .AddText("$7,482.50", new TextStyle { Bold = true })
    .AddText(", down 12% from last month.");

var table = section.AddTable();
table.AddColumn(ColumnWidth.Auto);
table.AddColumn(ColumnWidth.Relative());
table.AddColumn(ColumnWidth.Fixed(90), ParagraphAlignment.Right);
table.HeaderRows = 1;                                   // repeated on every page
table.AlternateRowBackground = PdfColor.FromHex("#F9FAFB");
table.AddRow("Date", "Note", "Amount");
foreach (var t in transactions)
    table.AddRow(t.Date, t.Note, t.Amount);

doc.Save("report.pdf");
```

Also available: justified text, bulleted/numbered lists, column and row spans,
images in the flow, multiple columns, keep-with-next, page breaks, a different
header/footer on the first page. Full example:
[ReportSample.cs](samples/LifeOS.Pdf.Samples/ReportSample.cs).

### Reports from templates

Keep the layout in an XML template that can be edited without recompiling, and fill it
with a C# object or JSON. Five templates ship with the library: invoice, receipt
(*kwitansi*), payslip, monthly finance report and certificate.

```csharp
using LifeOS.Pdf.Templates;

// Built-in template + JSON data
BuiltInTemplates.Invoice.Render(TemplateData.FromJson(json)).Save("invoice.pdf");

// Your own template
var template = PdfTemplate.Load("templates/order.xml");
template.Render(new { Number = "ORD-17", Customer = customer, Items = items }).Save("order.pdf");
```

```xml
<document title="Order {{ Number }}">
  <section size="A4" margin="20mm">
    <h1>Order {{ Number }}</h1>
    <p>For <b>{{ Customer.Name }}</b> · {{ Date | date:"d MMMM yyyy" }}</p>
    <table headerRows="1" zebra="#F9FAFB">
      <column width="*"/><column width="auto" align="right"/><column width="90" align="right"/>
      <row><cell>Item</cell><cell>Qty</cell><cell>Amount</cell></row>
      <foreach in="Items" as="item">
        <row><cell>{{ item.Name }}</cell><cell>{{ item.Quantity }}</cell><cell>{{ item.Quantity * item.Price | rupiah }}</cell></row>
      </foreach>
    </table>
    <p align="right"><b>Total {{ Items | sumproduct:"Quantity":"Price" | rupiah }}</b></p>
  </section>
</document>
```

Loops, conditions, variables, grouping and sums, and Indonesian/English formatting of
money, dates and numbers in words (*terbilang*) are built in. Full reference:
[docs/templates.md](docs/templates.md).

### QR codes & barcodes

Encoded from scratch and drawn as vectors (sharp at any zoom), or exported as PNG/SVG.
Every symbol is verified to scan with an independent decoder.

```csharp
using LifeOS.Pdf.Barcodes;

g.DrawQrCode(QrCode.Encode("https://lifeos.app/inv/42"), 400, 60, size: 100);
g.DrawQrCode(QrCode.Encode(url, QrErrorCorrection.High), 400, 200, 100, logo: PdfImage.FromFile("logo.png"));
g.DrawBarcode(Barcode.Code128("INV-2026-0042"), 72, 700, width: 200, height: 50);
g.DrawBarcode(Barcode.Ean13("899123456789"), 300, 700, 160, 60);    // check digit added

File.WriteAllBytes("qr.png", QrCode.Encode("hello").ToPng(moduleSize: 8));

// QRIS: turn a merchant's static QRIS into a dynamic one for an exact amount
var payload = Qris.WithAmount(staticQris, 125000);
```

QR (versions 1–40, error correction L/M/Q/H), Code 128, Code 39, EAN-13, EAN-8, UPC-A,
ITF and ITF-14. Also available as `section.AddQrCode(...)`/`AddBarcode(...)` in the layout
engine and as `<qrcode>`/`<barcode>` in templates; the built-in invoice shows a QRIS payment
code when the data has `PaymentQr`.

### Images → PDF

```csharp
using LifeOS.Pdf.Conversion;

var scan = ImageToPdf.Convert(new[] { "page1.jpg", "page2.png" },
    new ImageToPdfOptions { PageSize = PageSize.A4, Margin = 36 });
scan.Save("scan.pdf");
```

More examples in [samples/LifeOS.Pdf.Samples](samples/LifeOS.Pdf.Samples/Program.cs).

## Build & test

Requires the .NET SDK 8 or newer (SDK 10 also builds the `net10.0` target).

```bash
dotnet build -c Release
dotnet test -c Release
dotnet run --project samples/LifeOS.Pdf.Samples -f net8.0 -- out/net8.0
```

External validation (Python 3.10+, development only — not needed to use the library):

```bash
pip install pikepdf pypdf pypdfium2 fonttools opentype-sanitizer
python tools/validate_pdf.py out/net8.0/showcase.pdf --render-dir out/png
```

## How it is developed

Each phase goes through **analyst → architect → developer → QA**, one phase per
release. Project rules are in [CLAUDE.md](CLAUDE.md), the agent roles in
[.claude/agents](.claude/agents), and the per-phase playbook and documents in
[docs/](docs/PHASES.md) (internal documents are written in Indonesian).

## License

[MIT](LICENSE). The code is written from the ISO 32000 specification and does
not copy from AGPL-licensed or proprietary libraries. See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
