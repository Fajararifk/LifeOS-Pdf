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

        /// <summary>Scans one page.</summary>
        public ScannedPage ScanPage(PdfDocument document, int pageIndex, ScanOptions? options = null) =>
            ScanPage(document.ToArray(), pageIndex, options ?? new ScanOptions());

        /// <summary>Scans every page, in order (serializes once).</summary>
        public IReadOnlyList<ScannedPage> ScanAll(PdfDocument document, ScanOptions? options = null)
        {
            options ??= new ScanOptions();
            var bytes = document.ToArray();
            var results = new List<ScannedPage>(document.Pages.Count);
            for (var i = 0; i < document.Pages.Count; i++)
            {
                results.Add(ScanPage(bytes, i, options));
            }

            return results;
        }

        private ScannedPage ScanPage(byte[] bytes, int pageIndex, ScanOptions options)
        {
            var image = _renderer.Render(bytes, pageIndex, new RenderOptions { Dpi = options.Dpi });

            var barcodes = new List<BarcodeReadResult>();
            if (options.Barcode)
            {
                barcodes.AddRange(_barcode.ReadAllPng(image.Png));
            }

            OcrPageResult? ocr = null;
            if (options.Ocr && _ocr != null)
            {
                ocr = _ocr.Recognize(image.Png, new OcrOptions { Dpi = options.Dpi });
            }

            return new ScannedPage(
                pageIndex,
                ocr?.Text ?? string.Empty,
                ocr?.Words ?? Array.Empty<OcrWord>(),
                barcodes);
        }

        public void Dispose() => _ocr?.Dispose();
    }
}
