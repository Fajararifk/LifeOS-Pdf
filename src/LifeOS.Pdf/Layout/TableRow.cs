using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>A table row. Cells are created automatically, one per column.</summary>
    public sealed class TableRow
    {
        private readonly List<TableCell> _cells = new List<TableCell>();
        private double _minHeight;

        internal TableRow(Table table)
        {
            for (var i = 0; i < table.ColumnList.Count; i++)
            {
                _cells.Add(new TableCell());
            }
        }

        public IReadOnlyList<TableCell> Cells => _cells;

        public PdfColor? Background { get; set; }

        /// <summary>Minimum row height in points.</summary>
        public double MinHeight
        {
            get => _minHeight;
            set
            {
                PdfFormat.RequireNonNegative(value, nameof(MinHeight));
                _minHeight = value;
            }
        }
    }
}
