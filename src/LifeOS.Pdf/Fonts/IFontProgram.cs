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

        bool HasGlyph(int codePoint);

        /// <summary>Converts text to the bytes written in the content stream plus metrics.</summary>
        TextRun Map(string text);
    }

    /// <summary>The result of mapping a string onto a font.</summary>
    internal sealed class TextRun
    {
        public TextRun(byte[] codes, int[] glyphs, int[] codePoints, double width)
        {
            Codes = codes;
            Glyphs = glyphs;
            CodePoints = codePoints;
            Width = width;
        }

        /// <summary>Bytes for the PDF string operand (1 byte per glyph for simple fonts, 2 for Identity-H).</summary>
        public byte[] Codes { get; }

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
