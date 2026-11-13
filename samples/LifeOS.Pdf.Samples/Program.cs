using System;
using System.Globalization;
using System.IO;
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// Builds a two-page showcase of everything Fase 0 can do.
    /// Usage: dotnet run --project samples/LifeOS.Pdf.Samples -f net8.0 [output-folder]
    /// </summary>
    internal static class Program
    {
        private static readonly PdfColor Ink = PdfColor.FromHex("#1F2937");
        private static readonly PdfColor Muted = PdfColor.FromHex("#6B7280");
        private static readonly PdfColor Accent = PdfColor.FromHex("#0F766E");
        private static readonly PdfColor Rule = PdfColor.FromHex("#D1D5DB");
        private static readonly PdfColor Band = PdfColor.FromHex("#F3F4F6");

        private const double Margin = 56;

        private static int Main(string[] args)
        {
            var outputDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "output");
            Directory.CreateDirectory(outputDir);

            var doc = new PdfDocument();
            doc.Info.Title = "LifeOS.Pdf — Showcase Fase 0";
            doc.Info.Author = "LifeOS.Pdf";
            doc.Info.Subject = "Contoh semua fitur Core Writer v0.1";
            doc.Info.Creator = "LifeOS.Pdf.Samples (" + RuntimeName() + ")";

            ShowcasePage(doc);
            InvoicePage(doc);

            for (var i = 0; i < doc.Pages.Count; i++)
            {
                Footer(doc.Pages[i], i + 1, doc.Pages.Count);
            }

            var path = Path.Combine(outputDir, "showcase.pdf");
            doc.Save(path);
            Console.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes) on " + RuntimeName());
            return 0;
        }

        private static void ShowcasePage(PdfDocument doc)
        {
            var page = doc.AddPage(PageSize.A4);
            var g = page.Graphics;
            var width = page.Width - 2 * Margin;

            g.FillRectangle(Accent, 0, 0, page.Width, 8);
            g.DrawText("LifeOS.Pdf", new PdfFont(StandardFont.HelveticaBold, 28), Margin, 48, Ink);
            g.DrawText("Core Writer v0.1 — dibuat dari nol dengan C#, tanpa dependency.", new PdfFont(StandardFont.Helvetica, 11), Margin, 84, Muted);

            var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "landscape.jpg");
            var image = PdfImage.FromFile(imagePath);
            // 70% of the text width keeps the whole page above the footer.
            var imageWidth = width * 0.7;
            g.DrawImage(image, Margin, 112, imageWidth);
            var imageHeight = imageWidth * image.PixelHeight / image.PixelWidth;
            var y = 112 + imageHeight + 8;
            g.DrawText("Gambar JPEG " + image.PixelWidth + "×" + image.PixelHeight + " px, disisipkan apa adanya (DCTDecode).",
                new PdfFont(StandardFont.HelveticaOblique, 9), Margin, y, Muted);

            y += 32;
            Heading(g, "14 font standar", y);
            y += 24;
            var families = (StandardFont[])Enum.GetValues(typeof(StandardFont));
            var columnWidth = width / 2;
            for (var i = 0; i < families.Length; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var sampleText = families[i] == StandardFont.Symbol ? "abgdpqs 1234"
                    : families[i] == StandardFont.ZapfDingbats ? "3456789 nopq"
                    : families[i] + " — Rp1.350.000";
                g.DrawText(sampleText, new PdfFont(families[i], 11), Margin + col * columnWidth, y + row * 18, Ink);
            }

            y += 7 * 18 + 20;
            Heading(g, "Bentuk & warna", y);
            y += 26;
            g.DrawRectangle(new PdfPen(Ink, 1.5), Margin, y, 70, 50);
            g.DrawRectangle(new PdfPen(Accent, 1), PdfColor.FromHex("#CCFBF1"), Margin + 90, y, 70, 50);
            g.DrawRoundedRectangle(null, PdfColor.FromHex("#FDE68A"), Margin + 180, y, 70, 50, 12);
            g.DrawEllipse(new PdfPen(PdfColor.Red, 2), null, Margin + 270, y, 50, 50);
            g.DrawEllipse(null, PdfColor.Blue, Margin + 340, y + 5, 80, 40);
            g.DrawLine(new PdfPen(Muted, 1).WithDash(4, 3), Margin + 430, y, Margin + 480, y + 50);

            y += 74;
            Heading(g, "Paragraf dengan word-wrap", y);
            y += 24;
            const string paragraph =
                "LifeOS.Pdf adalah library PDF open-source untuk .NET yang ditulis dari nol. " +
                "Fase 0 mencakup halaman, teks, garis, kotak, elips, gambar JPEG, kompresi Flate, dan metadata dokumen. " +
                "File yang dihasilkan valid menurut ISO 32000 dan bisa dibuka di Chrome, Edge, Firefox, Adobe Acrobat, maupun PDF reader lain.";
            var body = new PdfFont(StandardFont.TimesRoman, 11);
            var columnGap = 20;
            var half = (width - columnGap) / 2;
            g.DrawTextBox(paragraph, body, Margin, y, half, Ink);
            g.DrawTextBox(paragraph, body, Margin + half + columnGap, y, half, Ink, TextAlign.Right);
        }

        private static void InvoicePage(PdfDocument doc)
        {
            var page = doc.AddPage(PageSize.A4);
            var g = page.Graphics;
            var width = page.Width - 2 * Margin;

            g.FillRectangle(Accent, 0, 0, page.Width, 8);
            g.DrawText("Ringkasan Pengeluaran", new PdfFont(StandardFont.HelveticaBold, 22), Margin, 48, Ink);
            g.DrawText("November 2026", new PdfFont(StandardFont.Helvetica, 11), Margin, 78, Muted);

            var rows = new[]
            {
                new { Date = "2026-11-01", Category = "Makan", Note = "Belanja mingguan", Amount = 450000L },
                new { Date = "2026-11-03", Category = "Transportasi", Note = "Isi saldo e-money", Amount = 200000L },
                new { Date = "2026-11-05", Category = "Internet", Note = "Langganan bulanan", Amount = 350000L },
                new { Date = "2026-11-07", Category = "Listrik", Note = "Token PLN", Amount = 300000L },
                new { Date = "2026-11-10", Category = "Kesehatan", Note = "Vitamin & obat", Amount = 125500L },
                new { Date = "2026-11-12", Category = "Pendidikan", Note = "Kursus online .NET", Amount = 499000L },
            };

            var top = 116.0;
            var rowHeight = 26.0;
            var cols = new[] { Margin, Margin + 90, Margin + 200, Margin + width };
            var header = new PdfFont(StandardFont.HelveticaBold, 10);
            var cell = new PdfFont(StandardFont.Helvetica, 10);
            var money = new PdfFont(StandardFont.Courier, 10);

            g.FillRectangle(Band, Margin, top, width, rowHeight);
            g.DrawText("Tanggal", header, cols[0] + 8, top + 8, Ink);
            g.DrawText("Kategori", header, cols[1] + 8, top + 8, Ink);
            g.DrawText("Catatan", header, cols[2] + 8, top + 8, Ink);
            g.DrawText("Nominal", header, cols[3] - 8, top + 8, Ink, TextAlign.Right);

            long total = 0;
            for (var i = 0; i < rows.Length; i++)
            {
                var y = top + (i + 1) * rowHeight;
                g.DrawText(rows[i].Date, cell, cols[0] + 8, y + 8, Ink);
                g.DrawText(rows[i].Category, cell, cols[1] + 8, y + 8, Ink);
                g.DrawText(rows[i].Note, cell, cols[2] + 8, y + 8, Muted);
                g.DrawText(Rupiah(rows[i].Amount), money, cols[3] - 8, y + 8, Ink, TextAlign.Right);
                g.DrawLine(new PdfPen(Rule, 0.5), Margin, y + rowHeight, Margin + width, y + rowHeight);
                total += rows[i].Amount;
            }

            var totalY = top + (rows.Length + 1) * rowHeight + 8;
            g.DrawLine(new PdfPen(Ink, 1), Margin + width - 200, totalY, Margin + width, totalY);
            g.DrawText("Total", header, Margin + width - 200, totalY + 10, Ink);
            g.DrawText(Rupiah(total), new PdfFont(StandardFont.CourierBold, 11), cols[3] - 8, totalY + 10, Accent, TextAlign.Right);

            g.DrawTextBox(
                "Tabel ini digambar manual dengan garis, kotak, dan teks rata kanan. " +
                "Layout engine (tabel otomatis, paragraf multi-halaman, header/footer) direncanakan di Fase 2.",
                new PdfFont(StandardFont.HelveticaOblique, 9), Margin, totalY + 60, width, Muted);
        }

        private static void Footer(PdfPage page, int number, int count)
        {
            var g = page.Graphics;
            var y = page.Height - 40;
            g.DrawLine(new PdfPen(Rule, 0.5), Margin, y - 8, page.Width - Margin, y - 8);
            var font = new PdfFont(StandardFont.Helvetica, 8);
            g.DrawText("LifeOS.Pdf · github.com/Fajararifk/LifeOS-Pdf", font, Margin, y, Muted);
            g.DrawText("Halaman " + number + " dari " + count, font, page.Width - Margin, y, Muted, TextAlign.Right);
        }

        private static void Heading(PdfGraphics g, string text, double y)
        {
            g.DrawText(text.ToUpperInvariant(), new PdfFont(StandardFont.HelveticaBold, 9), Margin, y, Accent);
        }

        /// <summary>Rp1.350.000 — whole rupiah, '.' as thousands separator.</summary>
        private static string Rupiah(long amount) =>
            "Rp" + amount.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');

        private static string RuntimeName() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    }
}
