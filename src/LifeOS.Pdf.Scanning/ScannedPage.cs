using System.Collections.Generic;
using LifeOS.Pdf.BarcodeScanning;
using LifeOS.Pdf.Ocr;

namespace LifeOS.Pdf.Scanning
{
    /// <summary>Options for <see cref="DocumentScanner.ScanPage(PdfDocument, int, ScanOptions)"/>.</summary>
    public sealed class ScanOptions
    {
        public int Dpi { get; set; } = 200;

        public bool Barcode { get; set; } = true;

        public bool Ocr { get; set; } = true;
    }

    /// <summary>The combined result of scanning one page.</summary>
    public sealed class ScannedPage
    {
        public ScannedPage(int pageIndex, string text, IReadOnlyList<OcrWord> words, IReadOnlyList<BarcodeReadResult> barcodes)
        {
            PageIndex = pageIndex;
            Text = text;
            Words = words;
            Barcodes = barcodes;
        }

        public int PageIndex { get; }

        /// <summary>The OCR text (empty when OCR is disabled).</summary>
        public string Text { get; }

        /// <summary>OCR words with their boxes in points (empty when OCR is disabled).</summary>
        public IReadOnlyList<OcrWord> Words { get; }

        public IReadOnlyList<BarcodeReadResult> Barcodes { get; }
    }
}
