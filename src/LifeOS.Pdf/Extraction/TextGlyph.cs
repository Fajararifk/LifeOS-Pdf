namespace LifeOS.Pdf.Extraction
{
    /// <summary>One character (or ligature) on a page, as displayed.</summary>
    public sealed class TextGlyph
    {
        internal TextGlyph(string text, PdfRect bounds, string fontName, double fontSize, bool bold, bool italic)
        {
            Text = text;
            Bounds = bounds;
            FontName = fontName;
            FontSize = fontSize;
            Bold = bold;
            Italic = italic;
        }

        /// <summary>Unicode text; usually one character, more for ligatures ("ﬁ" → "fi").</summary>
        public string Text { get; }

        /// <summary>Box on the page as displayed (top-left origin, points).</summary>
        public PdfRect Bounds { get; }

        /// <summary>Font name without the subset prefix, e.g. "Helvetica-Bold".</summary>
        public string FontName { get; }

        /// <summary>Effective size in points (font size × text and page scaling).</summary>
        public double FontSize { get; }

        public bool Bold { get; }

        public bool Italic { get; }

        /// <summary>Reading direction on the displayed page: 0 left→right, 1 top→bottom, 2 right→left (upside down), 3 bottom→top.</summary>
        internal int Orientation { get; set; }

        internal bool Invisible { get; set; }

        public override string ToString() => Text;
    }
}
