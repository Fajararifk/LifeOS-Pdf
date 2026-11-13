using System;

namespace LifeOS.Pdf.Forms
{
    /// <summary>One option of a combo box or list box (§12.7.3.4).</summary>
    public sealed class PdfChoiceOption
    {
        public PdfChoiceOption(string display, string? value = null)
        {
            Display = display ?? throw new ArgumentNullException(nameof(display));
            Value = value ?? display;
        }

        /// <summary>The text shown to the user.</summary>
        public string Display { get; }

        /// <summary>The value exported when selected; equals <see cref="Display"/> when omitted.</summary>
        public string Value { get; }
    }
}
