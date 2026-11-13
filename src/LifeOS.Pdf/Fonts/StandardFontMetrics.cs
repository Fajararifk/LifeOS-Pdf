using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Fonts
{
    /// <summary>
    /// Glyph widths (1/1000 em) for the standard fonts, used to measure and align text.
    /// Source: Adobe Core14 AFM metrics for codes 32–126.
    ///
    /// Exact tables: Helvetica, Helvetica-Bold, Times-Roman, Times-Bold, Courier (all 600).
    /// Approximate (TODO Fase 1, import full AFM): Times-Italic / Times-BoldItalic reuse
    /// the upright widths; Symbol / ZapfDingbats use a fixed average; codes 128–255 fall
    /// back to the width of their unaccented base letter, or of 'n'.
    /// The approximation only affects measuring/alignment, never what the viewer draws.
    /// </summary>
    internal sealed class StandardFontMetrics
    {
        private static readonly short[] HelveticaWidths =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, // 32–47
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, // 48–63
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, // 64–79
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, // 80–95
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, // 96–111
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,      // 112–126
        };

        private static readonly short[] HelveticaBoldWidths =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
        };

        private static readonly short[] TimesRomanWidths =
        {
            250, 333, 408, 500, 500, 833, 778, 180, 333, 333, 500, 564, 250, 333, 250, 278,
            500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 278, 278, 564, 564, 564, 444,
            921, 722, 667, 667, 722, 611, 556, 722, 722, 333, 389, 722, 611, 889, 722, 722,
            556, 722, 667, 556, 611, 722, 722, 944, 722, 722, 611, 333, 278, 333, 469, 500,
            333, 444, 500, 444, 500, 444, 333, 500, 500, 278, 278, 500, 278, 778, 500, 500,
            500, 500, 333, 389, 278, 500, 500, 722, 500, 500, 444, 480, 200, 480, 541,
        };

        private static readonly short[] TimesBoldWidths =
        {
            250, 333, 555, 500, 500, 1000, 833, 278, 333, 333, 500, 570, 250, 333, 250, 278,
            500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 333, 333, 570, 570, 570, 500,
            930, 722, 667, 722, 722, 667, 611, 778, 778, 389, 500, 778, 667, 944, 722, 778,
            611, 778, 722, 556, 667, 722, 722, 1000, 722, 722, 667, 333, 278, 333, 581, 500,
            333, 500, 556, 444, 556, 444, 333, 500, 556, 278, 333, 556, 278, 833, 556, 500,
            556, 556, 444, 389, 333, 556, 500, 722, 500, 500, 444, 394, 220, 394, 520,
        };

        private readonly short[]? _widths;
        private readonly short _fixedWidth;

        private StandardFontMetrics(string baseFont, short[]? widths, short fixedWidth, short ascent, short descent, bool builtInEncoding)
        {
            BaseFont = baseFont;
            _widths = widths;
            _fixedWidth = fixedWidth;
            Ascent = ascent;
            Descent = descent;
            UsesBuiltInEncoding = builtInEncoding;
        }

        /// <summary>PostScript name written to /BaseFont.</summary>
        public string BaseFont { get; }

        /// <summary>Ascender height in 1/1000 em.</summary>
        public short Ascent { get; }

        /// <summary>Descender depth in 1/1000 em (negative).</summary>
        public short Descent { get; }

        /// <summary>True for Symbol/ZapfDingbats, which must not get /Encoding /WinAnsiEncoding.</summary>
        public bool UsesBuiltInEncoding { get; }

        public static StandardFontMetrics For(StandardFont font)
        {
            switch (font)
            {
                case StandardFont.Helvetica: return new StandardFontMetrics("Helvetica", HelveticaWidths, 0, 718, -207, false);
                case StandardFont.HelveticaOblique: return new StandardFontMetrics("Helvetica-Oblique", HelveticaWidths, 0, 718, -207, false);
                case StandardFont.HelveticaBold: return new StandardFontMetrics("Helvetica-Bold", HelveticaBoldWidths, 0, 718, -207, false);
                case StandardFont.HelveticaBoldOblique: return new StandardFontMetrics("Helvetica-BoldOblique", HelveticaBoldWidths, 0, 718, -207, false);
                case StandardFont.TimesRoman: return new StandardFontMetrics("Times-Roman", TimesRomanWidths, 0, 683, -217, false);
                case StandardFont.TimesItalic: return new StandardFontMetrics("Times-Italic", TimesRomanWidths, 0, 683, -217, false);
                case StandardFont.TimesBold: return new StandardFontMetrics("Times-Bold", TimesBoldWidths, 0, 683, -217, false);
                case StandardFont.TimesBoldItalic: return new StandardFontMetrics("Times-BoldItalic", TimesBoldWidths, 0, 683, -217, false);
                case StandardFont.Courier: return new StandardFontMetrics("Courier", null, 600, 629, -157, false);
                case StandardFont.CourierBold: return new StandardFontMetrics("Courier-Bold", null, 600, 629, -157, false);
                case StandardFont.CourierOblique: return new StandardFontMetrics("Courier-Oblique", null, 600, 629, -157, false);
                case StandardFont.CourierBoldOblique: return new StandardFontMetrics("Courier-BoldOblique", null, 600, 629, -157, false);
                case StandardFont.Symbol: return new StandardFontMetrics("Symbol", null, 600, 1010, -293, true);
                case StandardFont.ZapfDingbats: return new StandardFontMetrics("ZapfDingbats", null, 788, 820, -143, true);
                default: throw new System.ArgumentOutOfRangeException(nameof(font), font, "Unknown standard font.");
            }
        }

        /// <summary>Width of one encoded byte in 1/1000 em.</summary>
        public int GetWidth(byte code)
        {
            if (_widths == null)
            {
                return _fixedWidth;
            }

            if (code >= 32 && code <= 126)
            {
                return _widths[code - 32];
            }

            switch (code)
            {
                case 0x85: // …
                case 0x97: // —
                case 0x89: // ‰
                    return 1000;
                case 0xA0: // nbsp
                    return _widths[0];
                case 0x96: // –
                    return _widths['n' - 32];
            }

            if (code >= 0xC0)
            {
                // Accented Latin letters share the width of their base letter.
                var baseLetter = BaseLetter((char)code);
                if (baseLetter >= 32 && baseLetter <= 126)
                {
                    return _widths[baseLetter - 32];
                }
            }

            return _widths['n' - 32];
        }

        private static char BaseLetter(char c)
        {
            var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            var first = decomposed[0];
            return CharUnicodeInfo.GetUnicodeCategory(first) == UnicodeCategory.NonSpacingMark ? c : first;
        }
    }
}
