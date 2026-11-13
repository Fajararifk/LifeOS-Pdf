using System.Collections.Generic;
using LifeOS.Pdf.BarcodeScanning;

namespace LifeOS.Pdf.Scanning
{
    /// <summary>Options for <see cref="DocumentScanner.ScanPage"/>.</summary>
    public sealed class ScanOptions
    {
        public int Dpi { get; set; } = 200;

        public bool Barcode { get; set; } = true;

        public bool Ocr { get; set; } = true;
    }

    /// <summary>The combined result of scanning one page.</summary>
    public sealed class ScannedPage
    {
        public ScannedPage(int pageIndex, string text, IReadOnlyList<BarcodeReadResult> barcodes)
        {
            PageIndex = pageIndex;
            Text = text;
            Barcodes = barcodes;
        }

        public int PageIndex { get; }

        /// <summary>The OCR text (empty when OCR is disabled).</summary>
        public string Text { get; }

        public IReadOnlyList<BarcodeReadResult> Barcodes { get; }
    }
}
