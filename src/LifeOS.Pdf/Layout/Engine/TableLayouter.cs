using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>
    /// Lays out a <see cref="Table"/>: computes column widths, cell heights and row groups
    /// (rows tied together by row spans), then places groups page by page with the
    /// header rows repeated at the top of every fragment.
    /// </summary>
    internal sealed class TableLayouter : BlockLayouter
    {
        private const double Epsilon = 0.001;

        private readonly Table _table;
        private readonly TextStyle _baseStyle;
        private readonly ModelCache _cache;
        private readonly int _nextGroup;

        public TableLayouter(Table table, TextStyle baseStyle, LayoutContext ctx)
            : this(table, baseStyle, new ModelCache(), 0)
        {
        }

        private TableLayouter(Table table, TextStyle baseStyle, ModelCache cache, int nextGroup)
        {
            _table = table;
            _baseStyle = baseStyle;
            _cache = cache;
            _nextGroup = nextGroup;
            SpaceBefore = nextGroup == 0 ? table.SpaceBefore : 0;
            SpaceAfter = table.SpaceAfter;
            KeepWithNext = table.KeepWithNext;
        }

        public override LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop)
        {
            var m = GetModel(ctx, width);
            var headerHeight = m.RowTop(m.HeaderRows);
            if (m.Groups.Count == 0)
            {
                // Header-only (or empty) table.
                if (headerHeight > available && !atTop)
                {
                    return LayoutResult.Nothing;
                }

                return new LayoutResult(Draw(ctx, m, new List<int>(Enumerable.Range(0, m.HeaderRows))), headerHeight, null);
            }

            double y = headerHeight;
            var g = _nextGroup;
            while (g < m.Groups.Count)
            {
                var gh = m.GroupHeight(g);
                if (y + gh > available + Epsilon)
                {
                    break;
                }

                y += gh;
                g++;
            }

            if (g == _nextGroup)
            {
                if (!atTop)
                {
                    return LayoutResult.Nothing;
                }

                var group = m.Groups[_nextGroup];
                throw new LayoutException("Table rows " + (group.Start + 1) + "–" + group.End + " need " +
                    Math.Round(headerHeight + m.GroupHeight(_nextGroup), 1) + " pt (including header rows) but an empty page only has " +
                    Math.Round(available, 1) + " pt. Rows are never split across pages: shorten the cell content, reduce row spans, or use a larger page.");
            }

            var rows = new List<int>(Enumerable.Range(0, m.HeaderRows));
            for (var i = _nextGroup; i < g; i++)
            {
                for (var r = m.Groups[i].Start; r < m.Groups[i].End; r++)
                {
                    rows.Add(r);
                }
            }

            var remainder = g < m.Groups.Count ? new TableLayouter(_table, _baseStyle, _cache, g) : null;
            return new LayoutResult(Draw(ctx, m, rows), y, remainder);
        }

        public override double FirstChunkHeight(LayoutContext ctx, double width)
        {
            var m = GetModel(ctx, width);
            return m.RowTop(m.HeaderRows) + (_nextGroup < m.Groups.Count ? m.GroupHeight(_nextGroup) : 0);
        }

        public override void ContentWidth(LayoutContext ctx, out double min, out double max)
        {
            var placements = Placements(ctx);
            ColumnBounds(ctx, placements, out var colMin, out var colMax);
            min = colMin.Sum();
            max = colMax.Sum();
            if (_table.Width.HasValue)
            {
                min = max = _table.Width.Value;
            }
        }

        private Model GetModel(LayoutContext ctx, double available)
        {
            // The page number is deliberately not part of the key: rebuilding a long table on
            // every page made layout quadratic. Page-number fields inside cells are still drawn
            // with the right value (content is re-laid out when drawn); only their measured
            // width comes from the page where the table starts.
            var key = available + "|" + ctx.PageCount;
            if (_cache.Key == key && _cache.Model != null)
            {
                return _cache.Model;
            }

            var model = BuildModel(ctx, available);
            _cache.Key = key;
            _cache.Model = model;
            return model;
        }

        private Model BuildModel(LayoutContext ctx, double available)
        {
            if (_table.ColumnList.Count == 0)
            {
                throw new LayoutException("A table has no columns. Call AddColumn before adding rows.");
            }

            var placements = Placements(ctx);
            var width = Math.Min(_table.Width ?? available, available);
            var colWidths = ColumnWidths(ctx, placements, width);
            var colX = new double[colWidths.Length + 1];
            for (var c = 0; c < colWidths.Length; c++)
            {
                colX[c + 1] = colX[c] + colWidths[c];
            }

            var rowCount = _table.RowList.Count;
            var rowHeights = new double[rowCount];
            for (var r = 0; r < rowCount; r++)
            {
                rowHeights[r] = _table.RowList[r].MinHeight;
            }

            foreach (var p in placements)
            {
                var pad = Padding(p.Cell);
                var inner = Math.Max(1, colX[p.Col + p.ColSpan] - colX[p.Col] - pad.Left - pad.Right);
                LayouterFactory.Stack(p.Layouters, ctx, inner, out var contentHeight);
                p.Height = contentHeight + pad.Top + pad.Bottom;
                if (p.RowSpan == 1)
                {
                    rowHeights[p.Row] = Math.Max(rowHeights[p.Row], p.Height);
                }
            }

            // Row-spanning cells: grow the last spanned row if the cell needs more room.
            foreach (var p in placements.Where(p => p.RowSpan > 1))
            {
                double have = 0;
                for (var r = p.Row; r < p.Row + p.RowSpan; r++)
                {
                    have += rowHeights[r];
                }

                if (p.Height > have)
                {
                    rowHeights[p.Row + p.RowSpan - 1] += p.Height - have;
                }
            }

            var headerRows = Math.Min(Math.Max(0, _table.HeaderRows), rowCount);
            foreach (var p in placements)
            {
                if (p.Row < headerRows && p.Row + p.RowSpan > headerRows)
                {
                    throw new LayoutException("A cell in header row " + (p.Row + 1) + " spans into the table body. Header rows must not span into body rows.");
                }
            }

            // Groups of body rows that must stay together because of row spans.
            var groups = new List<RowGroup>();
            var start = headerRows;
            while (start < rowCount)
            {
                var end = start + 1;
                bool grown;
                do
                {
                    grown = false;
                    foreach (var p in placements)
                    {
                        if (p.Row >= start && p.Row < end && p.Row + p.RowSpan > end)
                        {
                            end = p.Row + p.RowSpan;
                            grown = true;
                        }
                    }
                }
                while (grown);

                groups.Add(new RowGroup(start, end));
                start = end;
            }

            return new Model(colX, rowHeights, headerRows, groups, placements);
        }

        private List<Placement> Placements(LayoutContext ctx)
        {
            var columns = _table.ColumnList.Count;
            var rows = _table.RowList.Count;
            var occupied = new bool[rows, columns];
            var headerStyle = _baseStyle.Merge(_table.Style).Merge(ctx.Styles.TableHeader).Merge(_table.HeaderStyle);
            var bodyStyle = _baseStyle.Merge(_table.Style);
            var placements = new List<Placement>();
            for (var r = 0; r < rows; r++)
            {
                var row = _table.RowList[r];
                for (var c = 0; c < columns; c++)
                {
                    var cell = row.Cells[c];
                    if (occupied[r, c])
                    {
                        if (cell.BlockList.Count > 0)
                        {
                            throw new LayoutException("Cell at row " + (r + 1) + ", column " + (c + 1) +
                                " has content but is covered by another cell's ColSpan/RowSpan. Remove the content or the span.");
                        }

                        continue;
                    }

                    var colSpan = Math.Min(cell.ColSpan, columns - c);
                    var rowSpan = Math.Min(cell.RowSpan, rows - r);
                    for (var rr = r; rr < r + rowSpan; rr++)
                    {
                        for (var cc = c; cc < c + colSpan; cc++)
                        {
                            occupied[rr, cc] = true;
                        }
                    }

                    var style = r < _table.HeaderRows ? headerStyle : bodyStyle;
                    placements.Add(new Placement(cell, r, c, colSpan, rowSpan, LayouterFactory.Create(cell.BlockList, style, ctx)));
                }
            }

            return placements;
        }

        private Margins Padding(TableCell cell) => cell.Padding ?? _table.CellPadding;

        private void ColumnBounds(LayoutContext ctx, List<Placement> placements, out double[] colMin, out double[] colMax)
        {
            var n = _table.ColumnList.Count;
            colMin = new double[n];
            colMax = new double[n];
            for (var c = 0; c < n; c++)
            {
                var pad = _table.CellPadding.Left + _table.CellPadding.Right;
                colMin[c] = pad;
                colMax[c] = pad;
            }

            foreach (var p in placements.Where(p => p.ColSpan == 1))
            {
                var pad = Padding(p.Cell);
                LayouterFactory.ContentWidth(p.Layouters, ctx, out var min, out var max);
                colMin[p.Col] = Math.Max(colMin[p.Col], min + pad.Left + pad.Right);
                colMax[p.Col] = Math.Max(colMax[p.Col], max + pad.Left + pad.Right);
            }

            for (var c = 0; c < n; c++)
            {
                var w = _table.ColumnList[c].Width;
                if (w.Kind == ColumnWidthKind.Fixed)
                {
                    colMin[c] = colMax[c] = w.Value;
                }
            }
        }

        private double[] ColumnWidths(LayoutContext ctx, List<Placement> placements, double tableWidth)
        {
            var columns = _table.ColumnList;
            var n = columns.Count;
            var widths = new double[n];
            ColumnBounds(ctx, placements, out var colMin, out var colMax);

            double fixedPercent = 0;
            for (var c = 0; c < n; c++)
            {
                var w = columns[c].Width;
                if (w.Kind == ColumnWidthKind.Fixed)
                {
                    widths[c] = w.Value;
                }
                else if (w.Kind == ColumnWidthKind.Percent)
                {
                    widths[c] = tableWidth * w.Value / 100.0;
                }
                else
                {
                    continue;
                }

                fixedPercent += widths[c];
            }

            var remaining = tableWidth - fixedPercent;
            if (remaining < -Epsilon)
            {
                throw new LayoutException("Fixed and percentage table columns need " + Math.Round(fixedPercent, 1) +
                    " pt but the table is only " + Math.Round(tableWidth, 1) + " pt wide. Use smaller widths or Relative/Auto columns.");
            }

            var autos = Enumerable.Range(0, n).Where(c => columns[c].Width.Kind == ColumnWidthKind.Auto).ToList();
            var relatives = Enumerable.Range(0, n).Where(c => columns[c].Width.Kind == ColumnWidthKind.Relative).ToList();
            if (autos.Count == 0 && relatives.Count == 0)
            {
                return widths;
            }

            remaining = Math.Max(0, remaining);
            var relMin = relatives.Sum(c => colMin[c]);
            var autoMin = autos.Sum(c => colMin[c]);
            var autoPref = autos.Sum(c => colMax[c]);
            var forAutos = remaining - relMin;

            if (autoPref <= forAutos)
            {
                foreach (var c in autos)
                {
                    widths[c] = colMax[c];
                }

                var leftover = forAutos - autoPref;
                if (relatives.Count > 0)
                {
                    var weights = relatives.Sum(c => columns[c].Width.Value);
                    foreach (var c in relatives)
                    {
                        widths[c] = colMin[c] + leftover * columns[c].Width.Value / weights;
                    }
                }
                else if (autoPref > 0)
                {
                    // No relative columns: auto columns share the extra space to fill the table.
                    foreach (var c in autos)
                    {
                        widths[c] += leftover * colMax[c] / autoPref;
                    }
                }
            }
            else if (autoMin <= forAutos)
            {
                var k = autoPref - autoMin > 0 ? (forAutos - autoMin) / (autoPref - autoMin) : 0;
                foreach (var c in autos)
                {
                    widths[c] = colMin[c] + (colMax[c] - colMin[c]) * k;
                }

                foreach (var c in relatives)
                {
                    widths[c] = colMin[c];
                }
            }
            else
            {
                // Even the narrowest content does not fit: scale everything down (words get split).
                var total = autoMin + relMin;
                foreach (var c in autos.Concat(relatives))
                {
                    widths[c] = total > 0 ? remaining * colMin[c] / total : remaining / (autos.Count + relatives.Count);
                }
            }

            return widths;
        }

        private List<DrawItem> Draw(LayoutContext ctx, Model m, List<int> rows)
        {
            var items = new List<DrawItem>();
            var top = new Dictionary<int, double>();
            double y = 0;
            foreach (var r in rows)
            {
                top[r] = y;
                y += m.RowHeights[r];
            }

            var totalHeight = y;
            var width = m.ColX[m.ColX.Length - 1];
            var lastRow = rows.Count > 0 ? rows[rows.Count - 1] : -1;

            // 1. Backgrounds.
            foreach (var r in rows)
            {
                var row = _table.RowList[r];
                var fill = row.Background;
                if (fill == null && r < m.HeaderRows)
                {
                    fill = _table.HeaderBackground;
                }

                if (fill == null && r >= m.HeaderRows && (r - m.HeaderRows) % 2 == 1)
                {
                    fill = _table.AlternateRowBackground;
                }

                if (fill != null)
                {
                    items.Add(new RectItem(0, top[r], width, m.RowHeights[r], fill, null));
                }
            }

            var visible = m.Placements.Where(p => top.ContainsKey(p.Row)).ToList();
            foreach (var p in visible.Where(p => p.Cell.Background != null))
            {
                items.Add(new RectItem(m.ColX[p.Col], top[p.Row], m.ColX[p.Col + p.ColSpan] - m.ColX[p.Col], m.SpanHeight(p), p.Cell.Background, null));
            }

            // 2. Cell content.
            foreach (var p in visible)
            {
                var pad = Padding(p.Cell);
                var inner = Math.Max(1, m.ColX[p.Col + p.ColSpan] - m.ColX[p.Col] - pad.Left - pad.Right);
                var content = LayouterFactory.Stack(p.Layouters, ctx, inner, out _);
                var cellHeight = m.SpanHeight(p);
                var offset = p.Cell.VerticalAlignment == VerticalAlignment.Middle ? (cellHeight - p.Height) / 2
                    : p.Cell.VerticalAlignment == VerticalAlignment.Bottom ? cellHeight - p.Height : 0;
                DrawItemList.TranslateAll(content, m.ColX[p.Col] + pad.Left, top[p.Row] + pad.Top + offset);
                items.AddRange(content);
            }

            // 3. Grid lines (right and bottom edge of each cell, except on the outer border).
            if (_table.GridLines != null)
            {
                foreach (var p in visible)
                {
                    var x2 = m.ColX[p.Col + p.ColSpan];
                    var y1 = top[p.Row];
                    var y2 = y1 + m.SpanHeight(p);
                    if (p.Col + p.ColSpan < m.ColX.Length - 1)
                    {
                        items.Add(new LineItem(_table.GridLines, x2, y1, x2, y2));
                    }

                    if (p.Row + p.RowSpan - 1 != lastRow)
                    {
                        items.Add(new LineItem(_table.GridLines, m.ColX[p.Col], y2, x2, y2));
                    }
                }
            }

            // 4. Outer border.
            if (_table.Border != null && totalHeight > 0)
            {
                items.Add(new RectItem(0, 0, width, totalHeight, null, _table.Border));
            }

            return items;
        }

        private sealed class Placement
        {
            public Placement(TableCell cell, int row, int col, int colSpan, int rowSpan, List<BlockLayouter> layouters)
            {
                Cell = cell;
                Row = row;
                Col = col;
                ColSpan = colSpan;
                RowSpan = rowSpan;
                Layouters = layouters;
            }

            public TableCell Cell { get; }

            public int Row { get; }

            public int Col { get; }

            public int ColSpan { get; }

            public int RowSpan { get; }

            public List<BlockLayouter> Layouters { get; }

            /// <summary>Content height including padding.</summary>
            public double Height { get; set; }
        }

        private readonly struct RowGroup
        {
            public RowGroup(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Start { get; }

            public int End { get; }
        }

        private sealed class Model
        {
            public Model(double[] colX, double[] rowHeights, int headerRows, List<RowGroup> groups, List<Placement> placements)
            {
                ColX = colX;
                RowHeights = rowHeights;
                HeaderRows = headerRows;
                Groups = groups;
                Placements = placements;
            }

            public double[] ColX { get; }

            public double[] RowHeights { get; }

            public int HeaderRows { get; }

            public List<RowGroup> Groups { get; }

            public List<Placement> Placements { get; }

            public double RowTop(int row)
            {
                double y = 0;
                for (var r = 0; r < row; r++)
                {
                    y += RowHeights[r];
                }

                return y;
            }

            public double GroupHeight(int group)
            {
                double h = 0;
                for (var r = Groups[group].Start; r < Groups[group].End; r++)
                {
                    h += RowHeights[r];
                }

                return h;
            }

            public double SpanHeight(Placement p)
            {
                double h = 0;
                for (var r = p.Row; r < p.Row + p.RowSpan; r++)
                {
                    h += RowHeights[r];
                }

                return h;
            }
        }

        private sealed class ModelCache
        {
            public string? Key { get; set; }

            public Model? Model { get; set; }
        }
    }
}
