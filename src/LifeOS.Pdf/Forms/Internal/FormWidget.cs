using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Forms.Internal
{
    /// <summary>
    /// A widget annotation of a field (§12.5.6.19): where the field appears on a page and,
    /// for buttons, its on-state. A simple field has one widget; a radio group has one per button.
    /// </summary>
    internal sealed class FormWidget
    {
        public FormWidget(PdfFormField field, PdfPage page, double x, double y, double width, double height)
        {
            Field = field;
            Page = page;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public PdfFormField Field { get; }

        public PdfPage Page { get; }

        // Top-left, in points, unrotated page space (converted to PDF space at write time).
        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        /// <summary>The on-state name of a button widget (radio); null for other fields.</summary>
        public string? OnState { get; set; }

        /// <summary>Object number of the widget in the source file, or -1 when created here.</summary>
        public int SourceNumber { get; set; } = -1;

        /// <summary>The source widget dictionary (kept so an unmodified field round-trips exactly).</summary>
        public PdfDictionary? SourceDict { get; set; }

        /// <summary>True when the value changed and the appearance must be regenerated.</summary>
        public bool Dirty { get; set; }
    }
}
