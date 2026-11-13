# LifeOS.Pdf

Library PDF open-source untuk .NET, **ditulis dari nol** dengan C#, tanpa
dependency. Jalan di **.NET Standard 2.0, .NET 5, 6, 7, 8, 9, dan 10**
(dan .NET Framework 4.6.1+ lewat netstandard2.0).

File yang dihasilkan divalidasi dengan qpdf, pypdf, dan PDFium (engine di
balik PDF viewer Chrome), dan font tertanamnya dengan fontTools serta OTS
(sanitizer font di Chrome/Firefox), jadi bisa dibuka di Chrome, Edge, Firefox,
Adobe Acrobat, maupun PDF reader lain.

<p align="center">
  <img src="docs/images/showcase-p1.png" width="380" alt="Halaman contoh: teks, 14 font standar, bentuk, gambar JPEG">
  <img src="docs/images/showcase-p3.png" width="380" alt="Halaman contoh: font Unicode tertanam, PNG transparan, rotasi dan watermark">
  <img src="docs/images/report-p1.png" width="380" alt="Laporan keuangan multi-halaman dari layout engine: paragraf justify, daftar, tabel dengan rowspan dan colspan">
</p>

## Status

| Fase | Isi | Status |
|---|---|---|
| 0 | Core writer: halaman, teks 14 font standar, bentuk, JPEG, kompresi, metadata | ✅ v0.1 |
| 1 | Font TrueType + Unicode, PNG transparan, gambar → PDF, rotasi & opacity | ✅ v0.2 |
| 2 | Layout engine: paragraf, tabel multi-halaman, header/footer, kerning, font fallback | ✅ v0.3 |
| 3 | Laporan dari template (invoice, slip gaji, laporan keuangan) | berikutnya |
| 4 | Buat QR & barcode (vektor, PNG, SVG) | rencana |
| 5 | Link, bookmark, lampiran, anotasi | rencana |
| 6 | Buka PDF, merge, split, watermark, kompres | rencana |
| 7 | Ekstrak teks, gambar, tabel (CSV) | rencana |
| 8 | Form: buat, isi, flatten | rencana |
| 9 | Enkripsi & tanda tangan digital | rencana |
| 10 | Render ke gambar, baca QR/barcode, OCR (package terpisah) | rencana |
| 11 | HTML → PDF (wkhtmltopdf & Chromium, package terpisah) | rencana |
| 12 | PDF/A, PDF/UA, gradien, layer, e-invoice | rencana |

Detail tiap fase dan pemetaan use case ada di [docs/PHASES.md](docs/PHASES.md).

## Contoh

```csharp
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;

var doc = new PdfDocument();
doc.Info.Title = "Laporan Bulanan";

var page = doc.AddPage(PageSize.A4);
var g = page.Graphics; // titik (0,0) di kiri-atas, satuan point (1/72 inch)

g.DrawText("Halo dunia", new PdfFont(StandardFont.HelveticaBold, 24), 72, 72);
g.DrawTextBox("Paragraf panjang otomatis dipotong per baris sesuai lebar kotak.",
    new PdfFont(StandardFont.TimesRoman, 12), 72, 120, width: 300);
g.DrawRoundedRectangle(new PdfPen(PdfColor.Blue, 1), PdfColor.FromHex("#DBEAFE"), 72, 200, 200, 60, radius: 8);
g.DrawText("Rp1.350.000", new PdfFont(StandardFont.Courier, 12), 272, 220, align: TextAlign.Right);
g.DrawImage(PdfImage.FromFile("foto.jpg"), 72, 300, width: 250); // tinggi mengikuti rasio

doc.Save("laporan.pdf");
```

### Font TrueType & Unicode, PNG, watermark

```csharp
var noto = PdfFontFace.FromFile("NotoSans-Regular.ttf");   // .ttf atau .ttc
g.DrawText("Tiếng Việt · Ελληνικά · Русский · 日本語*", new PdfFont(noto, 12), 72, 400);
// *selama font-nya punya glyph — cek dulu dengan noto.HasGlyph("日本語")

g.DrawImage(PdfImage.FromFile("logo.png"), 400, 40, 120);   // PNG transparan

using (g.Save())
{
    g.RotateAt(-35, page.Width / 2, page.Height / 2);
    g.SetOpacity(0.1);
    g.DrawText("DRAFT", new PdfFont(StandardFont.HelveticaBold, 120), page.Width / 2, page.Height / 2, align: TextAlign.Center);
}
```

Font di-*subset* otomatis (hanya glyph yang dipakai ikut tertanam) dan teks tetap
bisa di-copy serta dicari di viewer.

### Laporan multi-halaman (layout engine)

Tidak perlu menghitung koordinat: susun konten, library yang memotong baris,
membuat halaman baru, mengulang header tabel, dan mengisi "Halaman X dari Y".

```csharp
using LifeOS.Pdf.Layout;

var doc = new FlowDocument();
doc.Styles.Normal = new TextStyle { Family = FontFamily.Helvetica, Size = 10,
    Fallbacks = { FontFamily.FromFiles("NotoSans-Regular.ttf") } };   // untuk karakter non-Latin

var section = doc.AddSection(new PageSetup { PageSize = PageSize.A4 });
section.Footer.AddParagraph().AddText("Halaman ").AddPageNumber().AddText(" dari ").AddPageCount()
    .Format.Alignment = ParagraphAlignment.Center;

section.AddHeading("Laporan Keuangan November 2026");
section.AddParagraph()
    .AddText("Total pengeluaran ")
    .AddText("Rp7.482.500", new TextStyle { Bold = true })
    .AddText(", turun 12% dari bulan lalu.");

var table = section.AddTable();
table.AddColumn(ColumnWidth.Auto);
table.AddColumn(ColumnWidth.Relative());
table.AddColumn(ColumnWidth.Fixed(90), ParagraphAlignment.Right);
table.HeaderRows = 1;                                   // diulang di setiap halaman
table.AlternateRowBackground = PdfColor.FromHex("#F9FAFB");
table.AddRow("Tanggal", "Catatan", "Nominal");
foreach (var t in transaksi)
    table.AddRow(t.Tanggal, t.Catatan, t.Nominal);

doc.Save("laporan.pdf");
```

Juga tersedia: justify, daftar bullet/bernomor, colspan/rowspan, gambar dalam alur,
dua kolom, keep-with-next, page break, header/footer berbeda di halaman pertama.
Contoh lengkap: [ReportSample.cs](samples/LifeOS.Pdf.Samples/ReportSample.cs).

### Gambar → PDF

```csharp
using LifeOS.Pdf.Conversion;

var scan = ImageToPdf.Convert(new[] { "hal1.jpg", "hal2.png" },
    new ImageToPdfOptions { PageSize = PageSize.A4, Margin = 36 });
scan.Save("scan.pdf");
```

Contoh lengkap ada di [samples/LifeOS.Pdf.Samples](samples/LifeOS.Pdf.Samples/Program.cs).

## Build & test

Butuh .NET SDK 8 atau lebih baru (SDK 10 juga membangun target `net10.0`).

```bash
dotnet build -c Release
dotnet test -c Release
dotnet run --project samples/LifeOS.Pdf.Samples -f net8.0 -- out/net8.0
```

Validasi eksternal (Python 3.10+):

```bash
pip install pikepdf pypdf pypdfium2 fonttools opentype-sanitizer
python tools/validate_pdf.py out/net8.0/showcase.pdf --render-dir out/png
```

## Cara pengembangan

Repo ini memakai alur kerja yang sama dengan LifeOS:
**analyst → architect → developer → qa**, satu fase per rilis. Aturan proyek
ada di [CLAUDE.md](CLAUDE.md), peran agent di [.claude/agents](.claude/agents),
dan playbook tiap fase di [docs/PHASES.md](docs/PHASES.md).

## Lisensi

[MIT](LICENSE). Kode ditulis berdasarkan spesifikasi ISO 32000, tidak
menyalin dari library berlisensi AGPL atau proprietary. Lihat
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
