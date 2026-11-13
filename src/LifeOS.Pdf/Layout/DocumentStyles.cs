using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Layout
{
    /// <summary>Document-wide default styles.</summary>
    public sealed class DocumentStyles
    {
        /// <summary>Base style of all text. Every property is set (Helvetica 10 pt, black).</summary>
        public TextStyle Normal { get; set; } = new TextStyle
        {
            Family = FontFamily.Helvetica,
            Size = 10,
            Bold = false,
            Italic = false,
            Underline = false,
            Strikethrough = false,
            Color = PdfColor.Black,
            Kerning = true,
        };

        public HeadingStyle Heading1 { get; } = new HeadingStyle(20, 14, 6);

        public HeadingStyle Heading2 { get; } = new HeadingStyle(15, 12, 4);

        public HeadingStyle Heading3 { get; } = new HeadingStyle(12, 10, 4);

        /// <summary>Style of table header rows (merged over Normal and the table's style).</summary>
        public TextStyle TableHeader { get; set; } = new TextStyle { Bold = true };

        internal HeadingStyle Heading(int level) => level == 1 ? Heading1 : level == 2 ? Heading2 : Heading3;
    }
}
