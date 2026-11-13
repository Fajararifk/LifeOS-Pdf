# LifeOS.Pdf — Step-by-step Tutorial

A hands-on guide to **every feature** in LifeOS.Pdf, from your first PDF to forms,
encryption, digital signatures and scanned documents. Each section is a small,
runnable example you can paste into a console app.

> **Coordinate system:** the public API uses **top-left coordinates in points**
> (1/72 inch), the same convention as UI and report code. Blue colors are made with
> `PdfColor.FromHex("#2563EB")` or `new PdfColor(r, g, b)`.

## Contents

1. [Setup](#1-setup)
2. [Your first PDF (Phase 0)](#2-your-first-pdf-phase-0)
3. [Fonts, images & transforms (Phase 1)](#3-fonts-images--transforms-phase-1)
4. [Layout engine (Phase 2)](#4-layout-engine-phase-2)
5. [Report templates (Phase 3)](#5-report-templates-phase-3)
6. [QR codes & barcodes (Phase 4)](#6-qr-codes--barcodes-phase-4)
7. [Navigation & annotations (Phase 5)](#7-navigation--annotations-phase-5)
8. [Open, merge & edit (Phase 6)](#8-open-merge--edit-phase-6)
9. [Extract text, tables & images (Phase 7)](#9-extract-text-tables--images-phase-7)
10. [Forms (Phase 8)](#10-forms-phase-8)
11. [Encryption & signatures (Phase 9)](#11-encryption--signatures-phase-9)
12. [Render, scan & OCR (Phase 10)](#12-render-scan--ocr-phase-10)
13. [End-to-end example](#13-end-to-end-example)
14. [Error handling](#14-error-handling)
15. [FAQ & troubleshooting](#15-faq--troubleshooting)

---

## 1. Setup

```bash
dotnet new console -n LifeOsTutorial
cd LifeOsTutorial
dotnet add package LifeOS.Pdf
```

The core package is **100% managed** and runs on .NET Standard 2.0 and .NET 5–10.
Phase 9/10 features are published as **optional companion packages**:

```bash
dotnet add package LifeOS.Pdf.Signing          # digital signatures (.NET 8+)
dotnet add package LifeOS.Pdf.Rendering        # PDF → PNG (native PDFium)
dotnet add package LifeOS.Pdf.Barcode.Scanning # read QR/barcodes (native zxing-cpp)
dotnet add package LifeOS.Pdf.Ocr              # OCR (native Tesseract + tessdata)
dotnet add package LifeOS.Pdf.Scanning         # combined render + barcode + OCR
```

---

## 2. Your first PDF (Phase 0)

```csharp
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

var doc = new PdfDocument();
doc.Info.Title = "Hello LifeOS.Pdf";
doc.Info.Author = "Fajar";
doc.Info.Subject = "Tutorial";

var page = doc.AddPage(PageSize.A4);      // A3, A4, A5, Letter, Legal, or new PageSize(w, h)
var g = page.Graphics;                    // (0,0) is the top-left corner

g.DrawText("Hello world", new PdfFont(StandardFont.HelveticaBold, 24), 72, 72);

// Word-wrapped text box; returns the height it used
double used = g.DrawTextBox("Long paragraphs wrap automatically to the width of the box.",
    new PdfFont(StandardFont.TimesRoman, 12), 72, 120, width: 360);

// Shapes: outline, fill, rounded, ellipse, line
g.DrawRectangle(new PdfPen(PdfColor.Black, 1), 72, 200, 200, 80);
g.DrawRectangle(new PdfPen(PdfColor.Blue, 1), PdfColor.FromHex("#DBEAFE"), 72, 300, 200, 80);
g.DrawRoundedRectangle(null, PdfColor.FromHex("#FDE68A"), 300, 200, 160, 60, radius: 10);
g.DrawEllipse(new PdfPen(PdfColor.Red, 2), null, 300, 300, 60, 40);
g.DrawLine(new PdfPen(PdfColor.Gray, 1).WithDash(4, 3), 72, 420, 500, 420);

// Text alignment
g.DrawText("right aligned", new PdfFont(StandardFont.Helvetica, 12), 500, 460, align: TextAlign.Right);

doc.Save("hello.pdf");
```

- **Landscape:** `doc.AddPage(new PageSize(PageSize.A4.Height, PageSize.A4.Width))`.
- **Page rotation:** `page.Rotation = 90;`.
- **Save options:** `doc.Save("x.pdf", new PdfSaveOptions { UseObjectStreams = true })` for smaller files,
  or `CompressContent = false` to read the raw operators while debugging.

---

## 3. Fonts, images & transforms (Phase 1)

### TrueType fonts & full Unicode

```csharp
using LifeOS.Pdf.Fonts;

var noto = PdfFontFace.FromFile("NotoSans-Regular.ttf");      // .ttf or .ttc
var notoBold = PdfFontFace.FromFile("NotoSans-Bold.ttf");

g.DrawText("Tiếng Việt · Ελληνικά · Русский · 日本語",
    new PdfFont(noto, 12), 72, 520, PdfColor.Black);

// Check before drawing — characters without a glyph would be blank
if (noto.HasGlyph("日本語"))
    g.DrawText("日本語 OK", new PdfFont(noto, 12), 72, 540);

// Change only the size
var small = new PdfFont(noto, 9);
```

Fonts are **subset automatically** (only used glyphs are embedded) and text stays
selectable/searchable thanks to the generated ToUnicode map.

### Images (JPEG & transparent PNG)

```csharp
using LifeOS.Pdf.Images;

g.DrawImage(PdfImage.FromFile("photo.jpg"), 72, 560, width: 250); // keeps aspect ratio
g.DrawImage(PdfImage.FromFile("logo.png"), 400, 560, 120);        // transparency via soft mask

var image = PdfImage.FromFile("photo.jpg");
Console.WriteLine($"{image.PixelWidth}×{image.PixelHeight} {image.Format} alpha={image.HasAlpha} dpi={image.DpiX}");
```

### Rotation, scale, opacity & clipping

```csharp
using (g.Save())
{
    g.RotateAt(-35, page.Width / 2, page.Height / 2);
    g.SetOpacity(0.1);
    g.DrawText("DRAFT", new PdfFont(StandardFont.HelveticaBold, 120),
        page.Width / 2, page.Height / 2, align: TextAlign.Center);
}

using (g.Save())
{
    g.ClipRectangle(72, 620, 200, 80);          // clip to a box
    g.ScaleAt(2, 2, 72, 620);
    g.DrawText("clipped & scaled", new PdfFont(StandardFont.Helvetica, 10), 72, 640);
}
```

### Image → PDF

```csharp
using LifeOS.Pdf.Conversion;

var scan = ImageToPdf.Convert(
    new[] { "page1.jpg", "page2.png" },
    new ImageToPdfOptions
    {
        PageSize = PageSize.A4,
        Margin = 36,
        Fit = ImageFit.Contain,   // Contain or Cover
    });
scan.Save("scan.pdf");
```

---

## 4. Layout engine (Phase 2)

Describe the content; the engine breaks lines, creates pages, repeats table headers
and fills in "Page X of Y".

```csharp
using LifeOS.Pdf.Layout;

var report = new FlowDocument();
report.Styles.Normal = new TextStyle
{
    Family = FontFamily.Helvetica,
    Size = 10,
    Fallbacks = { FontFamily.FromFiles("NotoSans-Regular.ttf") }, // non-Latin glyphs
};

var section = report.AddSection(new PageSetup
{
    PageSize = PageSize.A4,
    Margins = Margins.FromMillimeters(20, 20, 20, 20),
});

// Header/footer with page fields
section.Header.AddParagraph().AddText("Monthly report").Format.Alignment = ParagraphAlignment.Right;
section.Footer.AddParagraph()
    .AddText("Page ").AddPageNumber().AddText(" of ").AddPageCount()
    .Format.Alignment = ParagraphAlignment.Center;

// First page may differ
section.FirstPageFooter.AddParagraph().AddText("Generated by LifeOS.Pdf");

section.AddHeading("Finance report — November 2026");

// Mixed styles in one paragraph
section.AddParagraph()
    .AddText("Total spending ")
    .AddText("Rp7.482.500", new TextStyle { Bold = true, Color = PdfColor.FromHex("#B91C1C") })
    .AddText(", down 12% from last month.");

// Lists
var list = section.AddList(ListKind.Bullet);
list.AddItem("Groceries");
list.AddItem("Transport");
list.AddList(ListKind.Number).AddItem("Nested item");

// Table
var table = section.AddTable();
table.AddColumn(ColumnWidth.Auto);
table.AddColumn(ColumnWidth.Relative());
table.AddColumn(ColumnWidth.Percent(20), ParagraphAlignment.Right);
table.HeaderRows = 1;                                   // repeated on every page
table.AlternateRowBackground = PdfColor.FromHex("#F9FAFB");
table.AddRow("Date", "Note", "Amount");
foreach (var t in transactions)
    table.AddRow(t.Date, t.Note, t.Amount);

section.AddPageBreak();
section.AddHorizontalLine();
section.AddImage(PdfImage.FromFile("chart.png"), width: 300);
section.AddQrCode("https://lifeos.app", size: 80);
section.AddBarcode(LifeOS.Pdf.Barcodes.Barcode.Code128("INV-1"));

report.Save("report.pdf");
```

Also: `keep-together`, `keep-with-next`, `AddTableOfContents(title, maxLevel)`,
multiple columns per section, and a `LayoutException` when content cannot fit.

---

## 5. Report templates (Phase 3)

Keep the layout in an XML template (editable without recompiling) and fill it with a
C# object or JSON. Five built-in templates ship: invoice, receipt (*kwitansi*),
payslip, monthly finance report and certificate.

```csharp
using LifeOS.Pdf.Templates;

// Built-in template from JSON
var invoice = BuiltInTemplates.Invoice.Render(TemplateData.FromJson(json));
invoice.Save("invoice.pdf");

// Your own template + a C# object
var template = PdfTemplate.Load("templates/order.xml");
template.Render(new { Number = "ORD-17", Customer = customer, Items = items })
    .Save("order.pdf");

// Errors carry the line and column
try { template.Render(new { }); }
catch (TemplateException ex) { Console.WriteLine(ex.Message); }
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
    <if test="Items | count &gt; 0">
      <p align="right"><b>Total {{ Items | sumproduct:"Quantity":"Price" | rupiah }}</b></p>
    </if>
  </section>
</document>
```

Filters: `rupiah`, `currency`, `number`, `percent`, `date`, `terbilang`, `words`,
`upper`, `lower`, `title`, `join`, `count`, `sum`, `avg`, `min`, `max`, `sort`,
`sortdesc`, `where`, `groupby`. Full reference: [templates.md](templates.md).

---

## 6. QR codes & barcodes (Phase 4)

Encoded from scratch and drawn as vectors (sharp at any zoom) or exported as PNG/SVG.

```csharp
using LifeOS.Pdf.Barcodes;

// QR — versions 1–40, error correction L/M/Q/H; optional logo and colors
var qr = QrCode.Encode("https://lifeos.app/inv/42", QrErrorCorrection.High);
g.DrawQrCode(qr, 72, 700, size: 100);
g.DrawQrCode("https://lifeos.app", 200, 700, 100, logo: PdfImage.FromFile("logo.png"));
g.DrawQrCode(QrCode.Encode("custom"), 330, 700, 100, dark: PdfColor.FromHex("#1E3A8A"));

// 1D barcodes
g.DrawBarcode(Barcode.Code128("INV-2026-0042"), 72, 820, width: 200, height: 50);
g.DrawBarcode(Barcode.Code39("LIFEOS-42X"), 300, 820, 200, 50);
g.DrawBarcode(Barcode.Ean13("899123456789"), 72, 900, 160, 60);  // check digit added
g.DrawBarcode(Barcode.UpcA("03600029145"), 260, 900, 160, 60);
g.DrawBarcode(Barcode.Itf14("8991234567890"), 450, 900, 180, 60);

// Export
File.WriteAllBytes("qr.png", qr.ToPng(moduleSize: 8));
File.WriteAllText("qr.svg", qr.ToSvg());

// QRIS: turn a merchant's static QRIS into a dynamic one for an exact amount
var payload = Qris.WithAmount(staticQris, 125000);
```

In the layout engine use `section.AddQrCode(...)` / `AddBarcode(...)`; in templates
use `<qrcode>` / `<barcode>`.

---

## 7. Navigation & annotations (Phase 5)

```csharp
using LifeOS.Pdf.Navigation;

// Links (only http, https, mailto and tel are accepted)
page.AddLink(72, 700, 120, 14, "https://lifeos.app");
page.AddLink(72, 720, 120, 14, PdfDestination.ToPage(otherPage, top: 100));
page.AddLink(72, 740, 120, 14, PdfDestination.Named("summary"));

// Bookmarks (outline)
var chapter = doc.Outline.Add("Chapter 1", PdfDestination.ToPage(doc.Pages[0]));
chapter.IsOpen = true;
chapter.Add("1.1", PdfDestination.FitPage(doc.Pages[1]));

// Named destinations, page labels, viewer preferences
doc.NamedDestinations["summary"] = PdfDestination.ToPage(doc.Pages[2]);
doc.PageLabels.Add(0, PageLabelStyle.LowerRoman);   // i, ii, … then 1, 2, …
doc.PageLabels.Add(2, PageLabelStyle.Decimal);
doc.ViewerPreferences.PageLayout = PageLayout.TwoPageRight;
doc.ViewerPreferences.PageMode = PageMode.Outlines;

// Attachments (embedded files)
doc.Attachments.Add("data.csv", csvBytes, "text/csv", description: "Raw data");

// Annotations (with their own appearance)
page.AddNote(400, 80, "Check this number", author: "Fajar");
page.AddHighlight(72, 100, 200, 14, comment: "important");
page.AddStamp(350, 300, 160, 60, "PAID");
```

In the layout engine, headings generate bookmarks automatically
(`FlowDocument.GenerateOutline`), and `AddTableOfContents` builds a clickable TOC.

---

## 8. Open, merge & edit (Phase 6)

```csharp
using LifeOS.Pdf.Manipulation;

// Open from a file, stream or bytes; draw on imported pages
var doc = PdfDocument.Open("contract.pdf");
doc.Pages[0].Graphics.DrawText("APPROVED", font, 400, 60, PdfColor.Red);
doc.Pages[2].Rotation = 90;
doc.RemovePage(5);
doc.InsertPage(0, PageSize.A4);
doc.MovePage(1, 3);
doc.Save("contract-signed.pdf");

// Copy pages between documents
var target = new PdfDocument();
target.ImportPages(doc);                 // all pages, keeping links/annotations
target.ImportPage(doc.Pages[0]);

// Merge (a bookmark per file) and append
var all = PdfMerger.Merge(new[] { "invoice.pdf", "receipt.pdf" });
PdfMerger.Append(all, PdfDocument.Open("appendix.pdf"), "Appendix");

// Split / extract
var parts = PdfSplitter.SplitEvery(all, 10);
var ranges = PdfSplitter.SplitByRanges(all, "1-3", "5-8");
var first = PdfSplitter.Extract(all, PageRange.Parse("1-2"));

// Watermark, page numbers, Bates numbers
PdfStamper.AddTextWatermark(all, "COPY");
PdfStamper.AddImageWatermark(all, PdfImage.FromFile("logo.png"));
PdfStamper.AddPageNumbers(all, "Page {page} of {pages}");
PdfStamper.AddBatesNumbers(all, "LIFEOS-", start: 1, digits: 6);

// Imposition
var twoUp = PdfImposition.NUp(all, columns: 2, rows: 1);
var booklet = PdfImposition.Booklet(all);
PdfImposition.Overlay(all, PdfDocument.Open("letterhead.pdf").Pages[0]); // behind the content

// Draw an existing page as a vector form XObject
page.Graphics.DrawPage(all.Pages[0], 40, 40, 250, 350);

// Smaller files
all.Save("all.pdf", new PdfSaveOptions { UseObjectStreams = true });
```

`PdfDocument.Open` reads PDF 1.0–1.7 / 2.0, cross-reference streams, object streams,
incremental updates and damaged files (rebuilding the table). `PdfReadOptions` limits
nesting, stream size, object count and pages for untrusted input.

---

## 9. Extract text, tables & images (Phase 7)

```csharp
using LifeOS.Pdf.Extraction;

var doc = PdfDocument.Open("statement.pdf");

// Text in reading order; pages separated by \f
string text = doc.ExtractText();

// Layout-preserving text, or text inside an area
string layout = doc.Pages[0].ExtractText(new TextExtractionOptions { Layout = true });
string area = doc.Pages[0].ExtractText(new TextExtractionOptions
{
    Area = new PdfRect(72, 72, 400, 200),
});

// Words and glyphs with positions and fonts
foreach (var word in doc.Pages[0].GetWords())
    Console.WriteLine($"{word.Text} {word.Bounds} {word.FontName} {word.FontSize}");
foreach (var glyph in doc.Pages[0].GetGlyphs())
    Console.WriteLine($"'{glyph.Text}' at {glyph.Bounds}");

// Search (page + rectangles)
foreach (var hit in doc.Search("Rp1.924.500"))
    Console.WriteLine($"page {hit.PageIndex}: {hit.Bounds}");

// Tables (ruled or column-aligned) → CSV/JSON
foreach (var table in doc.Pages[0].ExtractTables())
{
    File.WriteAllText("table.csv", table.ToCsv());
    File.WriteAllText("table.json", table.ToJson());
}

// Images: JPEG as is, others decoded to PNG
foreach (var image in doc.Pages[0].GetImages())
    image.Save("images/" + image.Name);

// Convert to text / simple HTML
File.WriteAllText("doc.txt", PdfTextConverter.ToText(doc));
File.WriteAllText("doc.html", PdfTextConverter.ToHtml(doc));

// True redaction: content is removed from the file, not covered
PdfRedactor.RedactText(doc, "3174012345678901");
PdfRedactor.Redact(doc.Pages[0], new PdfRect(400, 520, 140, 140));
doc.Save("redacted.pdf");
```

Positions are in points with the origin at the top-left of the page as displayed.
Redaction removes glyphs, images, vector shapes and annotations inside the area,
rewrites form XObjects, and scrubs metadata and bookmarks.

---

## 10. Forms (Phase 8)

```csharp
using LifeOS.Pdf.Forms;

var doc = new PdfDocument();
var page = doc.AddPage(PageSize.A4);

page.AddTextField("Customer.Nama", 72, 72, 200, 20).Value = "Budi";
page.AddTextField("Alamat", 72, 100, 300, 40).Multiline = true;
page.AddTextField("NIK", 72, 150, 200, 20).MaxLength = 16;

var agree = page.AddCheckBox("Setuju", 72, 180, 16, 16);
agree.Checked = true;

var gender = page.AddRadioButton("Gender", "Pria", 72, 210, 14, 14);
page.AddRadioButton("Gender", "Wanita", 130, 210, 14, 14);
gender.Value = "Pria";

page.AddComboBox("Kota", 72, 240, 200, 20, "Jakarta", "Bandung", "Surabaya")
    .SelectedValues = new[] { "Bandung" };
page.AddListBox("Minat", 72, 270, 200, 60, "Musik", "Olahraga", "Teknologi")
    .SelectedValues = new[] { "Teknologi" };
page.AddButton("Reset", 72, 340, 90, 24, "Reset").Action = ButtonAction.Reset;
page.AddSignatureField("Ttd", 400, 700, 160, 40);

doc.Save("form.pdf");

// Fill an opened form, then flatten it into static pages
var form = PdfDocument.Open("form.pdf");
form.Form.SetValue("Customer.Nama", "Siti");
form.Form.SetValue("Setuju", false);
form.Form.SetValue("Gender", "Wanita");
form.Form.Flatten();
form.Save("filled.pdf");

// Move field values as JSON
string json = form.Form.ExportJson();
form.Form.ImportJson(json);
```

Field types: text (multiline, password, comb, max length, alignment, colors),
checkbox (on-value, check style), radio group, combo/list (multi-select, editable),
push button, signature placeholder. Appearance streams are written for every state.

---

## 11. Encryption & signatures (Phase 9)

### Password encryption (core package)

```csharp
using LifeOS.Pdf;

doc.Save("encrypted.pdf", new PdfSaveOptions
{
    Encryption = new PdfEncryptionOptions
    {
        UserPassword = "secret",                       // required to open
        OwnerPassword = "owner",                       // full access
        Algorithm = PdfEncryptionAlgorithm.AES_128,    // or RC4_128
        Permissions = PdfPermissions.Print | PdfPermissions.Copy,
        EncryptMetadata = true,
    },
});

// Open with a password, then save without Encryption to remove it
var opened = PdfDocument.Open("encrypted.pdf", new PdfReadOptions { Password = "secret" });
Console.WriteLine(opened.IsEncrypted + " " + opened.Permissions);
opened.Save("decrypted.pdf");
```

Wrong password → `PdfEncryptedException`.

### Digital signatures (`LifeOS.Pdf.Signing`, .NET 8+)

```csharp
using LifeOS.Pdf.Signing;
using System.Security.Cryptography.X509Certificates;

// .NET 8: new X509Certificate2("cert.pfx", "password")
// .NET 9+: X509CertificateLoader.LoadPkcs12FromFile("cert.pfx", "password")
using var cert = new X509Certificate2("cert.pfx", "password");

page.AddSignatureField("Ttd", 400, 700, 160, 40);
var signed = new PdfSigner(cert) { Reason = "Approval", Location = "Jakarta" }.Sign(doc);

var result = PdfSignatureValidator.Verify(signed);
Console.WriteLine($"{result.IsValid} by {result.SignerName} (whole doc: {result.CoversWholeDocument})");
```

Signatures are CMS/PKCS#7 detached (PAdES B-B) covering the whole file via `/ByteRange`.
Timestamps (TSA), OCSP/CRL/LTV and multiple signatures are planned for a later phase.

---

## 12. Render, scan & OCR (Phase 10)

```csharp
using LifeOS.Pdf.Rendering;
using LifeOS.Pdf.BarcodeScanning;
using LifeOS.Pdf.Ocr;
using LifeOS.Pdf.Scanning;

// 1) Render a page to PNG at a chosen DPI (PDFium)
var renderer = new PdfRenderer();
var image = renderer.Render(doc.ToArray(), 0, new RenderOptions { Dpi = 200 });
var all = renderer.RenderAll(doc.ToArray(), new RenderOptions { Dpi = 150 });

// 2) Read every QR code / barcode on the page (zxing-cpp)
var reader = new BarcodeReader();
foreach (var b in reader.ReadAllPng(image.Png))
    Console.WriteLine($"[{b.Format}] {b.Text}");

// 3) OCR text from a scanned image (Tesseract; point to your tessdata folder)
using var ocr = new OcrEngine("/path/to/tessdata", "eng+ind");
OcrPageResult recognized = ocr.Recognize(image.Png);
foreach (var word in recognized.Words)
    Console.WriteLine($"{word.Text} at {word.X},{word.Y}");

// 4) Combined render + barcode + OCR in one call
using var scanner = new DocumentScanner("/path/to/tessdata", "eng");
var scanned = scanner.ScanPage(doc, 0, new ScanOptions { Dpi = 200, Barcode = true, Ocr = true });
Console.WriteLine(scanned.Text);
foreach (var b in scanned.Barcodes) Console.WriteLine($"[{b.Format}] {b.Text}");
```

Native setup:
- **Rendering:** provide the native PDFium library for your platform (copy it next to the app).
- **OCR:** download `eng.traineddata` (and `ind.traineddata`) and pass the folder.

---

## 13. End-to-end example

A complete invoice from scratch: header, table, totals, a QR code and a signature area.

```csharp
using LifeOS.Pdf;
using LifeOS.Pdf.Barcodes;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Layout;

var items = new[]
{
    new { Name = "Inspection", Qty = 1, Price = 56_000m },
    new { Name = "Service fee", Qty = 2, Price = 25_000m },
};

var invoice = new FlowDocument();
invoice.Styles.Normal = new TextStyle { Family = FontFamily.Helvetica, Size = 10 };
var section = invoice.AddSection(new PageSetup { PageSize = PageSize.A4 });

section.AddHeading("INVOICE");
section.AddParagraph().AddText("No. INV-2026-001 · 13 November 2026");

var table = section.AddTable();
table.AddColumn(ColumnWidth.Auto);
table.AddColumn(ColumnWidth.Auto, ParagraphAlignment.Right);
table.AddColumn(ColumnWidth.Fixed(100), ParagraphAlignment.Right);
table.HeaderRows = 1;
table.AddRow("Item", "Qty", "Amount");
foreach (var item in items)
    table.AddRow(item.Name, item.Qty.ToString(), $"Rp{item.Qty * item.Price:N0}");

var total = items.Sum(i => i.Qty * i.Price);
section.AddParagraph()
    .AddText("Total: ")
    .AddText($"Rp{total:N0}", new TextStyle { Bold = true, Size = 12 });

section.AddQrCode(QrCode.Encode("https://lifeos.app/inv/2026-001"), size: 90);
section.AddSpacer(20);
section.AddHorizontalLine();
section.AddParagraph().AddText("Thank you for your business.");

invoice.Save("invoice.pdf");
```

---

## 14. Error handling

The library never writes a broken PDF silently — it throws with an actionable message:

| Exception | When |
|---|---|
| `PdfReadException` | damaged/unreadable PDF |
| `PdfEncryptedException` | encrypted file without (or with the wrong) password |
| `PdfLimitException` | an `PdfReadOptions` limit was exceeded (untrusted input) |
| `LayoutException` | content does not fit (e.g. a table wider than the page) |
| `TemplateException` | template error, with line and column |
| `ArgumentException` / `ArgumentOutOfRangeException` | invalid public API input |

```csharp
try
{
    var doc = PdfDocument.Open("input.pdf", new PdfReadOptions { Password = "secret" });
}
catch (PdfEncryptedException)
{
    Console.WriteLine("Wrong password or unsupported encryption.");
}
catch (PdfLimitException ex)
{
    Console.WriteLine("Rejected by a safety limit: " + ex.Message);
}
```

---

## 15. FAQ & troubleshooting

**The output looks wrong in one viewer but fine in another.**
Open it in two viewers (e.g. Chrome and Acrobat). The test suite validates with qpdf,
pypdf and PDFium; the API writes explicit appearance streams so viewers agree.

**My non-Latin text is blank.**
The font has no glyph for those characters. Check with `face.HasGlyph("…")` and use a
font that covers the script (e.g. Noto Sans), or set a fallback in the layout engine.

**Money/number formatting is wrong.**
Never call `ToString()` for PDF content. Use `TextFormats.Rupiah(value)` in templates or
`value.ToString("#,0", CultureInfo.InvariantCulture)` in your own code.

**The file is too big.**
Use `new PdfSaveOptions { UseObjectStreams = true }`; images are already embedded
without re-encoding, and fonts are subset automatically.

**Output is not deterministic.**
Only `CreationDate` varies by default; set `doc.Info.CreationDate` to fix it. `/ID` is
derived from the content.

**QR scanning misses a dense code.**
Use `BarcodeReader.ReadAllPng` (zxing-cpp with `TryHarder`); if a page has several
codes, it returns all of them. For OCR of scanned pages, raise the render DPI
(e.g. 200–300) before `OcrEngine.Recognize`.

**How do I upgrade from another library?**
The API is coordinate-based (top-left, points) and fluent; see the [migration notes]
(https://github.com/Fajararifk/LifeOS-Pdf) or open an issue with a snippet.

## Where to go next

- Feature list and roadmap: [PHASES.md](PHASES.md)
- Template language: [templates.md](templates.md)
- Third-party licences: [../THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)
- Full sample app: [../samples/LifeOS.Pdf.Samples](../samples/LifeOS.Pdf.Samples/Program.cs)
