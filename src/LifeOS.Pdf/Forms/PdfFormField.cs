using System;
using System.Collections.Generic;
using LifeOS.Pdf.Forms.Internal;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Forms
{
    /// <summary>
    /// Base of every form field (§12.7.2). A field has a partial name and appears as one or
    /// more widget annotations across pages.
    /// </summary>
    public abstract class PdfFormField
    {
        private readonly string _name;

        internal PdfFormField(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A field needs a name.", nameof(name));
            }

            _name = name;
        }

        /// <summary>The fully qualified partial name ("Customer.Nama").</summary>
        public string Name => _name;

        /// <summary>True when the viewer must not let the user change the value.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>True when the viewer should require the field to be filled.</summary>
        public bool Required { get; set; }

        /// <summary>Tooltip shown on hover.</summary>
        public string? Tooltip { get; set; }

        /// <summary>The /FT value: "Tx", "Btn", "Ch" or "Sig".</summary>
        internal abstract string FieldType { get; }

        /// <summary>The widget annotations of this field (one, or several for a radio group).</summary>
        internal List<FormWidget> Widgets { get; } = new List<FormWidget>();

        // --- read from a file (FormReader) ---
        internal PdfDictionary? SourceDict { get; set; }

        /// <summary>The /DA value inherited from the source, or null.</summary>
        internal string? SourceAppearance { get; set; }

        /// <summary>Marks the field and every widget dirty so the appearance is regenerated.</summary>
        internal void MarkDirty()
        {
            foreach (var w in Widgets)
            {
                w.Dirty = true;
            }
        }

        internal FormWidget AddWidget(PdfPage page, double x, double y, double width, double height)
        {
            var widget = new FormWidget(this, page, x, y, width, height);
            Widgets.Add(widget);
            page.FormWidgets.Add(widget);
            return widget;
        }
    }
}
