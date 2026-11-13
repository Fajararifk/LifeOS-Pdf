using System;
using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Forms
{
    /// <summary>
    /// A radio button group (§12.7.3.3): one field with several on-states, one widget per
    /// button. Create it with <see cref="PdfPage.AddRadioButton"/>; each call with the same
    /// name returns the same group and adds a button.
    /// </summary>
    public sealed class PdfRadioButton : PdfFormField
    {
        internal PdfRadioButton(string name) : base(name)
        {
        }

        internal override string FieldType => "Btn";

        /// <summary>The selected on-state name, or null when none is selected.</summary>
        public string? Value { get; set; }

        public CheckStyle Style { get; set; } = CheckStyle.Circle;

        public PdfColor? Background { get; set; }

        public PdfColor? BorderColor { get; set; }
    }
}
