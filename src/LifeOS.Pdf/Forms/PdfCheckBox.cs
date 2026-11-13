using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Forms
{
    /// <summary>A checkbox (§12.7.3.3): an on/off button.</summary>
    public sealed class PdfCheckBox : PdfFormField
    {
        internal PdfCheckBox(string name) : base(name)
        {
        }

        internal override string FieldType => "Btn";

        public bool Checked { get; set; }

        /// <summary>The exported value when checked; "Yes" by default.</summary>
        public string OnValue { get; set; } = "Yes";

        public CheckStyle Style { get; set; } = CheckStyle.Check;

        public PdfColor? Background { get; set; }

        public PdfColor? BorderColor { get; set; }
    }
}
