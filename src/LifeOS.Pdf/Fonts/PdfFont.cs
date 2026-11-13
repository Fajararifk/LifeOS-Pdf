using System;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Fonts
{
    /// <summary>A font face at a given size, used to draw and measure text.</summary>
    public sealed class PdfFont
    {
        /// <summary>One of the 14 standard fonts (no embedding, WinAnsi characters only).</summary>
        public PdfFont(StandardFont family, double size)
            : this(PdfFontFace.Standard(family), size)
        {
        }

        /// <summary>Any face, e.g. one loaded with <see cref="PdfFontFace.FromFile"/>.</summary>
        public PdfFont(PdfFontFace face, double size)
        {
            if (face == null)
            {
                throw new ArgumentNullException(nameof(face));
            }

            PdfFormat.RequireFinite(size, nameof(size));
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Font size must be greater than zero.");
            }

            Face = face;
            Size = size;
        }

        /// <summary>The typeface.</summary>
        public PdfFontFace Face { get; }

        /// <summary>Size in points.</summary>
        public double Size { get; }

        /// <summary>Distance from the top of the text line to the baseline, in points.</summary>
        public double Ascent => Face.Program.Ascent * Size / 1000.0;

        /// <summary>Distance from the baseline to the bottom of descenders, in points (positive).</summary>
        public double Descent => -Face.Program.Descent * Size / 1000.0;

        /// <summary>Recommended distance between two baselines (1.2 × size).</summary>
        public double LineHeight => Size * 1.2;

        /// <summary>Returns a copy of this font with a different size.</summary>
        public PdfFont WithSize(double size) => new PdfFont(Face, size);

        /// <summary>Width of <paramref name="text"/> in points when drawn with this font.</summary>
        public double MeasureWidth(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return Face.Program.Map(text).Width * Size / 1000.0;
        }

        internal TextRun Map(string text, bool kerning = false) => Face.Program.Map(text, kerning);
    }
}
