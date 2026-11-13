using System.Collections.Generic;

namespace LifeOS.Pdf.Extraction
{
    /// <summary>Glyphs without a gap between them.</summary>
    public sealed class TextWord
    {
        internal TextWord(string text, PdfRect bounds, IReadOnlyList<TextGlyph> glyphs)
        {
            Text = text;
            Bounds = bounds;
            Glyphs = glyphs;
        }

        public string Text { get; }

        public PdfRect Bounds { get; }

        public IReadOnlyList<TextGlyph> Glyphs { get; }

        /// <summary>Font of the first glyph.</summary>
        public string FontName => Glyphs[0].FontName;

        public double FontSize => Glyphs[0].FontSize;

        public bool Bold => Glyphs[0].Bold;

        public bool Italic => Glyphs[0].Italic;

        public override string ToString() => Text;
    }
}
