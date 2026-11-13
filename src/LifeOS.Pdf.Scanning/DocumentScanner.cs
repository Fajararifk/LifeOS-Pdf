using System;
using System.Collections.Generic;
using LifeOS.Pdf;
using LifeOS.Pdf.BarcodeScanning;
using LifeOS.Pdf.Ocr;
using LifeOS.Pdf.Rendering;

namespace LifeOS.Pdf.Scanning
{
    /// <summary>Renders a page and runs barcode + OCR on it in one call.</summary>
    public sealed class DocumentScanner : IDisposable
    {
        private readonly PdfRenderer _renderer = new PdfRenderer();
        private readonly BarcodeReader _barcode = new BarcodeReader();
        private readonly OcrEngine? _ocr;

        public DocumentScanner(string tessDataPath, string language = "eng")
        {
            _ocr = new OcrEngine(tessDataPath, language);
        }

        public ScannedPage ScanPage(PdfDocument document, int pageIndex, ScanOptions? options = null)
        {
            options ??= new ScanOptions();
            var image = _renderer.Render(document.ToArray(), pageIndex, new RenderOptions { Dpi = options.Dpi });

            var barcodes = new List<BarcodeReadResult>();
            if (options.Barcode)
            {
                var result = _barcode.ReadPng(image.Png);
                if (result != null)
                {
                    barcodes.Add(result);
                }
            }

            var text = string.Empty;
            if (options.Ocr && _ocr != null)
            {
                text = _ocr.RecognizeText(image.Png);
            }

            return new ScannedPage(pageIndex, text, barcodes);
        }

        public void Dispose() => _ocr?.Dispose();
    }
}
