using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Forms
{
    /// <summary>A push button (§12.7.3.3): shows a caption and optionally resets the form.</summary>
    public sealed class PdfButton : PdfFormField
    {
        internal PdfButton(string name) : base(name)
        {
        }

        internal override string FieldType => "Btn";

        public string Caption { get; set; } = string.Empty;

        public PdfFont Font { get; set; } = new PdfFont(StandardFont.Helvetica, 10);

        public PdfColor TextColor { get; set; } = PdfColor.Black;

        public PdfColor? Background { get; set; }

        public PdfColor? BorderColor { get; set; }

        public ButtonAction Action { get; set; } = ButtonAction.None;
    }
}
