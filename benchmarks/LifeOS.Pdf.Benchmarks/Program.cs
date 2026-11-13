using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LifeOS.Pdf;
using LifeOS.Pdf.Barcodes;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Templates;

namespace LifeOS.Pdf.Benchmarks
{
    /// <summary>
    /// Repeatable timing of typical workloads, one per phase. Each scenario runs a warm-up and
    /// then N measured iterations; the best time and the allocation of the best run are shown.
    /// Usage: dotnet run -c Release --project benchmarks/LifeOS.Pdf.Benchmarks [-- filter] [--quick]
    /// </summary>
    internal static class Program
    {
        private static string Assets => Path.Combine(AppContext.BaseDirectory, "Assets");

        private static int Main(string[] args)
        {
            var quick = args.Contains("--quick");
            var filter = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            var noto = PdfFontFace.FromFile(Path.Combine(Assets, "NotoSans-sample.ttf"));
            var png = PdfImage.FromFile(Path.Combine(Assets, "badge.png"));
            var jpg = PdfImage.FromFile(Path.Combine(Assets, "landscape.jpg"));
            var report = FlowReport(400).Render().ToArray();
            var big = ManyPages(2000).ToArray();

            var scenarios = new List<(string Name, Func<object?> Run)>
            {
                ("F0 graphics: 200 pages × 120 text + shapes", () => GraphicsDoc(200).ToArray()),
                ("F0 write: 2000 pages small", () => ManyPages(2000).ToArray()),
                ("F1 TrueType: 100 pages Unicode text", () => TrueTypeDoc(noto, 100).ToArray()),
                ("F1 images: 300 PNG + 300 JPEG draws", () => ImageDoc(png, jpg, 300).ToArray()),
                ("F2 layout: 400 paragraphs + 1500-row table", () => FlowReport(400).Render().ToArray()),
                ("F2 layout: 3000 justified paragraphs", () => Paragraphs(3000).Render().ToArray()),
                ("F3 template: invoice with 1500 items", () => Invoice(1500)),
                ("F4 QR: encode 2000 codes (mixed)", () => EncodeQr(2000)),
                ("F4 QR: 300 codes drawn in PDF", () => QrDoc(300).ToArray()),
                ("F5 navigation: TOC + 300 headings", () => Toc(300).Render().ToArray()),
                ("F6 open+save report (" + report.Length / 1024 + " KB)", () => PdfDocument.Open(report).ToArray()),
                ("F6 merge 10× report + watermark", () => Merge(report, 10)),
                ("F6 open 2000-page file (" + big.Length / 1024 + " KB)", () => PdfDocument.Open(big).Pages.Count),
                ("F6 open+save 2000-page file", () => PdfDocument.Open(big).ToArray()),
                ("F7 extract text + tables from report", () => { var d = PdfDocument.Open(report); var n = 0; foreach (var p in d.Pages) { n += p.ExtractText().Length + p.ExtractTables().Count; } return n; }),
            };

            Console.WriteLine("LifeOS.Pdf benchmarks — " + RuntimeName() + (quick ? " (quick)" : string.Empty));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-48} {1,10} {2,12} {3,10}", "scenario", "best ms", "alloc MB", "out KB"));
            foreach (var (name, run) in scenarios)
            {
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // Warm-up until the JIT has promoted hot methods to optimized code (tiering
                // needs ~30 calls plus a background delay), at least 1 s or 30 runs.
                var result = run();
                var warm = Stopwatch.StartNew();
                for (var w = 0; w < 30 && warm.ElapsedMilliseconds < (quick ? 200 : 1000); w++)
                {
                    result = run();
                }

                var iterations = quick ? 1 : 7;
                var best = double.MaxValue;
                long bestAlloc = 0;
                for (var i = 0; i < iterations; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var sw = Stopwatch.StartNew();
                    result = run();
                    sw.Stop();
                    var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (sw.Elapsed.TotalMilliseconds < best)
                    {
                        best = sw.Elapsed.TotalMilliseconds;
                        bestAlloc = alloc;
                    }
                }

                var size = result is byte[] b ? (b.Length / 1024).ToString(CultureInfo.InvariantCulture) : "-";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-48} {1,10:0.0} {2,12:0.0} {3,10}", name, best, bestAlloc / 1048576.0, size));
            }

            return 0;
        }

        private static string RuntimeName() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        private static PdfDocument NewDoc()
        {
            var doc = new PdfDocument();
            doc.Info.CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return doc;
        }

        private static PdfDocument GraphicsDoc(int pages)
        {
            var doc = NewDoc();
            var font = new PdfFont(StandardFont.Helvetica, 9);
            var bold = new PdfFont(StandardFont.HelveticaBold, 9);
            var pen = new PdfPen(PdfColor.FromHex("#334155"), 0.5);
            for (var p = 0; p < pages; p++)
            {
                var g = doc.AddPage().Graphics;
                for (var i = 0; i < 60; i++)
                {
                    var y = 40 + i * 12.5;
                    g.DrawText("Transaksi " + i + " — Rp" + (i * 12345).ToString("N0", CultureInfo.InvariantCulture), font, 40, y);
                    g.DrawText((i * 1.5).ToString("0.00", CultureInfo.InvariantCulture), bold, 500, y, align: TextAlign.Right);
                    g.DrawLine(pen, 40, y + 3, 555, y + 3);
                    if (i % 6 == 0)
                    {
                        g.DrawRoundedRectangle(pen, PdfColor.FromHex("#F1F5F9"), 300, y - 8, 80, 10, 3);
                    }
                }
            }

            return doc;
        }

        private static PdfDocument ManyPages(int pages)
        {
            var doc = NewDoc();
            var font = new PdfFont(StandardFont.TimesRoman, 12);
            for (var p = 0; p < pages; p++)
            {
                doc.AddPage().Graphics.DrawText("Page " + (p + 1), font, 72, 72);
            }

            return doc;
        }

        private static PdfDocument TrueTypeDoc(PdfFontFace face, int pages)
        {
            var doc = NewDoc();
            var font = new PdfFont(face, 10);
            const string Text = "Xin chào thế giới · Καλημέρα κόσμε · Привет, мир · Selamat pagi dunia — Rp1.924.500";
            for (var p = 0; p < pages; p++)
            {
                var g = doc.AddPage().Graphics;
                for (var i = 0; i < 50; i++)
                {
                    g.DrawText(Text, font, 40, 40 + i * 15);
                }
            }

            return doc;
        }

        private static PdfDocument ImageDoc(PdfImage png, PdfImage jpg, int count)
        {
            var doc = NewDoc();
            PdfGraphics? g = null;
            for (var i = 0; i < count; i++)
            {
                if (i % 30 == 0)
                {
                    g = doc.AddPage().Graphics;
                }

                var y = 40 + (i % 30 / 5) * 120;
                var x = 40 + (i % 5) * 100;
                g!.DrawImage(i % 2 == 0 ? png : jpg, x, y, 90, 60);
                g.DrawImage(i % 2 == 0 ? jpg : png, x, y + 60, 90, 50);
            }

            return doc;
        }

        private static FlowDocument FlowReport(int paragraphs)
        {
            var doc = new FlowDocument();
            doc.Info.CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var s = doc.AddSection();
            s.Footer.AddParagraph().AddText("Halaman ").AddPageNumber().AddText(" dari ").AddPageCount();
            for (var i = 0; i < paragraphs; i++)
            {
                if (i % 40 == 0)
                {
                    s.AddHeading("Bagian " + (i / 40 + 1));
                }

                s.AddParagraph("Total pengeluaran bulan ini ").AddText("Rp1.924.500", new TextStyle { Bold = true })
                    .AddText(", turun 12% dibanding bulan lalu. Saving rate naik menjadi 31% karena pengeluaran makan berkurang.");
            }

            var t = s.AddTable();
            t.AddColumn(ColumnWidth.Auto);
            t.AddColumn(ColumnWidth.Relative(1));
            t.AddColumn(ColumnWidth.Auto, ParagraphAlignment.Right);
            t.AddColumn(ColumnWidth.Auto, ParagraphAlignment.Right);
            t.HeaderRows = 1;
            t.AddRow("No", "Keterangan", "Qty", "Jumlah");
            for (var i = 0; i < 1500; i++)
            {
                t.AddRow((i + 1).ToString(CultureInfo.InvariantCulture), "Pembelian bahan kebutuhan kantor nomor " + i, (i % 7 + 1).ToString(CultureInfo.InvariantCulture), "Rp" + (i * 1000).ToString("N0", CultureInfo.InvariantCulture));
            }

            return doc;
        }

        private static FlowDocument Paragraphs(int count)
        {
            var doc = new FlowDocument();
            doc.Info.CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var s = doc.AddSection();
            for (var i = 0; i < count; i++)
            {
                s.AddParagraph("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.")
                    .Format.Alignment = ParagraphAlignment.Justify;
            }

            return doc;
        }

        private static byte[] Invoice(int items)
        {
            var sb = new StringBuilder();
            sb.Append("{\"Number\":\"INV-1\",\"Date\":\"2026-11-13\",\"DueDate\":\"2026-11-27\",\"Seller\":{\"Name\":\"Fajar\",\"Address\":\"Jakarta\"},\"Customer\":{\"Name\":\"PT Maju\",\"Address\":\"Jakarta\"},\"TaxRate\":0.11,\"Items\":[");
            for (var i = 0; i < items; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"Name\":\"Item ").Append(i).Append("\",\"Description\":\"Keterangan barang\",\"Quantity\":").Append(i % 9 + 1).Append(",\"Unit\":\"pcs\",\"Price\":").Append(15000 + i).Append('}');
            }

            sb.Append("]}");
            return BuiltInTemplates.Invoice.Render(TemplateData.FromJson(sb.ToString())).ToArray();
        }

        private static object EncodeQr(int count)
        {
            var total = 0;
            for (var i = 0; i < count; i++)
            {
                var text = i % 3 == 0 ? "https://lifeos.app/inv/" + i : i % 3 == 1 ? (i * 7919L).ToString(CultureInfo.InvariantCulture).PadLeft(40, '1') : "INVOICE-" + i + " Rp" + i * 1000 + " · lunas";
                total += QrCode.Encode(text).Size;
            }

            return total;
        }

        private static PdfDocument QrDoc(int count)
        {
            var doc = NewDoc();
            PdfGraphics? g = null;
            for (var i = 0; i < count; i++)
            {
                if (i % 20 == 0)
                {
                    g = doc.AddPage().Graphics;
                }

                g!.DrawQrCode(QrCode.Encode("https://lifeos.app/inv/" + i), 40 + (i % 4) * 130, 40 + (i % 20 / 4) * 150, 120);
            }

            return doc;
        }

        private static FlowDocument Toc(int headings)
        {
            var doc = new FlowDocument();
            doc.Info.CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var s = doc.AddSection();
            s.AddTableOfContents();
            s.AddPageBreak();
            for (var i = 0; i < headings; i++)
            {
                s.AddHeading("Bab " + i, i % 3 + 1);
                s.AddParagraph("Isi bab " + i + ". Lihat ").AddLink("situs", "https://lifeos.app");
            }

            return doc;
        }

        private static byte[] Merge(byte[] source, int copies)
        {
            var merged = NewDoc();
            for (var i = 0; i < copies; i++)
            {
                merged.ImportPages(PdfDocument.Open(source));
            }

            var font = new PdfFont(StandardFont.HelveticaBold, 40);
            foreach (var page in merged.Pages)
            {
                page.Graphics.DrawText("SALINAN", font, page.Width / 2, page.Height / 2, PdfColor.Red, TextAlign.Center);
            }

            return merged.ToArray();
        }
    }
}
