using System.Collections.Generic;

namespace LifeOS.Pdf.Fonts.Standard
{
    /// <summary>One of the 14 standard fonts: not embedded, single-byte encoding.</summary>
    internal sealed class StandardFontProgram : IFontProgram
    {
        private readonly StandardFontData.Entry _data;

        public StandardFontProgram(StandardFont font)
        {
            Font = font;
            _data = StandardFontData.Get(font);
        }

        public StandardFont Font { get; }

        public StandardFontData.Entry Data => _data;

        public string PostScriptName => _data.BaseFont;

        public string FamilyName
        {
            get
            {
                var dash = _data.BaseFont.IndexOf('-');
                var family = dash < 0 ? _data.BaseFont : _data.BaseFont.Substring(0, dash);
                return family == "Times" ? "Times" : family;
            }
        }

        public bool IsBold => _data.BaseFont.Contains("Bold");

        public bool IsItalic => _data.BaseFont.Contains("Italic") || _data.BaseFont.Contains("Oblique");

        public bool IsFixedPitch => _data.FixedPitch;

        public bool IsEmbedded => false;

        public int GlyphCount
        {
            get
            {
                var n = 0;
                foreach (var w in _data.Widths)
                {
                    if (w > 0)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public double Ascent => _data.Ascent;

        public double Descent => _data.Descent;

        public double CapHeight => _data.CapHeight;

        // Adobe Core14 AFM values (UnderlinePosition −100 / UnderlineThickness 50 for these families).
        public double UnderlinePosition => -100;

        public double UnderlineThickness => 50;

        public bool HasGlyph(int codePoint) => TryEncode(codePoint, out var code) && _data.GetWidth(code) > 0;

        /// <summary>Standard fonts are not kerned (AFM kerning pairs are not bundled).</summary>
        public TextRun Map(string text, bool kerning = false)
        {
            var codePoints = CodePointReader.Read(text);
            var codes = new List<byte>(codePoints.Count);
            var glyphs = new List<int>(codePoints.Count);
            var mapped = new List<int>(codePoints.Count);
            double width = 0;
            foreach (var cp in codePoints)
            {
                if (!TryEncode(cp, out var code))
                {
                    if (_data.UsesBuiltInEncoding)
                    {
                        continue; // nothing sensible to substitute in a symbol font
                    }

                    code = WinAnsiEncoding.Replacement;
                }

                codes.Add(code);
                glyphs.Add(code);
                mapped.Add(cp);
                width += _data.GetWidth(code);
            }

            return new TextRun(codes.ToArray(), glyphs.ToArray(), mapped.ToArray(), width);
        }

        private bool TryEncode(int codePoint, out byte code)
        {
            if (_data.UsesBuiltInEncoding)
            {
                // Symbol/ZapfDingbats: the code point is the byte of the font's own encoding.
                if (codePoint >= 0x20 && codePoint <= 0xFF)
                {
                    code = (byte)codePoint;
                    return true;
                }

                code = 0;
                return false;
            }

            return WinAnsiEncoding.TryEncode(codePoint, out code);
        }
    }
}
