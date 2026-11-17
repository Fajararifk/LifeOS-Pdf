using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LifeOS.Pdf;
using LifeOS.Pdf.BarcodeScanning;
using LifeOS.Pdf.Barcodes;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Extraction;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Ocr;
using LifeOS.Pdf.Rendering;
using LifeOS.Pdf.Scanning;
using LifeOS.Pdf.Signing;
using LifeOS.Pdf.Templates;

namespace LifeOS.Pdf.Benchmarks
{
    /// <summary>
    /// Repeatable timing of typical workloads, one per phase. Each scenario runs a warm-up and
    /// then N measured iterations; the best time and the allocation of the best run are shown.
    /// Usage: dotnet run -c Release --project benchmarks/LifeOS.Pdf.Benchmarks [-- filter] [--quick]
    /// Phase-10 scenarios need the native PDFium library; the OCR ones also need tessdata
    /// (set LIFEOS_TESSDATA to its folder) and are skipped when it is absent.
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
            var imagePdf = ImageDoc(png, jpg, 300).ToArray();

            // Fixtures for phases 3 (CSV/XLSX) and 7–10, built once so that each scenario
            // measures only the operation named in its title.
            var csv = CsvText(5000);
            var table = TabularData.FromCsv(csv);
            var xlsx = XlsxBytes(2000);
            var fivePages = TextPages(5).ToArray();
            var threePages = TextPages(3).ToArray();
            var encrypted = Encrypt(report, PdfEncryptionAlgorithm.AES_128);
            var certificate = NewCertificate();
            var renderer = TryCreateRenderer();
            var ocr = TryCreateOcr();
            var scanner = TryCreateScanner();
            var qrPage = TryRender(renderer, QrDoc(40).ToArray(), 0, 200);
            var ocrPage = TryRender(renderer, fivePages, 0, 200);

            var scenarios = new List<(string Name, Func<object?> Run)>
            {
                ("F0 graphics: 200 pages × 120 text + shapes", () => GraphicsDoc(200).ToArray()),
                ("F0 write: 2000 pages small", () => ManyPages(2000).ToArray()),
                ("F1 TrueType: 100 pages Unicode text", () => TrueTypeDoc(noto, 100).ToArray()),
                ("F1 images: 300 PNG + 300 JPEG draws", () => ImageDoc(png, jpg, 300).ToArray()),
                ("F2 layout: 400 paragraphs + 1500-row table", () => FlowReport(400).Render().ToArray()),
                ("F2 layout: 3000 justified paragraphs", () => Paragraphs(3000).Render().ToArray()),
                ("F3 template: invoice with 1500 items", () => Invoice(1500)),
                ("F3 CSV parse: 5000 rows × 6 columns", () => TabularData.FromCsv(csv).Rows.Count),
                ("F3 table report: 5000-row CSV", () => BuiltInTemplates.TableReport.Render(table).ToArray()),
                ("F3 XLSX parse: 2000 rows × 5 columns", () => TabularData.FromXlsx(xlsx).Rows.Count),
                ("F4 QR: encode 2000 codes (mixed)", () => EncodeQr(2000)),
                ("F4 QR: 300 codes drawn in PDF", () => QrDoc(300).ToArray()),
                ("F5 navigation: TOC + 300 headings", () => Toc(300).Render().ToArray()),
                ("F6 open+save report (" + report.Length / 1024 + " KB)", () => PdfDocument.Open(report).ToArray()),
                ("F6 merge 10× report + watermark", () => Merge(report, 10)),
                ("F6 open 2000-page file (" + big.Length / 1024 + " KB)", () => PdfDocument.Open(big).Pages.Count),
                ("F6 open+save 2000-page file", () => PdfDocument.Open(big).ToArray()),
                ("F7 extract text + tables from report", () => { var d = PdfDocument.Open(report); var n = 0; foreach (var p in d.Pages) { n += p.ExtractText().Length + p.ExtractTables().Count; } return n; }),
                ("F7 PDF → HTML (" + report.Length / 1024 + " KB)", () => PdfTextConverter.ToHtml(PdfDocument.Open(report)).Length),
                ("F7 search report for \"Rp1.924.500\"", () => PdfDocument.Open(report).Search("Rp1.924.500").Count),
                ("F7 redact a value on every page", () => PdfRedactor.RedactText(PdfDocument.Open(report), "Rp1.924.500")),
                ("F7 images in a 600-image PDF", () => ImagesIn(imagePdf)),
                ("F8 forms: 500 fields fill + flatten", () => FormBytes(500, flatten: true)),
                ("F8 forms: 500 fields fill, keep editable", () => FormBytes(500, flatten: false)),
                ("F9 encrypt AES-128 (" + report.Length / 1024 + " KB)", () => Encrypt(report, PdfEncryptionAlgorithm.AES_128)),
                ("F9 encrypt RC4-128 (" + report.Length / 1024 + " KB)", () => Encrypt(report, PdfEncryptionAlgorithm.RC4_128)),
                ("F9 open AES-128 file (" + encrypted.Length / 1024 + " KB)", () => PdfDocument.Open(encrypted, new PdfReadOptions { Password = "secret" }).Pages.Count),
                ("F9 sign + verify a 1-page contract", () => SignAndVerify(certificate)),
                ("F10 render A4 @150 DPI", () => Render(renderer, report)),
                ("F10 render 10 pages @100 DPI", () => RenderMany(renderer, report, 10, 100)),
                ("F10 read 20 QR codes from a page", () => ReadQr(qrPage)),
                ("F10 OCR A4 text @200 DPI", () => OcrPage(ocr, ocrPage)),
                ("F10 make 3 pages searchable @150 DPI", () => Searchable(ocr, threePages)),
                ("F10 scan page: render + OCR @150 DPI", () => ScanPage(scanner, fivePages)),
            };

            Console.WriteLine("LifeOS.Pdf benchmarks — " + RuntimeName() + (quick ? " (quick)" : string.Empty));
            Console.WriteLine("fixtures: layout report " + PdfDocument.Open(report).Pages.Count + " pages (" + report.Length / 1024 + " KB), " +
                              "2000-page file " + big.Length / 1024 + " KB, 600-image PDF, 5000-row CSV, 2000-row XLSX");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-48} {1,10} {2,12} {3,10}", "scenario", "best ms", "alloc MB", "out KB"));
            foreach (var (name, run) in scenarios)
            {
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                object? result = null;
                var best = double.MaxValue;
                long bestAlloc = 0;
                try
                {
                    // Warm-up until the JIT has promoted hot methods to optimized code (tiering
                    // needs ~30 calls plus a background delay), at least 1 s or 30 runs.
                    result = run();
                    var warm = Stopwatch.StartNew();
                    for (var w = 0; w < 30 && warm.ElapsedMilliseconds < (quick ? 200 : 1000); w++)
                    {
                        result = run();
                    }

                    var iterations = quick ? 1 : 7;
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
                }
                catch (SkipException ex)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-48} {1,10}", name, "skipped"));
                    Console.WriteLine("    " + ex.Message);
                    continue;
                }
                catch (Exception ex) when (IsMissingNative(ex))
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-48} {1,10}", name, "skipped"));
                    Console.WriteLine("    native library not found on this machine");
                    continue;
                }

                var size = result is byte[] b ? (b.Length / 1024).ToString(CultureInfo.InvariantCulture) : "-";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-48} {1,10:0.0} {2,12:0.0} {3,10}", name, best, bestAlloc / 1048576.0, size));
            }

            ocr?.Dispose();
            scanner?.Dispose();
            certificate.Dispose();
            return 0;
        }

        private sealed class SkipException : Exception
        {
            public SkipException(string message)
                : base(message)
            {
            }
        }

        private static bool IsMissingNative(Exception? ex) =>
            ex is DllNotFoundException || ex is BadImageFormatException ||
            (ex is TypeInitializationException init && IsMissingNative(init.InnerException));

        private static PdfRenderer? TryCreateRenderer()
        {
            try
            {
                return new PdfRenderer();
            }
            catch (Exception ex) when (IsMissingNative(ex))
            {
                return null;
            }
        }

        private static string? TessData
        {
            get
            {
                var path = Environment.GetEnvironmentVariable("LIFEOS_TESSDATA");
                return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "eng.traineddata")) ? path : null;
            }
        }

        private static OcrEngine? TryCreateOcr()
        {
            var path = TessData;
            if (path == null)
            {
                return null;
            }

            try
            {
                return new OcrEngine(path, "eng");
            }
            catch (Exception ex) when (IsMissingNative(ex) || ex is Tesseract.TesseractException)
            {
                return null;
            }
        }

        private static DocumentScanner? TryCreateScanner()
        {
            var path = TessData;
            if (path == null)
            {
                return null;
            }

            try
            {
                return new DocumentScanner(path, "eng");
            }
            catch (Exception ex) when (IsMissingNative(ex) || ex is Tesseract.TesseractException)
            {
                return null;
            }
        }

        private static byte[]? TryRender(PdfRenderer? renderer, byte[] pdf, int page, int dpi)
        {
            if (renderer == null)
            {
                return null;
            }

            try
            {
                return renderer.Render(pdf, page, new RenderOptions { Dpi = dpi }).Png;
            }
            catch (Exception ex) when (IsMissingNative(ex))
            {
                return null;
            }
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

        // ------------------------------------------------------------------ phase 3 fixtures

        private static string CsvText(int rows)
        {
            var sb = new StringBuilder(rows * 64);
            sb.Append("Tanggal;No. Faktur;Pelanggan;Kategori;Jumlah;Status\n");
            for (var i = 0; i < rows; i++)
            {
                sb.Append("2026-11-").Append((i % 28 + 1).ToString("00", CultureInfo.InvariantCulture)).Append(';')
                  .Append("INV-2026-").Append((i + 1).ToString("0000", CultureInfo.InvariantCulture)).Append(';')
                  .Append("Pelanggan ").Append(i).Append(';')
                  .Append(i % 2 == 0 ? "Jasa" : "Barang").Append(';')
                  .Append(15000 + i).Append(';')
                  .Append(i % 3 == 0 ? "Lunas" : "Tempo").Append('\n');
            }

            return sb.ToString();
        }

        private static byte[] XlsxBytes(int rows)
        {
            const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var sheet = new StringBuilder(rows * 128);
            sheet.Append("<?xml version=\"1.0\"?><worksheet xmlns=\"").Append(Ns).Append("\"><sheetData><row r=\"1\">");
            foreach (var (column, title) in new[] { ("A", "Tanggal"), ("B", "Pelanggan"), ("C", "Kategori"), ("D", "Jumlah"), ("E", "Status") })
            {
                sheet.Append("<c r=\"").Append(column).Append("1\" t=\"inlineStr\"><is><t>").Append(title).Append("</t></is></c>");
            }

            sheet.Append("</row>");
            for (var i = 0; i < rows; i++)
            {
                var r = i + 2;
                sheet.Append("<row r=\"").Append(r).Append("\">")
                     .Append("<c r=\"A").Append(r).Append("\" t=\"inlineStr\"><is><t>2026-11-")
                     .Append((i % 28 + 1).ToString("00", CultureInfo.InvariantCulture)).Append("</t></is></c>")
                     .Append("<c r=\"B").Append(r).Append("\" t=\"inlineStr\"><is><t>Pelanggan ").Append(i).Append("</t></is></c>")
                     .Append("<c r=\"C").Append(r).Append("\" t=\"inlineStr\"><is><t>").Append(i % 2 == 0 ? "Jasa" : "Barang").Append("</t></is></c>")
                     .Append("<c r=\"D").Append(r).Append("\"><v>").Append(15000 + i).Append("</v></c>")
                     .Append("<c r=\"E").Append(r).Append("\" t=\"inlineStr\"><is><t>").Append(i % 3 == 0 ? "Lunas" : "Tempo").Append("</t></is></c>")
                     .Append("</row>");
            }

            sheet.Append("</sheetData></worksheet>");

            var workbook = "<workbook xmlns=\"" + Ns + "\" xmlns:r=\"" + RelNs + "\"><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
            var rels = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                       "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>";

            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
                AddEntry(zip, "xl/workbook.xml", workbook);
                AddEntry(zip, "xl/_rels/workbook.xml.rels", rels);
            }

            return stream.ToArray();
        }

        private static void AddEntry(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        // ------------------------------------------------------------------ phases 7–10 fixtures

        /// <summary>A plain text document: OCR and render scenarios read its pages.</summary>
        private static PdfDocument TextPages(int pages)
        {
            var doc = NewDoc();
            var title = new PdfFont(StandardFont.HelveticaBold, 16);
            var body = new PdfFont(StandardFont.Helvetica, 11);
            for (var p = 0; p < pages; p++)
            {
                var g = doc.AddPage(PageSize.A4).Graphics;
                g.DrawText("Laporan Keuangan November 2026", title, 56, 56);
                for (var i = 0; i < 30; i++)
                {
                    g.DrawText("Baris " + (i + 1) + ": total pengeluaran Rp1.924.500 dan pemasukan Rp2.450.000.", body, 56, 96 + i * 16);
                }
            }

            return doc;
        }

        private static byte[] FormBytes(int fields, bool flatten)
        {
            var doc = NewDoc();
            var page = doc.AddPage(PageSize.A4);
            var label = new PdfFont(StandardFont.Helvetica, 8);
            for (var i = 0; i < fields; i++)
            {
                if (i > 0 && i % 40 == 0)
                {
                    page = doc.AddPage(PageSize.A4);
                }

                var x = 56 + i % 2 * 250;
                var y = 72 + i % 40 / 2 * 34;
                page.Graphics.DrawText("Field " + i, label, x, y - 10);
                if (i % 4 == 3)
                {
                    page.AddCheckBox("cb" + i, x, y, 14, 14);
                }
                else
                {
                    page.AddTextField("tf" + i, x, y, 210, 18);
                }
            }

            for (var i = 0; i < fields; i++)
            {
                if (i % 4 == 3)
                {
                    doc.Form.SetValue("cb" + i, true);
                }
                else
                {
                    doc.Form.SetValue("tf" + i, "Nilai " + i);
                }
            }

            if (flatten)
            {
                doc.Form.Flatten();
            }

            return doc.ToArray();
        }

        private static PdfDocument SignableDoc()
        {
            var doc = NewDoc();
            var page = doc.AddPage(PageSize.A4);
            page.Graphics.DrawText("Kontrak Kerja Sama", new PdfFont(StandardFont.HelveticaBold, 18), 72, 72);
            page.Graphics.DrawText("Ditandatangani secara elektronik.", new PdfFont(StandardFont.Helvetica, 11), 72, 110);
            page.AddSignatureField("Ttd", 72, 690, 200, 50);
            return doc;
        }

        private static X509Certificate2 NewCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=LifeOS Benchmark", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return new X509Certificate2(created.Export(X509ContentType.Pfx));
        }

        private static byte[] SignAndVerify(X509Certificate2 certificate)
        {
            var signed = new PdfSigner(certificate) { Reason = "Benchmark", Location = "Jakarta" }.Sign(SignableDoc());
            if (!PdfSignatureValidator.Verify(signed).IsValid)
            {
                throw new InvalidOperationException("the signature did not verify");
            }

            return signed;
        }

        private static byte[] Encrypt(byte[] pdf, PdfEncryptionAlgorithm algorithm) =>
            PdfDocument.Open(pdf).ToArray(new PdfSaveOptions
            {
                Encryption = new PdfEncryptionOptions
                {
                    UserPassword = "secret",
                    OwnerPassword = "owner",
                    Algorithm = algorithm,
                },
            });

        private static int ImagesIn(byte[] pdf)
        {
            var total = 0;
            foreach (var page in PdfDocument.Open(pdf).Pages)
            {
                total += page.GetImages().Count;
            }

            return total;
        }

        private static byte[] Render(PdfRenderer? renderer, byte[]? pdf)
        {
            if (renderer == null || pdf == null)
            {
                throw new SkipException("native PDFium not found");
            }

            return renderer.Render(pdf, 0, new RenderOptions { Dpi = 150 }).Png;
        }

        private static int RenderMany(PdfRenderer? renderer, byte[] pdf, int pages, int dpi)
        {
            if (renderer == null)
            {
                throw new SkipException("native PDFium not found");
            }

            var total = 0;
            for (var i = 0; i < pages; i++)
            {
                total += renderer.Render(pdf, i, new RenderOptions { Dpi = dpi }).Png.Length;
            }

            return total;
        }

        private static int ReadQr(byte[]? png)
        {
            if (png == null)
            {
                throw new SkipException("native PDFium not found");
            }

            return new BarcodeReader().ReadAllPng(png).Count;
        }

        private static int OcrPage(OcrEngine? ocr, byte[]? png)
        {
            if (ocr == null || png == null)
            {
                throw new SkipException("set LIFEOS_TESSDATA to a folder with eng.traineddata");
            }

            return ocr.Recognize(png, new OcrOptions { Dpi = 200 }).Text.Length;
        }

        private static byte[] Searchable(OcrEngine? ocr, byte[] pdf)
        {
            if (ocr == null)
            {
                throw new SkipException("set LIFEOS_TESSDATA to a folder with eng.traineddata");
            }

            var doc = PdfDocument.Open(pdf);
            ocr.MakeSearchable(doc, new OcrOptions { Dpi = 150 });
            return doc.ToArray();
        }

        private static int ScanPage(DocumentScanner? scanner, byte[] pdf)
        {
            if (scanner == null)
            {
                throw new SkipException("set LIFEOS_TESSDATA to a folder with eng.traineddata");
            }

            var page = scanner.ScanPage(PdfDocument.Open(pdf), 1, new ScanOptions { Dpi = 150, Barcode = false, Ocr = true });
            return page.Text.Length;
        }
    }
}
