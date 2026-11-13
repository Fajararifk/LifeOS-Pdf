using System;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Fonts
{
    /// <summary>A font face at a given size, used to draw and measure text.</summary>
    public sealed class PdfFont
    {
        public PdfFont(StandardFont family, double size)
        {
            PdfFormat.RequireFinite(size, nameof(size));
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Font size must be greater than zero.");
            }

            Family = family;
            Size = size;
            Metrics = StandardFontMetrics.For(family);
        }

        /// <summary>Which of the 14 standard fonts this is.</summary>
        public StandardFont Family { get; }

        /// <summary>Size in points.</summary>
        public double Size { get; }

        /// <summary>Distance from the top of the text line to the baseline, in points.</summary>
        public double Ascent => Metrics.Ascent * Size / 1000.0;

        /// <summary>Distance from the baseline to the bottom of descenders, in points (positive).</summary>
        public double Descent => -Metrics.Descent * Size / 1000.0;

        /// <summary>Recommended distance between two baselines (1.2 × size).</summary>
        public double LineHeight => Size * 1.2;

        internal StandardFontMetrics Metrics { get; }

        /// <summary>Returns a copy of this font with a different size.</summary>
        public PdfFont WithSize(double size) => new PdfFont(Family, size);

        /// <summary>Width of <paramref name="text"/> in points when drawn with this font.</summary>
        public double MeasureWidth(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var units = 0;
            foreach (var b in Encode(text))
            {
                units += Metrics.GetWidth(b);
            }

            return units * Size / 1000.0;
        }

        internal byte[] Encode(string text) =>
            Metrics.UsesBuiltInEncoding ? WinAnsiEncoding.EncodeBuiltIn(text) : WinAnsiEncoding.Encode(text);
    }
}
