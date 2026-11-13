using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Forms
{
    /// <summary>A single-line or multi-line text field (§12.7.3.2).</summary>
    public sealed class PdfTextField : PdfFormField
    {
        internal PdfTextField(string name) : base(name)
        {
        }

        internal override string FieldType => "Tx";

        /// <summary>The current value.</summary>
        public string? Value { get; set; }

        /// <summary>The value restored by a reset action.</summary>
        public string? DefaultValue { get; set; }

        /// <summary>The font (face + size) used for the field text; Helvetica 10 by default.</summary>
        public PdfFont Font { get; set; } = new PdfFont(StandardFont.Helvetica, 10);

        public PdfColor TextColor { get; set; } = PdfColor.Black;

        public TextAlign Align { get; set; } = TextAlign.Left;

        public bool Multiline { get; set; }

        public bool Password { get; set; }

        /// <summary>Spread the value over fixed-width character cells.</summary>
        public bool Comb { get; set; }

        public int? MaxLength { get; set; }

        public PdfColor? Background { get; set; }

        public PdfColor? BorderColor { get; set; }
    }
}
