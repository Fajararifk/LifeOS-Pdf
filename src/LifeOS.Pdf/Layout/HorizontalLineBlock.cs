using System;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>A horizontal rule across the column.</summary>
    public sealed class HorizontalLineBlock : Block
    {
        internal HorizontalLineBlock(PdfPen pen)
        {
            Pen = pen;
        }

        public PdfPen Pen { get; set; }

        public double SpaceBefore { get; set; } = 6;

        public double SpaceAfter { get; set; } = 6;
    }
}
