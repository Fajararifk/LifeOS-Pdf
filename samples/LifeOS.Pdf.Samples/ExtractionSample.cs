using System;
using System.IO;
using LifeOS.Pdf.Extraction;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// Fase 7 showcase: reads the PDFs written by the other samples — text (reading order and
    /// layout), search, tables to CSV/JSON, images, HTML — and redacts a customer's data for real.
    /// </summary>
    internal static class ExtractionSample
    {
        public static void Run(string outputDir)
        {
            string In(string name) => Path.Combine(outputDir, name);
            var folder = Directory.CreateDirectory(In("extracted")).FullName;

            var invoice = PdfDocument.Open(In("template-invoice.pdf"));
            Write(Path.Combine(folder, "invoice.txt"), invoice.ExtractText());
            Write(Path.Combine(folder, "invoice-layout.txt"), invoice.ExtractText(new TextExtractionOptions { Layout = true }));
            foreach (var hit in invoice.Search("Rp24.031.500"))
            {
                Console.WriteLine("Found '" + hit.Text + "' on page " + (hit.PageIndex + 1) + " at " + hit.Bounds);
            }

            var report = PdfDocument.Open(In("report.pdf"));
            var n = 0;
            for (var p = 0; p < report.Pages.Count; p++)
            {
                foreach (var table in report.Pages[p].ExtractTables())
                {
                    n++;
                    Write(Path.Combine(folder, "report-table" + n + ".csv"), table.ToCsv());
                    Write(Path.Combine(folder, "report-table" + n + ".json"), table.ToJson());
                }
            }

            Write(Path.Combine(folder, "report.html"), PdfTextConverter.ToHtml(report));

            foreach (var image in PdfDocument.Open(In("showcase.pdf")).Pages[0].GetImages())
            {
                Console.WriteLine("Wrote " + image.Save(Path.Combine(folder, "showcase-" + image.Name)) + " (" + image.PixelWidth + "×" + image.PixelHeight + ")");
            }

            // Real redaction: the customer's name and the payment QR code are removed from the file.
            var redacted = PdfDocument.Open(In("template-invoice.pdf"));
            PdfRedactor.RedactText(redacted, "PT Maju Bersama Sejahtera");
            PdfRedactor.RedactText(redacted, "finance@majubersama.co.id");
            // The QR code sits at the right of the "PEMBAYARAN" block: redact that part of the page.
            var page = redacted.Pages[0];
            var anchor = redacted.Search("PEMBAYARAN", ignoreCase: false)[0].Bounds;
            PdfRedactor.Redact(page, new PdfRect(page.Width * 0.7, anchor.Y - 20, page.Width * 0.3 - 20, 130));
            redacted.Save(In("invoice-redacted.pdf"));
            Console.WriteLine("Wrote " + In("invoice-redacted.pdf"));
        }

        private static void Write(string path, string text)
        {
            File.WriteAllText(path, text);
            Console.WriteLine("Wrote " + path + " (" + text.Length + " chars)");
        }
    }
}
