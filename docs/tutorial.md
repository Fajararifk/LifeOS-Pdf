# LifeOS.Pdf — Step-by-step Tutorial

A hands-on guide to every feature in LifeOS.Pdf, from your first PDF to forms,
encryption, digital signatures and scanned documents. Each section is a small,
runnable example you can paste into a console app.

> **Getting started:** read [Installation](#1-installation) and
> [Your first PDF](#2-your-first-pdf-phase-0) first, then jump to the section you need.
> The API uses **top-left coordinates in points (1/72 inch)**, the same convention as
> most UI and report code.

## Contents

1. [Installation](#1-installation)
2. [Your first PDF (Phase 0)](#2-your-first-pdf-phase-0)
3. [Fonts, images & transforms (Phase 1)](#3-fonts-images--transforms-phase-1)
4. [Multi-page reports (Phase 2)](#4-multi-page-reports-phase-2)
5. [Report templates (Phase 3)](#5-report-templates-phase-3)
6. [QR codes & barcodes (Phase 4)](#6-qr-codes--barcodes-phase-4)
7. [Navigation & annotations (Phase 5)](#7-navigation--annotations-phase-5)
8. [Open, merge & edit existing PDFs (Phase 6)](#8-open-merge--edit-existing-pdfs-phase-6)
9. [Extract text, tables & images (Phase 7)](#9-extract-text-tables--images-phase-7)
10. [Forms (Phase 8)](#10-forms-phase-8)
11. [Encryption & digital signatures (Phase 9)](#11-encryption--digital-signatures-phase-9)
12. [Render, scan & OCR (Phase 10)](#12-render-scan--ocr-phase-10)
13. [Tips & troubleshooting](#13-tips--troubleshooting)

---

## 1. Installation

```bash
dotnet add package LifeOS.Pdf
```

The core package is **100% managed code with no dependencies** and runs on
.NET Standard 2.0 and .NET 5–10. The Phase 9/10 features are published as
**optional companion packages** installed separately:

```bash
dotnet add package LifeOS.Pdf.Signing          # digital signatures (.NET 8+)
dotnet add package LifeOS.Pdf.Rendering        # PDF → PNG (needs native PDFium)
dotnet add package LifeOS.Pdf.Barcode.Scanning # read QR/barcodes (zxing-cpp)
dotnet add package LifeOS.Pdf.Ocr              # OCR (needs native Tesseract + tessdata)
dotnet add package LifeOS.Pdf.Scanning         # combined render + barcode + OCR
```

Create a console project once and reuse it for the examples below:

```bash
dotnet new console -n LifeOsTutorial
cd LifeOsTutorial
dotnet add package LifeOS.Pdf
```

---

## 2. Your first PDF (Phase 0)

Create a document, add a page, draw text and shapes, and save it.

```csharp
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

var doc = new PdfDocument();
doc.Info.Title = "Hello LifeOS.Pdf";
doc.Info.Author = "Fajar";

var page = doc.AddPage(PageSize.A4);
var g = page.Graphics; // (0,0) is the top-left corner

g.DrawText("Hello world", new PdfFont(StandardFont.HelveticaBold, 24), 72, 72);
g.DrawTextBox("Long paragraphs wrap automatically to the width of the box.",
    new PdfFont(StandardFont.TimesRoman, 12), 72, 120, width: 360);
g.DrawRectangle(new PdfPen(PdfColor.Black, 1), 72, 200, 200, 80);
g.DrawRoundedRectangle(null, PdfColor.FromHex("#DBEAFE"), 72, 300, 200, 60, radius: 8);
g.DrawEllipse(new PdfPen(PdfColor.Red, 2), null, 300, 300, 60, 40);
g.DrawLine(new PdfPen(PdfColor.Gray, 1), 72, 400, 500, 400);

doc.Save("hello.pdf");
```

- **Page sizes:** `PageSize.A4`, `A3`, `A5`, `Letter`, `Legal`, or `new PageSize(w, h)`.
- **14 standard fonts** need no font files (`Helvetica`, `TimesRoman`, `Courier`, `Symbol`, `ZapfDingbats`, plus bold/italic variants).
- **Metadata:** `doc.Info.Title/Author/Subject/Keywords/Creator`.
- Output is deterministic: the same input produces the same bytes.

---

## 3. Fonts, images & transforms (Phase 1)

### TrueType fonts & full Unicode

```csharp
using LifeOS.Pdf.Fonts;

var noto = PdfFontFace.FromFile("NotoSans-Regular.ttf"); // .ttf or .ttc
g.DrawText("Tiếng Việt · Ελληνικά · Русский · 日本語",
    new PdfFont(noto, 12), 72, 460);

// Check the glyphs exist before drawing (missing ones would be blank):
if (noto.HasGlyph("日本語"))
    g.DrawText("日本語 OK", new PdfFont(noto, 12), 72, 480);
```

Fonts are **subset automatically** (only used glyphs are embedded) and text stays
selectable/searchable thanks to a generated ToUnicode map.

### Images (JPEG & transparent PNG)

```csharp
using LifeOS.Pdf.Images;

g.DrawImage(PdfImage.FromFile("photo.jpg"), 72, 520, width: 250); // keeps aspect ratio
g.DrawImage(PdfImage.FromFile("logo.png"), 400, 520, 120);        // transparency via soft mask
```

### Rotation, opacity & clipping

```csharp
using (g.Save())
{
    g.RotateAt(-35, page.Width / 2, page.Height / 2);
    g.SetOpacity(0.1);
    g.DrawText("DRAFT", new PdfFont(StandardFont.HelveticaBold, 120),
        page.Width / 2, page.Height / 2, align: TextAlign.Center);
}
```

### Image → PDF

```csharp
using LifeOS.Pdf.Conversion;

var scan = ImageToPdf.Convert(
    new[] { "page1.jpg", "page2.png" },
    new ImageToPdfOptions { PageSize = PageSize.A4, Margin = 36 });
scan.Save("scan.pdf");
```

---

## 4. Multi-page reports (Phase 2)

The layout engine paginates for you: describe the content, and it breaks lines,
creates pages, repeats table headers and fills in "Page X of Y".

```csharp
using LifeOS.Pdf.Layout;

var report = new FlowDocument();
report.Styles.Normal = new TextStyle
{
    Family = FontFamily.Helvetica,
    Size = 10,
    Fallbacks = { FontFamily.FromFiles("NotoSans-Regular.ttf") }, // non-Latin glyphs
};

var section = report.AddSection(new PageSetup { PageSize = PageSize.A4 });
section.Footer.AddParagraph()
    .AddText("Page ").AddPageNumber().AddText(" of ").AddPageCount()
    .Format.Alignment = ParagraphAlignment.Center;

section.AddHeading("Finance report — November 2026");

section.AddParagraph()
    .AddText("Total spending ")
    .AddText("Rp7.482.500", new TextStyle { Bold = true })
    .AddText(", down 12% from last month.");

var table = section.AddTable();
table.AddColumn(ColumnWidth.Auto);
table.AddColumn(ColumnWidth.Relative());
table.AddColumn(ColumnWidth.Fixed(90), ParagraphAlignment.Right);
table.HeaderRows = 1; // repeated on every page
table.AlternateRowBackground = PdfColor.FromHex("#F9FAFB");
table.AddRow("Date", "Note", "Amount");
foreach (var t in transactions)
    table.AddRow(t.Date, t.Note, t.Amount);

report.Save("report.pdf");
```

Also available: bulleted/numbered lists, column & row spans, images in the flow,
multiple columns, keep-with-next, page breaks, first-page-only header/footer.

---

## 5. Report templates (Phase 3)

Keep the layout in an XML template (editable without recompiling) and fill it with a
C# object or JSON. Five templates ship with the library: invoice, receipt
(*kwitansi*), payslip, monthly finance report and certificate.

```csharp
using LifeOS.Pdf.Templates;

// Built-in template + JSON data
BuiltInTemplates.Invoice
    .Render(TemplateData.FromJson(json))
    .Save("invoice.pdf");

// Your own template
var template = PdfTemplate.Load("templates/order.xml");
template.Render(new { Number = "ORD-17", Customer = customer, Items = items })
    .Save("order.pdf");
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
        <row>
          <cell>{{ item.Name }}</cell>
          <cell>{{ item.Quantity }}</cell>
          <cell>{{ item.Quantity * item.Price | rupiah }}</cell>
        </row>
      </foreach>
    </table>
    <p align="right"><b>Total {{ Items | sumproduct:"Quantity":"Price" | rupiah }}</b></p>
  </section>
</document>
```

Loops, conditions, grouping, sums, and Indonesian/English money, date and number-in-words
(*terbilang*) formatting are built in. Full reference: [docs/templates.md](templates.md).

---

## 6. QR codes & barcodes (Phase 4)

Encoded from scratch and drawn as vectors (sharp at any zoom), or exported as PNG/SVG.

```csharp
using LifeOS.Pdf.Barcodes;

g.DrawQrCode(QrCode.Encode("https://lifeos.app/inv/42"), 72, 620, size: 100);
g.DrawQrCode(QrCode.Encode(url, QrErrorCorrection.High), 200, 620, 100,
    logo: PdfImage.FromFile("logo.png"));

g.DrawBarcode(Barcode.Code128("INV-2026-0042"), 72, 740, width: 200, height: 50);
g.DrawBarcode(Barcode.Ean13("899123456789"), 300, 740, 160, 60); // check digit added

File.WriteAllBytes("qr.png", QrCode.Encode("hello").ToPng(moduleSize: 8));

// QRIS: turn a merchant's static QRIS into a dynamic one for an exact amount
var payload = Qris.WithAmount(staticQris, 125000);
```

Supported: QR (versions 1–40, ECC L/M/Q/H), Code 128, Code 39, EAN-13/8, UPC-A,
ITF/ITF-14. In the layout engine use `section.AddQrCode(...)`/`AddBarcode(...)`; in
templates use `<qrcode>`/`<barcode>`.

---

## 7. Navigation & annotations (Phase 5)

```csharp
using LifeOS.Pdf.Navigation;

// Layout engine: headings become bookmarks automatically
var section = doc.AddSection();
section.AddTableOfContents("Contents");        // dot leaders, page numbers, clickable
section.AddHeading("Summary").Anchor = "summary";
section.AddParagraph("See ").AddLink("our site", "https://lifeos.app")
    .AddText(" or ").AddLink("the summary", "#summary");
doc.Attachments.Add("data.csv", csvBytes, "text/csv");

// Low level
page.AddLink(72, 700, 120, 14, "https://lifeos.app");
page.AddLink(72, 720, 120, 14, PdfDestination.ToPage(otherPage, top: 100));
doc.Outline.Add("Chapter 1", PdfDestination.ToPage(doc.Pages[0]))
    .Add("1.1", PdfDestination.FitPage(doc.Pages[1]));
doc.PageLabels.Add(0, PageLabelStyle.LowerRoman); // i, ii, … then 1, 2, …
doc.ViewerPreferences.PageLayout = PageLayout.TwoPageRight;

// Annotations
page.AddNote(400, 80, "Check this number", author: "Fajar");
page.AddHighlight(72, 100, 200, 14, comment: "important");
page.AddStamp(350, 300, 160, 60, "PAID");
```

Links accept only `http`, `https`, `mailto` and `tel` (other schemes such as
`javascript:` are refused). In templates: `<a href>`, `anchor`, `<toc/>`,
`<attachment>`.

---

## 8. Open, merge & edit existing PDFs (Phase 6)

```csharp
using LifeOS.Pdf.Manipulation;

var doc = PdfDocument.Open("contract.pdf"); // PDF 1.0–1.7 / 2.0
doc.Pages[0].Graphics.DrawText("APPROVED", font, 400, 60, PdfColor.Red);
doc.Pages[2].Rotation = 90;
doc.RemovePage(5);
doc.Save("contract-signed.pdf");

var all = PdfMerger.Merge(new[] { "invoice.pdf", "receipt.pdf" }); // a bookmark per file
PdfStamper.AddTextWatermark(all, "COPY");
PdfStamper.AddPageNumbers(all, "Page {page} of {pages}");
PdfStamper.AddBatesNumbers(all, "LIFEOS-");
all.Save("all.pdf", new PdfSaveOptions { UseObjectStreams = true }); // smaller file

var parts = PdfSplitter.SplitEvery(PdfDocument.Open("big.pdf"), 10);
var twoUp = PdfImposition.NUp(doc, columns: 2, rows: 1);
var booklet = PdfImposition.Booklet(doc);
PdfImposition.Overlay(doc, PdfDocument.Open("letterhead.pdf").Pages[0]);
```

The parser handles cross-reference streams, object streams, incremental updates and
damaged files (the table is rebuilt), and is safe on untrusted input (nesting, stream
size, object count and page count are limited by `PdfReadOptions`).

---

## 9. Extract text, tables & images (Phase 7)

```csharp
using LifeOS.Pdf.Extraction;

var doc = PdfDocument.Open("statement.pdf");

string text = doc.ExtractText();                       // reading order, pages split by \f
string layout = doc.Pages[0].ExtractText(new TextExtractionOptions { Layout = true });

foreach (var word in doc.Pages[0].GetWords())          // text + box + font
    Console.WriteLine($"{word.Text} {word.Bounds} {word.FontName} {word.FontSize}");

foreach (var table in doc.Pages[0].ExtractTables())    // ruled or column-aligned
    File.WriteAllText("table.csv", table.ToCsv());     // also ToJson()

foreach (var image in doc.Pages[0].GetImages())        // JPEG as is, others → PNG
    image.Save("images/" + image.Name);

var hits = doc.Search("Rp1.924.500");                  // page + rectangles
File.WriteAllText("doc.html", PdfTextConverter.ToHtml(doc));

// True redaction: content is removed from the file, not just covered
PdfRedactor.RedactText(doc, "3174012345678901");
PdfRedactor.Redact(doc.Pages[0], new PdfRect(400, 520, 140, 140));
doc.Save("redacted.pdf");
```

Positions are in points with the origin at the top-left of the page as displayed.
Redaction removes glyphs, images, vector shapes and annotations inside the area,
rewrites form XObjects, and scrubs metadata and bookmarks.

---

## 10. Forms (Phase 8)

Create fillable forms, fill existing ones and flatten them into static content.

```csharp
using LifeOS.Pdf.Forms;

var doc = new PdfDocument();
var page = doc.AddPage(PageSize.A4);

page.AddTextField("Customer.Nama", 72, 72, 200, 20).Value = "Budi";
page.AddCheckBox("Setuju", 72, 110, 16, 16).Checked = true;
page.AddRadioButton("Gender", "Pria", 72, 140, 14, 14);
page.AddRadioButton("Gender", "Wanita", 130, 140, 14, 14);
page.AddComboBox("Kota", 72, 170, 150, 20, "Jakarta", "Bandung")
    .SelectedValues = new[] { "Bandung" };
page.AddButton("Reset", 72, 210, 90, 24, "Reset").Action = ButtonAction.Reset;
page.AddSignatureField("Ttd", 400, 700, 160, 40);
doc.Save("form.pdf");

// Fill an opened form, then bake it into static pages
var form = PdfDocument.Open("form.pdf");
form.Form.SetValue("Customer.Nama", "Siti");
form.Form.Flatten();
form.Save("flat.pdf");
```

Field types: text (multiline, password, comb, max length, alignment), checkbox,
radio group, combo/list, push button, signature placeholder. Appearance streams are
written for every state; `ExportJson`/`ImportJson` moves field data in and out.

---

## 11. Encryption & digital signatures (Phase 9)

### Password encryption (core package)

```csharp
using LifeOS.Pdf;

doc.Save("encrypted.pdf", new PdfSaveOptions
{
    Encryption = new PdfEncryptionOptions
    {
        UserPassword = "secret",   // required to open
        OwnerPassword = "owner",   // full access
        Algorithm = PdfEncryptionAlgorithm.AES_128, // or RC4_128
    },
});

var opened = PdfDocument.Open("encrypted.pdf",
    new PdfReadOptions { Password = "secret" });
opened.Save("decrypted.pdf"); // save without Encryption to remove the password
```

### Digital signatures (`LifeOS.Pdf.Signing`, .NET 8+)

```csharp
using LifeOS.Pdf.Signing;
using System.Security.Cryptography.X509Certificates;

// .NET 8: new X509Certificate2("cert.pfx", "password")
// .NET 9+: X509CertificateLoader.LoadPkcs12FromFile("cert.pfx", "password")
using var cert = new X509Certificate2("cert.pfx", "password");

page.AddSignatureField("Ttd", 400, 700, 160, 40);
var signed = new PdfSigner(cert) { Reason = "Approval", Location = "Jakarta" }
    .Sign(doc);

var result = PdfSignatureValidator.Verify(signed);
Console.WriteLine($"{result.IsValid} by {result.SignerName} (whole doc: {result.CoversWholeDocument})");
```

Signatures are CMS/PKCS#7 detached (PAdES B-B) covering the whole file via `/ByteRange`.
Timestamps (TSA), OCSP/CRL/LTV and multiple signatures are planned for a later phase.

---

## 12. Render, scan & OCR (Phase 10)

Three optional packages (rendering uses PDFium, OCR uses Tesseract; both need their
native runtime installed).

```csharp
using LifeOS.Pdf.Rendering;
using LifeOS.Pdf.BarcodeScanning;
using LifeOS.Pdf.Ocr;
using LifeOS.Pdf.Scanning;

// 1) Render a page to PNG at a chosen DPI (PDFium)
var image = new PdfRenderer().Render(doc.ToArray(), 0, new RenderOptions { Dpi = 200 });

// 2) Read every QR code / barcode on the page (zxing-cpp)
foreach (var b in new BarcodeReader().ReadAllPng(image.Png))
    Console.WriteLine($"[{b.Format}] {b.Text}");

// 3) OCR text from a scanned image (Tesseract; point to your tessdata folder)
using var ocr = new OcrEngine("/path/to/tessdata", "eng+ind");
string text = ocr.RecognizeText(image.Png);

// Or combine render + barcode + OCR in one call
using var scanner = new DocumentScanner("/path/to/tessdata", "eng");
var scanned = scanner.ScanPage(doc, 0);   // scanned.Text + scanned.Barcodes
```

Setup for the native runtime:
- **Rendering:** provide the native PDFium library for your platform (the `PDFiumCore`
  NuGet package has the bindings; copy `pdfium.dll` / `libpdfium.so` next to the app).
- **OCR:** download the `eng.traineddata` (and `ind.traineddata`) language files and
  pass the folder to `OcrEngine` / `DocumentScanner`.

---

## 13. Tips & troubleshooting

- **Culture:** never call `ToString()` on numbers in PDF content — the library does this
  for you. Format money with `TextFormats.Rupiah(...)` in templates.
- **Deterministic output:** the same input gives the same bytes (except `CreationDate`),
  which is great for caching and tests.
- **Untrusted input:** `PdfReadOptions` limits nesting, stream size, object count and
  pages, so a malicious PDF cannot hang the parser.
- **PDF validity:** every release is validated with qpdf, pypdf and PDFium (Chrome's
  engine), and embedded fonts with fontTools + OTS. Open your output in two viewers
  (e.g. Chrome and Acrobat) when in doubt.
- **Coordinate system:** public API uses **top-left origin, points**. The conversion to
  PDF's bottom-left system happens inside `PdfGraphics` only.
- **Large files:** `new PdfSaveOptions { UseObjectStreams = true }` packs dictionaries
  into object streams (smaller files, PDF 1.5+).

## Where to go next

- Full feature list and roadmap: [docs/PHASES.md](PHASES.md)
- Template language reference: [docs/templates.md](templates.md)
- Third-party licences: [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)
- Sample application with every feature: [samples/LifeOS.Pdf.Samples](../samples/LifeOS.Pdf.Samples/Program.cs)
