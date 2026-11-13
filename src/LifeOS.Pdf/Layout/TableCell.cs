using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>A table cell; holds any blocks (paragraphs, images, lists, nested tables).</summary>
    public sealed class TableCell : BlockContainer
    {
        private int _colSpan = 1;
        private int _rowSpan = 1;

        internal TableCell()
        {
        }

        /// <summary>Number of columns this cell covers. The covered cells must stay empty.</summary>
        public int ColSpan
        {
            get => _colSpan;
            set => _colSpan = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(ColSpan), value, "Span must be at least 1.");
        }

        /// <summary>Number of rows this cell covers. The covered cells must stay empty.</summary>
        public int RowSpan
        {
            get => _rowSpan;
            set => _rowSpan = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(RowSpan), value, "Span must be at least 1.");
        }

        public PdfColor? Background { get; set; }

        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

        /// <summary>Overrides the table's cell padding.</summary>
        public Margins? Padding { get; set; }

        internal override bool AllowsPageBreaks => false;
    }
}
