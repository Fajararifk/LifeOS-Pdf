using System.Collections.Generic;

namespace LifeOS.Pdf.Fonts
{
    /// <summary>
    /// A loaded font program (standard Type 1 metrics or a parsed TrueType file).
    /// All metrics are in 1/1000 em, the unit PDF uses for glyph space.
    /// </summary>
    internal interface IFontProgram
    {
        string PostScriptName { get; }

        string FamilyName { get; }

        bool IsBold { get; }

        bool IsItalic { get; }

        bool IsFixedPitch { get; }

        bool IsEmbedded { get; }

        int GlyphCount { get; }

        double Ascent { get; }

        /// <summary>Negative value below the baseline.</summary>
        double Descent { get; }

        double CapHeight { get; }

        /// <summary>Underline position below the baseline (negative), 1/1000 em.</summary>
        double UnderlinePosition { get; }

        double UnderlineThickness { get; }

        bool HasGlyph(int codePoint);

        /// <summary>Converts text to the bytes written in the content stream plus metrics.</summary>
        /// <param name="text">Text to map.</param>
        /// <param name="kerning">Apply pair kerning (TrueType fonts with kerning data).</param>
        TextRun Map(string text, bool kerning = false);
    }

    /// <summary>The result of mapping a string onto a font.</summary>
    internal sealed class TextRun
    {
        public TextRun(byte[] codes, int[] glyphs, int[] codePoints, double width)
            : this(codes, glyphs, codePoints, width, null)
        {
        }

        public TextRun(byte[] codes, int[] glyphs, int[] codePoints, double width, double[]? adjustments)
        {
            Codes = codes;
            Glyphs = glyphs;
            CodePoints = codePoints;
            Width = width;
            Adjustments = adjustments ?? new double[glyphs.Length];
            BytesPerGlyph = glyphs.Length == 0 ? 1 : codes.Length / glyphs.Length;
        }

        /// <summary>Bytes for the PDF string operand (1 byte per glyph for simple fonts, 2 for Identity-H).</summary>
        public byte[] Codes { get; }

        public int BytesPerGlyph { get; }

        /// <summary>
        /// Extra advance after each glyph in 1/1000 em (kerning; positive = wider), already
        /// included in <see cref="Width"/>. Written with the TJ operator.
        /// </summary>
        public double[] Adjustments { get; }

        public bool HasAdjustments
        {
            get
            {
                foreach (var a in Adjustments)
                {
                    if (a != 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Glyph IDs (TrueType) or character codes (standard fonts), parallel to <see cref="CodePoints"/>.</summary>
        public int[] Glyphs { get; }

        /// <summary>Unicode code point each glyph came from — used for ToUnicode.</summary>
        public int[] CodePoints { get; }

        /// <summary>Total advance width in 1/1000 em.</summary>
        public double Width { get; }
    }

    internal static class CodePointReader
    {
        public const int Replacement = 0xFFFD;

        /// <summary>
        /// Splits .NET UTF-16 text into Unicode code points. Surrogate pairs are joined,
        /// lone surrogates become U+FFFD, tab becomes a space and other control
        /// characters (no visible glyph) are dropped.
        /// </summary>
        public static List<int> Read(string text)
        {
            var result = new List<int>(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    result.Add(char.ConvertToUtf32(c, text[i + 1]));
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    result.Add(Replacement);
                }
                else if (c == '\t')
                {
                    result.Add(' ');
                }
                else if (c >= 0x20 && c != 0x7F)
                {
                    result.Add(c);
                }
            }

            return result;
        }
    }
}
