using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>
    /// Finds tables on a page. Lattice: cell borders (stroked lines or thin filled rectangles) form a
    /// grid; words go into the cell that contains their centre; a missing inner border means a merged
    /// cell. Stream: consecutive lines whose words fall into the same column bands (union of
    /// overlapping horizontal extents); lines with an empty first column continue the row above.
    /// </summary>
    internal static class TableDetector
    {
        private const double Snap = 2.0;

        public static List<ExtractedTable> Find(PageAnalysis analysis, TableExtractionOptions options)
        {
            var tables = new List<ExtractedTable>();
            var lines = analysis.Lines.Where(l => !options.Area.HasValue || options.Area.Value.Intersects(l.Bounds)).ToList();
            if (options.Detection != TableDetection.Stream)
            {
                tables.AddRange(Lattice(analysis.Rulings.Where(r => !options.Area.HasValue || options.Area.Value.Intersects(Grow(r))).ToList(), lines, options));
            }

            if (options.Detection != TableDetection.Lattice)
            {
                var free = lines.Where(l => !tables.Any(t => Grow(t.Bounds).Contains(l.Bounds))).ToList();
                tables.AddRange(Stream(free, options));
            }

            tables.Sort((a, b) => a.Bounds.Y.CompareTo(b.Bounds.Y));
            return tables;
        }

        private static PdfRect Grow(PdfRect r) => new PdfRect(r.X - Snap, r.Y - Snap, r.Width + 2 * Snap, r.Height + 2 * Snap);

        // ------------------------------------------------------------------ lattice

        private sealed class Rule
        {
            public double Pos, From, To;
        }

        private static List<ExtractedTable> Lattice(List<PdfRect> rulings, List<TextLine> lines, TableExtractionOptions options)
        {
            var result = new List<ExtractedTable>();
            var horizontal = Merge(rulings.Where(r => r.Height <= r.Width).Select(r => new Rule { Pos = r.Y + r.Height / 2, From = r.X, To = r.Right }));
            var vertical = Merge(rulings.Where(r => r.Height > r.Width).Select(r => new Rule { Pos = r.X + r.Width / 2, From = r.Y, To = r.Bottom }));
            if (horizontal.Count < 2 || vertical.Count < 2)
            {
                return result;
            }

            // Group lines that touch into separate tables (union-find over intersections).
            var all = horizontal.Select(h => (Rule: h, Horizontal: true)).Concat(vertical.Select(v => (Rule: v, Horizontal: false))).ToList();
            var parent = Enumerable.Range(0, all.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            for (var i = 0; i < all.Count; i++)
            {
                if (!all[i].Horizontal)
                {
                    continue;
                }

                for (var j = 0; j < all.Count; j++)
                {
                    if (all[j].Horizontal)
                    {
                        continue;
                    }

                    var h = all[i].Rule;
                    var v = all[j].Rule;
                    if (v.Pos >= h.From - Snap && v.Pos <= h.To + Snap && h.Pos >= v.From - Snap && h.Pos <= v.To + Snap)
                    {
                        parent[Find(i)] = Find(j);
                    }
                }
            }

            foreach (var group in Enumerable.Range(0, all.Count).GroupBy(Find))
            {
                var hs = group.Where(i => all[i].Horizontal).Select(i => all[i].Rule).ToList();
                var vs = group.Where(i => !all[i].Horizontal).Select(i => all[i].Rule).ToList();
                if (hs.Count < 2 || vs.Count < 2)
                {
                    continue;
                }

                var ys = Distinct(hs.Select(h => h.Pos));
                var xs = Distinct(vs.Select(v => v.Pos));
                if (ys.Count < 2 || xs.Count < 2)
                {
                    continue;
                }

                var bounds = PdfRect.FromPoints(xs[0], ys[0], xs[xs.Count - 1], ys[ys.Count - 1]);
                var rows = ys.Count - 1;
                var cols = xs.Count - 1;
                var cells = new StringBuilder[rows, cols];
                for (var r = 0; r < rows; r++)
                {
                    for (var c = 0; c < cols; c++)
                    {
                        cells[r, c] = new StringBuilder();
                    }
                }

                // A cell that has no border on its right/bottom joins its neighbour (merged cell).
                var ownerRow = new int[rows, cols];
                var ownerCol = new int[rows, cols];
                for (var r = 0; r < rows; r++)
                {
                    for (var c = 0; c < cols; c++)
                    {
                        ownerRow[r, c] = r;
                        ownerCol[r, c] = c;
                    }
                }

                for (var r = 0; r < rows; r++)
                {
                    var midY = (ys[r] + ys[r + 1]) / 2;
                    for (var c = 1; c < cols; c++)
                    {
                        if (!vs.Any(v => Math.Abs(v.Pos - xs[c]) <= Snap && v.From <= midY + Snap && v.To >= midY - Snap))
                        {
                            ownerRow[r, c] = ownerRow[r, c - 1];
                            ownerCol[r, c] = ownerCol[r, c - 1];
                        }
                    }
                }

                for (var c = 0; c < cols; c++)
                {
                    var midX = (xs[c] + xs[c + 1]) / 2;
                    for (var r = 1; r < rows; r++)
                    {
                        if (!hs.Any(h => Math.Abs(h.Pos - ys[r]) <= Snap && h.From <= midX + Snap && h.To >= midX - Snap))
                        {
                            ownerRow[r, c] = ownerRow[r - 1, c];
                            ownerCol[r, c] = ownerCol[r - 1, c];
                        }
                    }
                }

                var any = false;
                foreach (var line in lines)
                {
                    foreach (var word in line.Words)
                    {
                        var cx = word.Bounds.X + word.Bounds.Width / 2;
                        var cy = word.Bounds.Y + word.Bounds.Height / 2;
                        if (!bounds.Contains(cx, cy))
                        {
                            continue;
                        }

                        var c = Index(xs, cx);
                        var r = Index(ys, cy);
                        var cell = cells[ownerRow[r, c], ownerCol[r, c]];
                        if (cell.Length > 0)
                        {
                            cell.Append(' ');
                        }

                        cell.Append(word.Text);
                        any = true;
                    }
                }

                if (!any)
                {
                    continue;
                }

                var table = new List<List<string>>();
                for (var r = 0; r < rows; r++)
                {
                    var row = new List<string>();
                    for (var c = 0; c < cols; c++)
                    {
                        row.Add(cells[r, c].ToString());
                    }

                    if (row.Any(v => v.Length > 0))
                    {
                        table.Add(row);
                    }
                }

                DropEmptyColumns(table);
                if (table.Count >= options.MinRows && table.Count > 0 && table[0].Count >= options.MinColumns)
                {
                    result.Add(new ExtractedTable(table, bounds, TableDetection.Lattice));
                }
            }

            return result;
        }

        /// <summary>Joins collinear rules that overlap or touch (drawn in pieces cell by cell).</summary>
        private static List<Rule> Merge(IEnumerable<Rule> rules)
        {
            var result = new List<Rule>();
            foreach (var rule in rules.OrderBy(r => r.Pos).ThenBy(r => r.From))
            {
                var last = result.LastOrDefault(r => Math.Abs(r.Pos - rule.Pos) <= Snap && rule.From <= r.To + Snap);
                if (last != null)
                {
                    last.To = Math.Max(last.To, rule.To);
                    last.From = Math.Min(last.From, rule.From);
                }
                else
                {
                    result.Add(new Rule { Pos = rule.Pos, From = rule.From, To = rule.To });
                }
            }

            return result;
        }

        private static List<double> Distinct(IEnumerable<double> values)
        {
            var result = new List<double>();
            foreach (var v in values.OrderBy(v => v))
            {
                if (result.Count == 0 || v - result[result.Count - 1] > Snap)
                {
                    result.Add(v);
                }
            }

            return result;
        }

        private static int Index(List<double> edges, double v)
        {
            for (var i = 0; i < edges.Count - 2; i++)
            {
                if (v < edges[i + 1])
                {
                    return i;
                }
            }

            return edges.Count - 2;
        }

        private static void DropEmptyColumns(List<List<string>> table)
        {
            if (table.Count == 0)
            {
                return;
            }

            for (var c = table[0].Count - 1; c >= 0; c--)
            {
                if (table.All(r => r[c].Length == 0))
                {
                    foreach (var r in table)
                    {
                        r.RemoveAt(c);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ stream

        private sealed class Segment
        {
            public double X0, X1;
            public string Text = string.Empty;
        }

        private static List<ExtractedTable> Stream(List<TextLine> lines, TableExtractionOptions options)
        {
            var result = new List<ExtractedTable>();
            var block = new List<(TextLine Line, List<Segment> Segments)>();
            void Close()
            {
                // Trailing single-cell lines are not part of the table.
                while (block.Count > 0 && block[block.Count - 1].Segments.Count < 2)
                {
                    block.RemoveAt(block.Count - 1);
                }

                if (block.Count >= options.MinRows)
                {
                    var table = BuildStream(block, options);
                    if (table != null)
                    {
                        result.Add(table);
                    }
                }

                block.Clear();
            }

            TextLine? previous = null;
            foreach (var line in lines)
            {
                var segments = Segments(line);
                var gapAbove = previous == null ? 0 : line.Bounds.Y - previous.Bounds.Bottom;
                var size = line.Words.Count > 0 ? line.Words[0].FontSize : 10;
                if (gapAbove > 2.5 * size)
                {
                    Close();
                }

                if (segments.Count >= 2)
                {
                    block.Add((line, segments));
                }
                else if (block.Count > 0 && segments.Count == 1)
                {
                    block.Add((line, segments)); // possibly a continuation line inside the table
                }
                else
                {
                    Close();
                }

                previous = line;
            }

            Close();
            return result;
        }

        /// <summary>Words of a line merged into cells: a gap wider than about one em separates cells.</summary>
        private static List<Segment> Segments(TextLine line)
        {
            var result = new List<Segment>();
            Segment? current = null;
            TextWord? previous = null;
            foreach (var w in line.Words)
            {
                var gap = previous == null ? double.MaxValue : w.Bounds.X - previous.Bounds.Right;
                if (current == null || gap > Math.Max(w.FontSize, previous!.FontSize) * 1.0)
                {
                    current = new Segment { X0 = w.Bounds.X, X1 = w.Bounds.Right, Text = w.Text };
                    result.Add(current);
                }
                else
                {
                    current.X1 = w.Bounds.Right;
                    current.Text += " " + w.Text;
                }

                previous = w;
            }

            return result;
        }

        private static ExtractedTable? BuildStream(List<(TextLine Line, List<Segment> Segments)> block, TableExtractionOptions options)
        {
            // Column bands: union of overlapping segment extents of the multi-cell lines.
            var bands = new List<double[]>();
            foreach (var s in block.Where(b => b.Segments.Count >= 2).SelectMany(b => b.Segments).OrderBy(s => s.X0))
            {
                var last = bands.Count > 0 ? bands[bands.Count - 1] : null;
                if (last != null && s.X0 <= last[1] + 1)
                {
                    last[1] = Math.Max(last[1], s.X1);
                }
                else
                {
                    bands.Add(new[] { s.X0, s.X1 });
                }
            }

            if (bands.Count < options.MinColumns)
            {
                return null;
            }

            var rows = new List<List<string>>();
            var bounds = block[0].Line.Bounds;
            foreach (var (line, segments) in block)
            {
                bounds = bounds.Union(line.Bounds);
                var row = new List<string>(Enumerable.Repeat(string.Empty, bands.Count));
                foreach (var s in segments)
                {
                    var best = 0;
                    double bestOverlap = -1;
                    for (var c = 0; c < bands.Count; c++)
                    {
                        var overlap = Math.Min(s.X1, bands[c][1]) - Math.Max(s.X0, bands[c][0]);
                        if (overlap > bestOverlap)
                        {
                            bestOverlap = overlap;
                            best = c;
                        }
                    }

                    row[best] = row[best].Length > 0 ? row[best] + " " + s.Text : s.Text;
                }

                // A line with an empty first column continues the previous row (wrapped descriptions).
                if (rows.Count > 0 && row[0].Length == 0 && row.Count(v => v.Length > 0) <= Math.Max(1, bands.Count / 2))
                {
                    var prev = rows[rows.Count - 1];
                    for (var c = 0; c < row.Count; c++)
                    {
                        if (row[c].Length > 0)
                        {
                            prev[c] = prev[c].Length > 0 ? prev[c] + " " + row[c] : row[c];
                        }
                    }

                    continue;
                }

                rows.Add(row);
            }

            DropEmptyColumns(rows);
            if (rows.Count < options.MinRows || rows[0].Count < options.MinColumns)
            {
                return null;
            }

            // A bulleted or numbered list looks like two columns; it is not a table.
            if (rows.All(r => r[0].Length == 0 || IsListMarker(r[0])))
            {
                return null;
            }

            return new ExtractedTable(rows, bounds, TableDetection.Stream);
        }

        private static bool IsListMarker(string text)
        {
            var t = text.Trim();
            if (t.Length == 0 || t.Length > 4)
            {
                return false;
            }

            if (t == "•" || t == "–" || t == "-" || t == "*" || t == "·" || t == "◦" || t == "▪")
            {
                return true;
            }

            var last = t[t.Length - 1];
            return (last == '.' || last == ')') && t.Substring(0, t.Length - 1).All(char.IsLetterOrDigit);
        }
    }
}
