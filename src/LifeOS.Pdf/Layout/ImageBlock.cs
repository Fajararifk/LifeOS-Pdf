using System;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>An image placed in the text flow.</summary>
    public sealed class ImageBlock : Block
    {
        internal ImageBlock(PdfImage image, double? width, ParagraphAlignment alignment)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            if (width.HasValue)
            {
                PdfFormat.RequireNonNegative(width.Value, nameof(width));
            }

            Width = width;
            Alignment = alignment;
        }

        public PdfImage Image { get; }

        /// <summary>Width in points; null = natural size (from the image DPI, 96 if unknown). Always limited to the column width.</summary>
        public double? Width { get; set; }

        public ParagraphAlignment Alignment { get; set; }

        public double SpaceBefore { get; set; } = 6;

        public double SpaceAfter { get; set; } = 6;

        public bool KeepWithNext { get; set; }
    }
}
