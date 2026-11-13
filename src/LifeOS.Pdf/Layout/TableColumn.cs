using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>A table column.</summary>
    public sealed class TableColumn
    {
        internal TableColumn(ColumnWidth width, ParagraphAlignment? alignment)
        {
            Width = width;
            Alignment = alignment;
        }

        public ColumnWidth Width { get; set; }

        /// <summary>Default alignment of text added with <see cref="Table.AddRow(string[])"/>.</summary>
        public ParagraphAlignment? Alignment { get; set; }
    }
}
