using System.Collections.Generic;

namespace LifeOS.Pdf.Extraction
{
    /// <summary>Words on one baseline, left to right.</summary>
    public sealed class TextLine
    {
        internal TextLine(string text, PdfRect bounds, IReadOnlyList<TextWord> words)
        {
            Text = text;
            Bounds = bounds;
            Words = words;
        }

        /// <summary>The words joined by single spaces.</summary>
        public string Text { get; }

        public PdfRect Bounds { get; }

        public IReadOnlyList<TextWord> Words { get; }

        public override string ToString() => Text;
    }
}
