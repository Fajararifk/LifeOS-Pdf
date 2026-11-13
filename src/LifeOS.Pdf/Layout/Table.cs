using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>
    /// A table that flows across pages: rows are never split, header rows repeat on
    /// every page, rows joined by a row span move together.
    /// </summary>
    public sealed class Table : Block
    {
        internal Table()
        {
        }

        internal List<TableColumn> ColumnList { get; } = new List<TableColumn>();

        internal List<TableRow> RowList { get; } = new List<TableRow>();

        public IReadOnlyList<TableColumn> Columns => ColumnList;

        public IReadOnlyList<TableRow> Rows => RowList;

        /// <summary>Number of leading rows repeated at the top of each page. Default 0.</summary>
        public int HeaderRows { get; set; }

        /// <summary>Table width in points; null = full column width.</summary>
        public double? Width { get; set; }

        /// <summary>Space between cell border and content. Default 5 pt left/right, 3 pt top/bottom.</summary>
        public Margins CellPadding { get; set; } = new Margins(5, 3);

        /// <summary>Outer border; null = none.</summary>
        public PdfPen? Border { get; set; } = new PdfPen(PdfColor.FromHex("#9CA3AF"), 0.75);

        /// <summary>Lines between cells; null = none.</summary>
        public PdfPen? GridLines { get; set; } = new PdfPen(PdfColor.FromHex("#D1D5DB"), 0.5);

        public PdfColor? HeaderBackground { get; set; } = PdfColor.FromHex("#F3F4F6");

        /// <summary>Background of every second body row (zebra striping); null = none.</summary>
        public PdfColor? AlternateRowBackground { get; set; }

        /// <summary>Character style of header rows (merged over the document's TableHeader style).</summary>
        public TextStyle? HeaderStyle { get; set; }

        /// <summary>Character style of all cells.</summary>
        public TextStyle? Style { get; set; }

        public double SpaceBefore { get; set; } = 6;

        public double SpaceAfter { get; set; } = 6;

        public bool KeepWithNext { get; set; }

        public TableColumn AddColumn(ColumnWidth width, ParagraphAlignment? alignment = null)
        {
            if (RowList.Count > 0)
            {
                throw new InvalidOperationException("Add all columns before adding rows.");
            }

            var column = new TableColumn(width, alignment);
            ColumnList.Add(column);
            return column;
        }

        /// <summary>Adds an empty row with one cell per column.</summary>
        public TableRow AddRow()
        {
            if (ColumnList.Count == 0)
            {
                throw new InvalidOperationException("Add columns (AddColumn) before adding rows.");
            }

            var row = new TableRow(this);
            RowList.Add(row);
            return row;
        }

        /// <summary>Adds a row with one paragraph of text per cell (fewer texts leave cells empty).</summary>
        public TableRow AddRow(params string[] texts)
        {
            if (texts == null)
            {
                throw new ArgumentNullException(nameof(texts));
            }

            if (texts.Length > ColumnList.Count)
            {
                throw new ArgumentException("The row has " + texts.Length + " values but the table has " + ColumnList.Count + " columns.", nameof(texts));
            }

            var row = AddRow();
            for (var i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null)
                {
                    continue;
                }

                var p = row.Cells[i].AddParagraph(texts[i]);
                p.Format.SpaceAfter = 0;
                if (ColumnList[i].Alignment.HasValue)
                {
                    p.Format.Alignment = ColumnList[i].Alignment!.Value;
                }
            }

            return row;
        }
    }
}
