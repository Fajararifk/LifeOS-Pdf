using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>
    /// Builds words and lines from glyphs (§9.10 leaves this to the reader): glyphs are grouped
    /// per reading direction, by baseline, sorted along the line, split into words at gaps, and
    /// lines are put in reading order (optionally column by column).
    /// </summary>
    internal static class TextAssembler
    {
        private sealed class Item
        {
            public TextGlyph Glyph = null!;
            public double X0, X1, Top, Base, Size;
        }

        private sealed class Line
        {
            public List<Item> Items = new List<Item>();
            public double Base, Top, Size, X0, X1;
            public TextLine? Result;
        }

        public static List<TextLine> Lines(IEnumerable<TextGlyph> glyphs, PdfRect? area, bool includeInvisible, bool detectColumns)
        {
            var result = new List<TextLine>();
            var byOrientation = new List<Item>[4];
            foreach (var g in glyphs)
            {
                if ((!includeInvisible && g.Invisible) || g.Text.Length == 0)
                {
                    continue;
                }

                if (area.HasValue && !area.Value.Contains(g.Bounds.X + g.Bounds.Width / 2, g.Bounds.Y + g.Bounds.Height / 2))
                {
                    continue;
                }

                var item = Frame(g);
                (byOrientation[g.Orientation] ?? (byOrientation[g.Orientation] = new List<Item>())).Add(item);
            }

            for (var o = 0; o < 4; o++)
            {
                if (byOrientation[o] != null)
                {
                    var lines = BuildLines(byOrientation[o]);
                    result.AddRange((detectColumns ? Columns(lines) : lines).Select(l => l.Result!));
                }
            }

            return result;
        }

        /// <summary>Glyph box in a frame where the text runs left to right (y down).</summary>
        private static Item Frame(TextGlyph g)
        {
            var b = g.Bounds;
            double x0, x1, y0, y1;
            switch (g.Orientation)
            {
                case 1: x0 = b.Y; x1 = b.Bottom; y0 = -b.Right; y1 = -b.X; break;
                case 2: x0 = -b.Right; x1 = -b.X; y0 = -b.Bottom; y1 = -b.Y; break;
                case 3: x0 = -b.Bottom; x1 = -b.Y; y0 = b.X; y1 = b.Right; break;
                default: x0 = b.X; x1 = b.Right; y0 = b.Y; y1 = b.Bottom; break;
            }

            var size = g.FontSize > 0.1 ? g.FontSize : Math.Max(1, y1 - y0);
            // The box bottom includes the descent: the baseline is about 20% of the size above it.
            return new Item { Glyph = g, X0 = x0, X1 = x1, Top = y0, Base = y1 - size * 0.2, Size = size };
        }

        private static List<Line> BuildLines(List<Item> items)
        {
            items.Sort((a, b) => a.Base != b.Base ? a.Base.CompareTo(b.Base) : a.X0.CompareTo(b.X0));
            var lines = new List<Line>();
            Line? current = null;
            foreach (var item in items)
            {
                if (current == null || Math.Abs(item.Base - current.Base) > 0.45 * Math.Max(item.Size, current.Size))
                {
                    current = new Line { Base = item.Base, Size = item.Size };
                    lines.Add(current);
                }

                current.Items.Add(item);
                current.Size = Math.Max(current.Size, item.Size);
            }

            foreach (var line in lines)
            {
                line.Items.Sort((a, b) => a.X0.CompareTo(b.X0));
                Dedupe(line.Items);
                line.X0 = line.Items.Min(i => i.X0);
                line.X1 = line.Items.Max(i => i.X1);
                line.Top = line.Items.Min(i => i.Top);
                line.Result = Assemble(line.Items);
            }

            lines.RemoveAll(l => l.Result == null);
            lines.Sort((a, b) => Math.Abs(a.Base - b.Base) > 0.01 ? a.Base.CompareTo(b.Base) : a.X0.CompareTo(b.X0));
            return lines;
        }

        /// <summary>Fake bold (the same glyph drawn again slightly offset) counts once.</summary>
        private static void Dedupe(List<Item> items)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                // Overprinted copies sit within a few hundredths of an em; real letters ("ll") are further apart.
                var tolerance = items[i].Size * 0.06;
                for (var j = i - 1; j >= 0 && items[i].X0 - items[j].X0 < tolerance; j--)
                {
                    if (items[j].Glyph.Text == items[i].Glyph.Text && Math.Abs(items[j].X1 - items[i].X1) < tolerance &&
                        Math.Abs(items[j].Base - items[i].Base) < items[i].Size * 0.2)
                    {
                        items.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static TextLine? Assemble(List<Item> items)
        {
            var words = new List<TextWord>();
            var glyphs = new List<TextGlyph>();
            var text = new StringBuilder();
            Item? previous = null;
            void Flush()
            {
                if (glyphs.Count > 0)
                {
                    words.Add(new TextWord(text.ToString(), Union(glyphs.Select(g => g.Bounds)), glyphs.ToArray()));
                }

                glyphs.Clear();
                text.Clear();
            }

            foreach (var item in items)
            {
                var whitespace = item.Glyph.Text.Trim().Length == 0;
                if (previous != null)
                {
                    var gap = item.X0 - previous.X1;
                    if (gap > 0.15 * Math.Max(item.Size, previous.Size) || item.X0 < previous.X0 - 0.5 * item.Size)
                    {
                        Flush();
                    }
                }

                if (whitespace)
                {
                    Flush();
                }
                else
                {
                    glyphs.Add(item.Glyph);
                    text.Append(item.Glyph.Text);
                }

                previous = item;
            }

            Flush();
            if (words.Count == 0)
            {
                return null;
            }

            return new TextLine(string.Join(" ", words.Select(w => w.Text)), Union(words.Select(w => w.Bounds)), words);
        }

        private static PdfRect Union(IEnumerable<PdfRect> rects)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var r in rects)
            {
                x0 = Math.Min(x0, r.X);
                y0 = Math.Min(y0, r.Y);
                x1 = Math.Max(x1, r.Right);
                y1 = Math.Max(y1, r.Bottom);
            }

            return PdfRect.FromPoints(x0, y0, x1, y1);
        }

        /// <summary>
        /// Reading order for multi-column pages (a recursive XY-cut): a vertical gutter that is empty
        /// over the whole region splits it into columns (left first); otherwise the first horizontal
        /// gap splits off the top band.
        /// </summary>
        private static List<Line> Columns(List<Line> lines)
        {
            var result = new List<Line>();
            Cut(lines, result, 0);
            return result;
        }

        private static void Cut(List<Line> lines, List<Line> output, int depth)
        {
            if (lines.Count <= 1 || depth > 64)
            {
                output.AddRange(lines);
                return;
            }

            var size = lines.Average(l => l.Size);
            // Vertical gutter: sort by left edge and look for a gap no line crosses.
            var byX = lines.OrderBy(l => l.X0).ToList();
            var reach = byX[0].X1;
            for (var i = 1; i < byX.Count; i++)
            {
                if (byX[i].X0 - reach >= 1.5 * size)
                {
                    var left = byX.Take(i).ToList();
                    var right = byX.Skip(i).ToList();
                    if (left.Count >= 2 && right.Count >= 2)
                    {
                        Cut(Sorted(left), output, depth + 1);
                        Cut(Sorted(right), output, depth + 1);
                        return;
                    }
                }

                reach = Math.Max(reach, byX[i].X1);
            }

            // No gutter: emit the top line and continue with the rest.
            var sorted = Sorted(lines);
            output.Add(sorted[0]);
            sorted.RemoveAt(0);
            Cut(sorted, output, depth + 1);
        }

        private static List<Line> Sorted(List<Line> lines) =>
            lines.OrderBy(l => Math.Round(l.Base, 1)).ThenBy(l => l.X0).ToList();

        // ------------------------------------------------------------------ text output

        public static string Text(List<TextLine> lines, bool layout)
        {
            var sb = new StringBuilder();
            if (!layout)
            {
                TextLine? previous = null;
                foreach (var line in lines)
                {
                    if (previous != null)
                    {
                        sb.Append('\n');
                        var gap = line.Bounds.Y - previous.Bounds.Bottom;
                        if (gap > previous.Bounds.Height * 0.9)
                        {
                            sb.Append('\n'); // paragraph break
                        }
                    }

                    sb.Append(line.Text);
                    previous = line;
                }

                return sb.ToString();
            }

            // Layout: place words on a character grid.
            var widths = new List<double>();
            foreach (var line in lines)
            {
                foreach (var w in line.Words)
                {
                    foreach (var g in w.Glyphs)
                    {
                        if (g.Bounds.Width > 0)
                        {
                            widths.Add(g.Bounds.Width / Math.Max(1, g.Text.Length));
                        }
                    }
                }
            }

            if (widths.Count == 0)
            {
                return string.Empty;
            }

            widths.Sort();
            var charWidth = Math.Max(1, widths[widths.Count / 2]);
            var minX = lines.Min(l => l.Bounds.X);
            TextLine? prev = null;
            foreach (var line in lines)
            {
                if (prev != null)
                {
                    sb.Append('\n');
                    var blank = (int)Math.Round((line.Bounds.Y - prev.Bounds.Y) / Math.Max(1, prev.Bounds.Height) - 1);
                    for (var i = 0; i < Math.Min(2, blank); i++)
                    {
                        sb.Append('\n');
                    }
                }

                var row = new StringBuilder();
                foreach (var w in line.Words)
                {
                    var column = (int)Math.Round((w.Bounds.X - minX) / charWidth);
                    if (row.Length > 0 && column <= row.Length)
                    {
                        column = row.Length + 1;
                    }

                    row.Append(' ', Math.Max(0, column - row.Length));
                    row.Append(w.Text);
                }

                sb.Append(row);
                prev = line;
            }

            return sb.ToString();
        }
    }
}
