using System;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>Empty vertical space.</summary>
    public sealed class SpacerBlock : Block
    {
        internal SpacerBlock(double height)
        {
            PdfFormat.RequireNonNegative(height, nameof(height));
            Height = height;
        }

        public double Height { get; }
    }
}
