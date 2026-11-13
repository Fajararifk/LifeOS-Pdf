using LifeOS.Pdf.Barcodes;
using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Layout
{
    /// <summary>A QR code or linear barcode placed in the text flow.</summary>
    public sealed class BarcodeBlock : Block
    {
        internal BarcodeBlock(QrCode? qr, Barcode? barcode, double width, double height, ParagraphAlignment alignment)
        {
            QrCode = qr;
            Barcode = barcode;
            Width = width;
            Height = height;
            Alignment = alignment;
        }

        /// <summary>The QR code, or null for a linear barcode.</summary>
        public QrCode? QrCode { get; }

        /// <summary>The linear barcode, or null for a QR code.</summary>
        public Barcode? Barcode { get; }

        /// <summary>Width in points (limited to the column width).</summary>
        public double Width { get; set; }

        /// <summary>Height in points (for QR codes equal to the width).</summary>
        public double Height { get; set; }

        public ParagraphAlignment Alignment { get; set; }

        /// <summary>Print the human-readable text under a linear barcode.</summary>
        public bool ShowText { get; set; } = true;

        public PdfColor Color { get; set; } = PdfColor.Black;

        public double SpaceBefore { get; set; } = 6;

        public double SpaceAfter { get; set; } = 6;
    }
}
