using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Extraction
{
    /// <summary>A table found on a page: rows of cell texts (merged cells keep their text in the first cell).</summary>
    public sealed class ExtractedTable
    {
        internal ExtractedTable(List<List<string>> rows, PdfRect bounds, TableDetection method)
        {
            var copy = new List<IReadOnlyList<string>>();
            foreach (var r in rows)
            {
                copy.Add(r.ToArray());
            }

            Rows = copy;
            Bounds = bounds;
            Method = method;
        }

        public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

        public int RowCount => Rows.Count;

        public int ColumnCount => Rows.Count == 0 ? 0 : Rows[0].Count;

        public PdfRect Bounds { get; }

        /// <summary><see cref="TableDetection.Lattice"/> (ruled) or <see cref="TableDetection.Stream"/> (aligned text).</summary>
        public TableDetection Method { get; }

        public string this[int row, int column] => Rows[row][column];

        /// <summary>CSV (RFC 4180): fields with the separator, quotes or line breaks are quoted.</summary>
        public string ToCsv(char separator = ',')
        {
            var sb = new StringBuilder();
            foreach (var row in Rows)
            {
                for (var c = 0; c < row.Count; c++)
                {
                    if (c > 0)
                    {
                        sb.Append(separator);
                    }

                    var v = row[c];
                    if (v.IndexOf(separator) >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
                    {
                        sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
                    }
                    else
                    {
                        sb.Append(v);
                    }
                }

                sb.Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// JSON: with <paramref name="firstRowIsHeader"/> an array of objects keyed by the header cells,
        /// otherwise an array of arrays.
        /// </summary>
        public string ToJson(bool firstRowIsHeader = true)
        {
            var sb = new StringBuilder("[");
            if (firstRowIsHeader && Rows.Count > 0)
            {
                var header = new List<string>();
                var seen = new HashSet<string>();
                for (var c = 0; c < Rows[0].Count; c++)
                {
                    var name = Rows[0][c].Length > 0 ? Rows[0][c] : "column" + (c + 1).ToString(CultureInfo.InvariantCulture);
                    var unique = name;
                    for (var n = 2; !seen.Add(unique); n++)
                    {
                        unique = name + "_" + n.ToString(CultureInfo.InvariantCulture);
                    }

                    header.Add(unique);
                }

                for (var r = 1; r < Rows.Count; r++)
                {
                    sb.Append(r > 1 ? ",\n  {" : "\n  {");
                    for (var c = 0; c < header.Count; c++)
                    {
                        if (c > 0)
                        {
                            sb.Append(", ");
                        }

                        Quote(sb, header[c]);
                        sb.Append(": ");
                        Quote(sb, c < Rows[r].Count ? Rows[r][c] : string.Empty);
                    }

                    sb.Append('}');
                }
            }
            else
            {
                for (var r = 0; r < Rows.Count; r++)
                {
                    sb.Append(r > 0 ? ",\n  [" : "\n  [");
                    for (var c = 0; c < Rows[r].Count; c++)
                    {
                        if (c > 0)
                        {
                            sb.Append(", ");
                        }

                        Quote(sb, Rows[r][c]);
                    }

                    sb.Append(']');
                }
            }

            sb.Append(Rows.Count > (firstRowIsHeader ? 1 : 0) ? "\n]" : "]");
            return sb.ToString();
        }

        private static void Quote(StringBuilder sb, string s)
        {
            const char Slash = (char)92; // backslash
            sb.Append('"');
            foreach (var ch in s)
            {
                if (ch == '"' || ch == Slash)
                {
                    sb.Append(Slash).Append(ch);
                }
                else if (ch < 0x20)
                {
                    sb.Append(Slash);
                    switch ((int)ch)
                    {
                        case 10: sb.Append('n'); break;
                        case 13: sb.Append('r'); break;
                        case 9: sb.Append('t'); break;
                        default: sb.Append('u').Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture)); break;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }

            sb.Append('"');
        }
    }
}
