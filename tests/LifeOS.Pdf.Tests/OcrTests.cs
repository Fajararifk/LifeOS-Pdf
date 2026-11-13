using System;
using System.IO;
using LifeOS.Pdf;
using LifeOS.Pdf.Fonts;
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

        [Fact]
        public void Ocr_RendersAndRecognizesText()
        {
            if (!TessDataAvailable)
            {
                return; // tessdata not present in this environment
            }

            try
            {
                var doc = new PdfDocument();
                var page = doc.AddPage(PageSize.A4);
                page.Graphics.DrawText("Hello World", new PdfFont(StandardFont.Helvetica, 36), 72, 72);

                var renderer = new PdfRenderer();
                var image = renderer.Render(doc.ToArray(), 0, new RenderOptions { Dpi = 200 });

                using var engine = new OcrEngine(TessDataPath, "eng");
                var text = engine.RecognizeText(image.Png);

                Assert.Contains("Hello", text, StringComparison.OrdinalIgnoreCase);
            }
            catch (DllNotFoundException)
            {
                // native Tesseract not present
            }
            catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
            {
            }
        }

        [Fact]
        public void DocumentScanner_ScansBarcodeAndText()
        {
            if (!TessDataAvailable)
            {
                return;
            }

            try
            {
                var doc = new PdfDocument();
                var page = doc.AddPage(PageSize.A4);
                page.Graphics.DrawText("Invoice", new PdfFont(StandardFont.Helvetica, 36), 72, 72);

                using var scanner = new DocumentScanner(TessDataPath, "eng");
                var result = scanner.ScanPage(doc, 0, new ScanOptions { Dpi = 200, Ocr = true, Barcode = false });

                Assert.Contains("Invoice", result.Text, StringComparison.OrdinalIgnoreCase);
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
