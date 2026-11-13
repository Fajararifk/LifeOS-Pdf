using System;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>Horizontal alignment of lines in a paragraph.</summary>
    public enum ParagraphAlignment
    {
        Left,
        Center,
        Right,

        /// <summary>Both edges straight; the last line and lines ending in a line break stay left-aligned.</summary>
        Justify,
    }
}
