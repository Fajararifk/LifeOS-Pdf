using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Forms
{
    /// <summary>A combo box (editable) or list box (multi-select) (§12.7.3.4).</summary>
    public sealed class PdfChoiceField : PdfFormField
    {
        private readonly bool _combo;
        private readonly List<PdfChoiceOption> _options = new List<PdfChoiceOption>();

        internal PdfChoiceField(string name, bool combo) : base(name)
        {
            _combo = combo;
        }

        internal override string FieldType => "Ch";

        /// <summary>True for a combo box, false for a list box.</summary>
        public bool Combo => _combo;

        /// <summary>The selectable options, in order.</summary>
        public IReadOnlyList<PdfChoiceOption> Options => _options;

        /// <summary>The export values currently selected.</summary>
        public string[] SelectedValues { get; set; } = Array.Empty<string>();

        /// <summary>True when several options may be selected at once (list box).</summary>
        public bool MultiSelect { get; set; }

        /// <summary>True when the user may type a value not in the list (combo box).</summary>
        public bool Editable { get; set; }

        public PdfFont Font { get; set; } = new PdfFont(StandardFont.Helvetica, 10);

        public PdfColor TextColor { get; set; } = PdfColor.Black;

        public PdfColor? Background { get; set; }

        public PdfColor? BorderColor { get; set; }

        internal void AddOption(PdfChoiceOption option) => _options.Add(option);
    }
}
