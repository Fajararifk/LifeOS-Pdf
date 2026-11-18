#if NET8_0_OR_GREATER
using System;
using System.Globalization;
using System.IO;
using System.Text;
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Html;
using LifeOS.Pdf.Manipulation;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// HTML → PDF (Fase 11) with an installed Chrome/Edge/Chromium. Skipped with a message when none is found,
    /// so the rest of the showcase still runs on machines without a browser.
    /// </summary>
    internal static class HtmlSample
    {
        public static void Run(string assetsDir, string outputDir)
        {
            try
            {
                using var html = new ChromiumConverter();

                // 1) A file with its own stylesheet: files next to invoice.html may be read, nothing else.
                var invoice = html.ConvertFile(Path.Combine(assetsDir, "html", "invoice.html"), new HtmlToPdfOptions
                {
                    Margins = new HtmlMargins(36, 40, 54, 40),
                    FooterHtml = "<div style='width:100%;text-align:center;color:#6B7280'>Halaman {page} dari {pages}</div>",
                    Author = "CV Contoh Kreatif",
                });
                Write(outputDir, "html-invoice.pdf", invoice);

                // 2) A generated report string, then combined with a LifeOS.Pdf cover page and a watermark.
                var report = html.ConvertHtml(ReportHtml(120), new HtmlToPdfOptions
                {
                    HeaderHtml = "<div style='width:100%;text-align:right;color:#6B7280'>Laporan Penjualan · {title}</div>",
                    FooterHtml = "<div style='width:100%;text-align:center;color:#6B7280'>{page} / {pages}</div>",
                    Margins = new HtmlMargins(54, 40, 54, 40),
                });

                var combined = new PdfDocument();
                combined.Info.Title = "LifeOS.Pdf — HTML report";
                var cover = combined.AddPage(PageSize.A4);
                cover.Graphics.FillRectangle(PdfColor.FromHex("#0F766E"), 0, 0, cover.Width, 8);
                cover.Graphics.DrawText("Laporan Penjualan 2026", new PdfFont(StandardFont.HelveticaBold, 28), 56, 120, PdfColor.FromHex("#1F2937"));
                cover.Graphics.DrawText("Sampul dibuat dengan LifeOS.Pdf, isi dari HTML (Chromium " + (html.BrowserVersion ?? "?") + ").",
                    new PdfFont(StandardFont.Helvetica, 11), 56, 160, PdfColor.FromHex("#6B7280"));
                PdfMerger.Append(combined, PdfDocument.Open(report), "Isi laporan");
                PdfStamper.AddTextWatermark(combined, "DRAFT");
                Write(outputDir, "html-report.pdf", combined.ToArray());
            }
            catch (HtmlConversionException ex) when (ex.Error == HtmlConversionError.BrowserNotFound || ex.Error == HtmlConversionError.LaunchFailed)
            {
                Console.WriteLine("Skipped HTML → PDF: " + ex.Message.Split('\n')[0]);
            }
        }

        private static string ReportHtml(int rows)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>Q4 2026</title><style>")
              .Append("body{font-family:'Segoe UI',Arial,sans-serif;color:#1F2937;font-size:10pt}")
              .Append("h1{color:#0F766E}table{width:100%;border-collapse:collapse}")
              .Append("th{background:#F3F4F6;text-align:left;padding:6px}td{padding:6px;border-bottom:1px solid #E5E7EB}")
              .Append(".n{text-align:right}thead{display:table-header-group}tr{break-inside:avoid}")
              .Append("</style></head><body><h1>Penjualan per transaksi</h1>")
              .Append("<p>Header tabel diulang otomatis di setiap halaman oleh browser.</p>")
              .Append("<table><thead><tr><th>No</th><th>Tanggal</th><th>Pelanggan</th><th class='n'>Jumlah</th></tr></thead><tbody>");
            var id = CultureInfo.GetCultureInfo("id-ID");
            for (var i = 1; i <= rows; i++)
            {
                var date = new DateTime(2026, 10, 1).AddDays(i % 90);
                sb.Append("<tr><td>").Append(i).Append("</td><td>").Append(date.ToString("d MMM yyyy", id))
                  .Append("</td><td>Pelanggan ").Append((char)('A' + i % 26))
                  .Append("</td><td class='n'>Rp").Append((i * 125_000 % 9_000_000 + 50_000).ToString("N0", id)).Append("</td></tr>");
            }

            return sb.Append("</tbody></table></body></html>").ToString();
        }

        private static void Write(string outputDir, string name, byte[] pdf)
        {
            var path = Path.Combine(outputDir, name);
            File.WriteAllBytes(path, pdf);
            Console.WriteLine("Wrote " + path + " (" + pdf.Length + " bytes)");
        }
    }
}
#endif
