namespace LifeOS.Pdf.Fonts.Standard
{
    /// <summary>
    /// Metrics for the 14 standard fonts. The numbers live in
    /// StandardFontMetrics.Generated.cs (from Adobe's AFM files); this part holds the
    /// shape of an entry and a cache so each font is materialised once.
    /// </summary>
    internal static partial class StandardFontData
    {
        private static readonly Entry?[] Cache = new Entry?[14];
        private static readonly object Gate = new object();

        public static Entry Get(StandardFont font)
        {
            var index = (int)font;
            if (index < 0 || index >= Cache.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(font), font, "Unknown standard font.");
            }

            lock (Gate)
            {
                return Cache[index] ?? (Cache[index] = Create(font));
            }
        }

        internal sealed class Entry
        {
            public Entry(string baseFont, short ascent, short descent, short capHeight, double italicAngle, short stemV,
                bool fixedPitch, bool builtInEncoding, short[] bbox, short[] widths)
            {
                BaseFont = baseFont;
                Ascent = ascent;
                Descent = descent;
                CapHeight = capHeight;
                ItalicAngle = italicAngle;
                StemV = stemV;
                FixedPitch = fixedPitch;
                UsesBuiltInEncoding = builtInEncoding;
                BBox = bbox;
                Widths = widths;
            }

            /// <summary>PostScript name written to /BaseFont.</summary>
            public string BaseFont { get; }

            public short Ascent { get; }

            public short Descent { get; }

            public short CapHeight { get; }

            public double ItalicAngle { get; }

            public short StemV { get; }

            public bool FixedPitch { get; }

            /// <summary>True for Symbol/ZapfDingbats: no /Encoding /WinAnsiEncoding.</summary>
            public bool UsesBuiltInEncoding { get; }

            public short[] BBox { get; }

            /// <summary>Widths for codes 32–255 (index = code − 32). 0 = no glyph.</summary>
            public short[] Widths { get; }

            public int GetWidth(byte code) => code < 32 ? 0 : Widths[code - 32];
        }
    }
}
