using System;
using System.IO;
using LifeOS.Pdf;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Ocr;
using LifeOS.Pdf.Rendering;
using LifeOS.Pdf.Scanning;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class OcrTests
    {
        private const string TessDataPath = @"C:\Users\Lenovo\AppData\Local\Temp\opencode\tessdata";

        private static bool TessDataAvailable => File.Exists(Path.Combine(TessDataPath, "eng.traineddata"));

        private static PdfDocument TextDocument()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage(PageSize.A4);
            page.Graphics.DrawText("Hello World", new PdfFont(StandardFont.Helvetica, 36), 72, 72);
            return doc;
        }

        [Fact]
        public void RecognizePdf_ReturnsWordsInPoints()
        {
            if (!TessDataAvailable)
            {
                return;
            }

            try
            {
                using var ocr = new OcrEngine(TessDataPath, "eng");
                var result = ocr.RecognizePdf(TextDocument(), 0, new OcrOptions { Dpi = 200 });

                Assert.Contains("Hello", result.Text, StringComparison.OrdinalIgnoreCase);
                Assert.NotEmpty(result.Words);
                // Positions are in points (page width ~595), not image pixels (~1654 at 200 DPI).
                Assert.InRange(result.Words[0].X, 0, 595);
                Assert.InRange(result.Words[0].Y, 0, 842);
            }
            catch (DllNotFoundException)
            {
            }
            catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
            {
            }
        }

        [Fact]
        public void MakeSearchable_AddsSearchableTextLayer()
        {
            if (!TessDataAvailable)
            {
                return;
            }

            try
            {
                // Render a text PDF to a PNG, then build an image-only "scanned" PDF.
                var png = new PdfRenderer().Render(TextDocument().ToArray(), 0, new RenderOptions { Dpi = 200 }).Png;
                var scanned = new PdfDocument();
                var page = scanned.AddPage(PageSize.A4);
                page.Graphics.DrawImage(PdfImage.FromBytes(png), 0, 0, page.Width, page.Height);
                Assert.DoesNotContain("Hello", scanned.ExtractText());

                using var ocr = new OcrEngine(TessDataPath, "eng");
                ocr.MakeSearchable(scanned, new OcrOptions { Dpi = 200 });

                PdfStructure.Verify(scanned.ToArray());
                Assert.Contains("Hello", scanned.ExtractText(), StringComparison.OrdinalIgnoreCase);
            }
            catch (DllNotFoundException)
            {
            }
            catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
            {
            }
        }

        [Fact]
        public void DocumentScanner_ScansAllPages()
        {
            if (!TessDataAvailable)
            {
                return;
            }

            try
            {
                var doc = new PdfDocument();
                doc.AddPage(PageSize.A4).Graphics.DrawText("Invoice", new PdfFont(StandardFont.Helvetica, 36), 72, 72);
                doc.AddPage(PageSize.A4).Graphics.DrawText("Terms", new PdfFont(StandardFont.Helvetica, 36), 72, 72);

                using var scanner = new DocumentScanner(TessDataPath, "eng");
                var pages = scanner.ScanAll(doc, new ScanOptions { Dpi = 200, Ocr = true, Barcode = false });

                Assert.Equal(2, pages.Count);
                Assert.Contains("Invoice", pages[0].Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Terms", pages[1].Text, StringComparison.OrdinalIgnoreCase);
            }
            catch (DllNotFoundException)
            {
            }
            catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
            {
            }
        }
    }
}
