using System;
using System.Globalization;
using System.IO;
using LifeOS.Pdf;
using LifeOS.Pdf.Conversion;
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
            doc.Info.Title = "LifeOS.Pdf — Showcase";
            doc.Info.Author = "LifeOS.Pdf";
            doc.Info.Subject = "Contoh fitur Fase 0 dan Fase 1";
            doc.Info.Creator = "LifeOS.Pdf.Samples (" + RuntimeName() + ")";

            ShowcasePage(doc);
            InvoicePage(doc);
            UnicodePage(doc);

            for (var i = 0; i < doc.Pages.Count; i++)
            {
                Footer(doc.Pages[i], i + 1, doc.Pages.Count);
            }

            var path = Path.Combine(outputDir, "showcase.pdf");
            doc.Save(path);
            Console.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes) on " + RuntimeName());

            // Image → PDF conversion: one image per A4 page.
            var images = ImageToPdf.Convert(
                new[] { Asset("landscape.jpg"), Asset("badge.png") },
                new ImageToPdfOptions { PageSize = PageSize.A4, Margin = 36 });
            images.Info.Title = "LifeOS.Pdf — ImageToPdf";
            var imagesPath = Path.Combine(outputDir, "images.pdf");
            images.Save(imagesPath);
            Console.WriteLine("Wrote " + imagesPath + " (" + new FileInfo(imagesPath).Length + " bytes)");

            // Layout engine (Fase 2): multi-page report.
            var reportPath = Path.Combine(outputDir, "report.pdf");
            ReportSample.Build(Path.Combine(AppContext.BaseDirectory, "Assets")).Save(reportPath);
            Console.WriteLine("Wrote " + reportPath + " (" + new FileInfo(reportPath).Length + " bytes)");

            // Templates (Fase 3): every built-in template with sample JSON data.
            TemplatesSample.Run(Path.Combine(AppContext.BaseDirectory, "Assets"), outputDir);

            // QR & barcodes (Fase 4).
            BarcodesSample.Run(Path.Combine(AppContext.BaseDirectory, "Assets"), outputDir);

            // Navigation & interactive features (Fase 5).
            NavigationSample.Run(outputDir);

            // Opening and editing existing PDFs (Fase 6).
            ManipulationSample.Run(outputDir);

            // Reading content back: text, tables, images, HTML, redaction (Fase 7).
            ExtractionSample.Run(outputDir);

            // Interactive forms: create, fill and flatten (Fase 8).
            FormSample.Run(outputDir);

            // Encryption (Fase 9): password-protected output.
            var protectedDoc = new PdfDocument();
            var pp = protectedDoc.AddPage();
            pp.Graphics.DrawText("Dokumen terproteksi — password: rahasia",
                new PdfFont(StandardFont.HelveticaBold, 16), Margin, 72, Accent);
            var protectedPath = Path.Combine(outputDir, "encrypted.pdf");
            protectedDoc.Save(protectedPath, new PdfSaveOptions
            {
                Encryption = new PdfEncryptionOptions { UserPassword = "rahasia", OwnerPassword = "owner", Algorithm = PdfEncryptionAlgorithm.AES_128 },
            });
            Console.WriteLine("Wrote " + protectedPath + " (" + new FileInfo(protectedPath).Length + " bytes)");
            return 0;
        }

        private static void UnicodePage(PdfDocument doc)
        {
            var page = doc.AddPage(PageSize.A4);
            var g = page.Graphics;
            var width = page.Width - 2 * Margin;
            var noto = PdfFontFace.FromFile(Asset("NotoSans-sample.ttf"));

            g.FillRectangle(Accent, 0, 0, page.Width, 8);
            g.DrawText("Font tertanam & Unicode", new PdfFont(noto, 24), Margin, 48, Ink);
            g.DrawText("Noto Sans (SIL OFL) di-subset otomatis: hanya glyph yang dipakai ikut tertanam.",
                new PdfFont(noto, 10), Margin, 84, Muted);

            var y = 120.0;
            Heading(g, "Bahasa & aksara", y);
            y += 22;
            var lines = new[]
            {
                "Indonesia — Rp1.350.000 · tanggal 13 November 2026 · “kutip” dan ‘apostrof’",
                "Tiếng Việt — Xin chào thế giới, đồng ₫ và chữ có dấu ệ ữ ộ",
                "Ελληνικά — Καλημέρα κόσμε · αβγδεζηθ ΩΣΔ",
                "Русский — Привет, мир! Ёжик в тумане",
                "Simbol — € £ ¥ © ® ™ № ‰ ° ± × ÷ ½ ¾",
            };
            var body = new PdfFont(noto, 12);
            foreach (var line in lines)
            {
                g.DrawText(line, body, Margin, y, Ink);
                y += 20;
            }

            // Check before drawing: characters without a glyph would show as empty boxes.
            // (The check is printed with escaped code points, so no box appears here.)
            const string math = "→∑∞";
            y += g.DrawTextBox("noto.HasGlyph(\"\\u2192\\u2211\\u221E\") = " + noto.HasGlyph(math) +
                               " — Noto Sans tidak punya panah & simbol matematika; untuk itu pakai font lain, mis. Noto Sans Math.",
                new PdfFont(noto, 9), Margin, y, width, Muted) + 8;

            // Optional: a CJK TrueType font from Windows, if present (not shipped with the repo).
            var cjkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msgothic.ttc");
            if (File.Exists(cjkPath))
            {
                var gothic = PdfFontFace.FromFile(cjkPath);
                g.DrawText("日本語 — こんにちは世界（MS Gothic, font Windows lokal）", new PdfFont(gothic, 12), Margin, y, Ink);
                y += 20;
            }

            y += 16;
            Heading(g, "PNG transparan, rotasi & opacity", y);
            y += 24;

            // Stripes behind the badge make the transparency visible.
            for (var i = 0; i < 8; i++)
            {
                g.FillRectangle(i % 2 == 0 ? PdfColor.FromHex("#FDE68A") : PdfColor.FromHex("#BFDBFE"), Margin + i * 20, y, 20, 120);
            }

            g.DrawImage(PdfImage.FromFile(Asset("badge.png")), Margin + 20, y, 120);

            var cx = Margin + 300;
            var cy = y + 60;
            for (var angle = 0; angle < 360; angle += 45)
            {
                using (g.Save())
                {
                    g.RotateAt(angle, cx, cy);
                    g.SetOpacity(0.35 + angle / 720.0);
                    g.DrawText("  LifeOS.Pdf", new PdfFont(noto, 11), cx, cy - 6, Accent);
                }
            }

            y += 150;
            Heading(g, "Paragraf Unicode dengan word-wrap", y);
            y += 22;
            g.DrawTextBox(
                "Teks dengan font tertanam tetap bisa di-copy dan dicari di viewer karena setiap glyph punya peta ToUnicode. " +
                "Lebar huruf diambil langsung dari tabel hmtx font, sehingga rata kanan, tengah, dan word-wrap akurat: " +
                "Hà Nội, Αθήνα, Москва, dan Jakarta.",
                new PdfFont(noto, 11), Margin, y, width, Ink, TextAlign.Left, 1.4);

            // Diagonal watermark across the page.
            using (g.Save())
            {
                g.RotateAt(-35, page.Width / 2, page.Height / 2);
                g.SetOpacity(0.08);
                g.DrawText("CONTOH", new PdfFont(StandardFont.HelveticaBold, 120), page.Width / 2, page.Height / 2 - 60, Ink, TextAlign.Center);
            }
        }

        private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

        private static void ShowcasePage(PdfDocument doc)
        {
            var page = doc.AddPage(PageSize.A4);
            var g = page.Graphics;
            var width = page.Width - 2 * Margin;

            g.FillRectangle(Accent, 0, 0, page.Width, 8);
            g.DrawText("LifeOS.Pdf", new PdfFont(StandardFont.HelveticaBold, 28), Margin, 48, Ink);
            g.DrawText("v0.2 — dibuat dari nol dengan C#, tanpa dependency.", new PdfFont(StandardFont.Helvetica, 11), Margin, 84, Muted);

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
                "Fase 0 dan 1 mencakup halaman, teks, font TrueType tertanam, garis, kotak, elips, gambar JPEG & PNG, " +
                "rotasi, transparansi, kompresi Flate, dan metadata dokumen. " +
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
