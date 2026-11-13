using System;
using System.IO;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Manipulation;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// Fase 6 showcase: opens the PDFs written by the other samples and edits them — merge with
    /// bookmarks, watermark, page and Bates numbers, split, 2-up, booklet, letterhead, compact save.
    /// </summary>
    internal static class ManipulationSample
    {
        public static void Run(string outputDir)
        {
            string In(string name) => Path.Combine(outputDir, name);

            var merged = PdfMerger.Merge(new[] { In("report.pdf"), In("template-invoice.pdf"), In("navigation.pdf") });
            merged.Info.Title = "LifeOS.Pdf — Gabungan";
            merged.Pages[merged.Pages.Count - 1].Rotation = 90; // stamps follow the displayed orientation
            PdfStamper.AddTextWatermark(merged, "SALINAN");
            PdfStamper.AddPageNumbers(merged, "Halaman {page} dari {pages}");
            PdfStamper.AddBatesNumbers(merged, "LIFEOS-");
            Save(merged, In("merged.pdf"));
            Save(merged, In("merged-compact.pdf"), new PdfSaveOptions { UseObjectStreams = true });

            var parts = PdfSplitter.SplitEvery(PdfDocument.Open(In("report.pdf")), 3);
            for (var i = 0; i < parts.Count; i++)
            {
                Save(parts[i], In("report-part" + (i + 1) + ".pdf"));
            }

            Save(PdfImposition.NUp(PdfDocument.Open(In("report.pdf")), 2, 1, border: new PdfPen(PdfColor.LightGray, 0.5)), In("report-2up.pdf"));
            Save(PdfImposition.Booklet(PdfDocument.Open(In("report.pdf"))), In("report-booklet.pdf"));

            // Letterhead drawn behind an existing invoice.
            var letterhead = new PdfDocument();
            var kop = letterhead.AddPage(PageSize.A4);
            kop.Graphics.FillRectangle(PdfColor.FromHex("#E6F4F1"), 0, 0, kop.Width, 34);
            kop.Graphics.DrawText("Fajar Digital Studio · Jakarta · fajardigital.id", new PdfFont(StandardFont.Helvetica, 8), kop.Width / 2, 12, PdfColor.FromHex("#0F766E"), TextAlign.Center);
            var invoice = PdfDocument.Open(In("template-invoice.pdf"));
            PdfImposition.Overlay(invoice, kop);
            Save(invoice, In("invoice-letterhead.pdf"));
        }

        private static void Save(PdfDocument doc, string path, PdfSaveOptions? options = null)
        {
            doc.Save(path, options);
            Console.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes)");
        }
    }
}
