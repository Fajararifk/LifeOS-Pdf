using System;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Drawing
{
    /// <summary>How lines and outlines are stroked.</summary>
    public sealed class PdfPen
    {
        public PdfPen(PdfColor color, double width = 1)
        {
            PdfFormat.RequireNonNegative(width, nameof(width));
            Color = color;
            Width = width;
        }

        public PdfColor Color { get; }

        /// <summary>Line width in points. 0 means the thinnest line the device can draw.</summary>
        public double Width { get; }

        /// <summary>Optional dash pattern in points, e.g. { 4, 2 } = 4 on, 2 off. Null = solid.</summary>
        public double[]? DashPattern { get; private set; }

        /// <summary>Returns a copy of this pen with a dash pattern.</summary>
        public PdfPen WithDash(params double[] pattern)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException(nameof(pattern));
            }

            foreach (var p in pattern)
            {
                PdfFormat.RequireNonNegative(p, nameof(pattern));
            }

            return new PdfPen(Color, Width) { DashPattern = (double[])pattern.Clone() };
        }
    }
}
