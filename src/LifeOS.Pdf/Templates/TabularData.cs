using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LifeOS.Pdf.Templates
{
    /// <summary>
    /// Tabular data loaded from CSV or XLSX (first sheet by default). Pass it straight to
    /// <see cref="PdfTemplate.Render"/>: the template iterates <see cref="Rows"/> and reads each
    /// value by header name (e.g. <c>{{ row.Tanggal }}</c>).
    /// </summary>
    public sealed class TabularData
    {
        private TabularData()
        {
        }

        /// <summary>Display names of the columns (the original header text).</summary>
        public IReadOnlyList<string> Columns { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// The keys of <see cref="Rows"/>, parallel to <see cref="Columns"/>: the header text
        /// reduced to a valid identifier (<c>No. Faktur</c> → <c>No_Faktur</c>), so a template can
        /// read <c>{{ row.No_Faktur }}</c> or, generically, <c>{{ row[f] }}</c>.
        /// </summary>
        public IReadOnlyList<string> Fields { get; private set; } = Array.Empty<string>();

        /// <summary>One dictionary per data row, keyed by <see cref="Fields"/> (case-insensitive).</summary>
        public IReadOnlyList<Dictionary<string, object?>> Rows { get; private set; } = Array.Empty<Dictionary<string, object?>>();

        /// <summary>
        /// Suggested alignment per column, parallel to <see cref="Columns"/>: <c>"right"</c> when
        /// every non-empty value is a number, otherwise <c>"left"</c>. Handy for template columns.
        /// </summary>
        public IReadOnlyList<string> Aligns { get; private set; } = Array.Empty<string>();

        /// <summary>Sum of every numeric column, keyed by field name. Empty columns are skipped.</summary>
        public IReadOnlyDictionary<string, decimal> Totals { get; private set; } = new Dictionary<string, decimal>();

        /// <summary>True when at least one column is numeric, i.e. a total row makes sense.</summary>
        public bool HasTotals => Totals.Count > 0;

        /// <summary>Optional report title (for the template).</summary>
        public string? Title { get; set; }

        /// <summary>Optional report subtitle (for the template).</summary>
        public string? Subtitle { get; set; }

        // ------------------------------------------------------------------ CSV

        /// <summary>Parses CSV text. The first row is the header unless <paramref name="hasHeader"/> is false.</summary>
        public static TabularData FromCsv(string text, char? delimiter = null, bool hasHeader = true)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var rows = ParseCsv(text, delimiter ?? DetectDelimiter(text));
            return Build(rows, hasHeader);
        }

        /// <summary>Reads a CSV file (RFC 4180: quotes, escaped quotes, comma or semicolon).</summary>
        public static TabularData FromCsvFile(string path, char? delimiter = null, bool hasHeader = true)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return FromCsv(File.ReadAllText(path), delimiter, hasHeader);
        }

        // ------------------------------------------------------------------ XLSX

        /// <summary>Reads an .xlsx file (one sheet; values only, no formulas or styles beyond dates).</summary>
        public static TabularData FromXlsx(byte[] data, string? sheetName = null, bool hasHeader = true)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            var shared = ReadSharedStrings(zip);
            var styles = ReadDateStyles(zip);
            var grid = ReadSheet(zip, sheetName, shared, styles);
            return Build(grid, hasHeader);
        }

        /// <summary>Reads an .xlsx file from disk.</summary>
        public static TabularData FromXlsxFile(string path, string? sheetName = null, bool hasHeader = true)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return FromXlsx(File.ReadAllBytes(path), sheetName, hasHeader);
        }

        // ------------------------------------------------------------------ build

        private static TabularData Build(List<List<string>> rows, bool hasHeader)
        {
            var columns = new List<string>();
            var keys = new List<string>();
            var width = 0;
            foreach (var row in rows)
            {
                width = Math.Max(width, row.Count);
            }

            for (var i = 0; i < width; i++)
            {
                var header = hasHeader && rows.Count > 0 && i < rows[0].Count && !string.IsNullOrWhiteSpace(rows[0][i])
                    ? rows[0][i].Trim()
                    : "Column" + (i + 1);
                columns.Add(header);
                var key = Sanitize(header, i);
                var unique = key;
                for (var n = 2; keys.Contains(unique); n++)
                {
                    unique = key + "_" + n.ToString(CultureInfo.InvariantCulture);
                }

                keys.Add(unique);
            }

            var data = new List<Dictionary<string, object?>>();
            for (var r = hasHeader ? 1 : 0; r < rows.Count; r++)
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < keys.Count; c++)
                {
                    dict[keys[c]] = c < rows[r].Count ? rows[r][c] : null;
                }

                data.Add(dict);
            }

            return new TabularData
            {
                Columns = columns,
                Fields = keys,
                Rows = data,
                Aligns = AlignsFor(keys, data),
                Totals = TotalsFor(keys, data),
            };
        }

        private static List<string> AlignsFor(List<string> keys, List<Dictionary<string, object?>> data)
        {
            var aligns = new List<string>(keys.Count);
            foreach (var key in keys)
            {
                var numeric = false;
                var all = true;
                foreach (var row in data)
                {
                    var text = row[key] as string;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    if (!TryNumber(text!, out _))
                    {
                        all = false;
                        break;
                    }

                    numeric = true;
                }

                aligns.Add(numeric && all ? "right" : "left");
            }

            return aligns;
        }

        private static Dictionary<string, decimal> TotalsFor(List<string> keys, List<Dictionary<string, object?>> data)
        {
            var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var sum = 0m;
                var numeric = false;
                var all = true;
                foreach (var row in data)
                {
                    var text = row[key] as string;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    if (!TryNumber(text!, out var n))
                    {
                        all = false;
                        break;
                    }

                    sum += n;
                    numeric = true;
                }

                if (numeric && all)
                {
                    totals[key] = sum;
                }
            }

            return totals;
        }

        private static bool TryNumber(string text, out decimal value) =>
            decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

        private static List<List<string>> ParseCsv(string text, char delimiter)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == delimiter)
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        continue; // CRLF: the LF ends the row
                    }

                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else if (c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private static char DetectDelimiter(string text)
        {
            var lineEnd = text.IndexOfAny(new[] { '\r', '\n' });
            var line = lineEnd >= 0 ? text.Substring(0, lineEnd) : text;
            var commas = 0;
            var semicolons = 0;
            var tabs = 0;
            var quoted = false;
            foreach (var c in line)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (!quoted)
                {
                    if (c == ',') commas++;
                    else if (c == ';') semicolons++;
                    else if (c == '\t') tabs++;
                }
            }

            if (tabs > commas && tabs > semicolons) return '\t';
            if (semicolons > commas) return ';';
            return ',';
        }

        private static string Sanitize(string name, int index)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_'); // one underscore per run of punctuation: "No. Faktur" → No_Faktur
                }
            }

            var result = sb.ToString().Trim('_');
            if (result.Length == 0)
            {
                result = "Column" + (index + 1);
            }

            if (!char.IsLetter(result[0]))
            {
                result = "_" + result;
            }

            return result;
        }

        // ------------------------------------------------------------------ OOXML

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return list;
            }

            using var stream = entry.Open();
            foreach (var si in Load(stream).Root!.Elements().Where(e => e.Name.LocalName == "si"))
            {
                var sb = new StringBuilder();
                foreach (var t in si.Descendants().Where(e => e.Name.LocalName == "t"))
                {
                    sb.Append(t.Value);
                }

                list.Add(sb.ToString());
            }

            return list;
        }

        private static HashSet<int> ReadDateStyles(ZipArchive zip)
        {
            var dates = new HashSet<int>();
            var entry = zip.GetEntry("xl/styles.xml");
            if (entry == null)
            {
                return dates;
            }

            XDocument doc;
            using (var stream = entry.Open())
            {
                doc = Load(stream);
            }

            var custom = new Dictionary<int, string>();
            var numFmts = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "numFmts");
            if (numFmts != null)
            {
                foreach (var fmt in numFmts.Elements().Where(e => e.Name.LocalName == "numFmt"))
                {
                    if (int.TryParse(fmt.Attribute("numFmtId")?.Value, out var id))
                    {
                        custom[id] = fmt.Attribute("formatCode")?.Value ?? string.Empty;
                    }
                }
            }

            var cellXfs = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "cellXfs");
            if (cellXfs != null)
            {
                var xfIndex = 0;
                foreach (var xf in cellXfs.Elements().Where(e => e.Name.LocalName == "xf"))
                {
                    if (int.TryParse(xf.Attribute("numFmtId")?.Value, out var id) && (IsBuiltInDate(id) || (custom.TryGetValue(id, out var code) && IsDateCode(code))))
                    {
                        dates.Add(xfIndex);
                    }

                    xfIndex++;
                }
            }

            return dates;
        }

        private static bool IsBuiltInDate(int id) => (id >= 14 && id <= 22) || (id >= 45 && id <= 47);

        private static bool IsDateCode(string code)
        {
            var c = code.ToLowerInvariant();
            return c.Contains("y") || c.Contains("d") || (c.Contains("h") && c.Contains("s"));
        }

        private static List<List<string>> ReadSheet(ZipArchive zip, string? sheetName, List<string> shared, HashSet<int> dateStyles)
        {
            var path = ResolveSheetPath(zip, sheetName);
            var grid = new List<List<string>>();
            var byRow = new Dictionary<int, Dictionary<int, string>>();
            var maxRow = -1;
            var maxCol = -1;

            var entry = zip.GetEntry(path);
            if (entry == null)
            {
                return grid;
            }

            XDocument doc;
            using (var stream = entry.Open())
            {
                doc = Load(stream);
            }

            foreach (var cell in doc.Descendants().Where(e => e.Name.LocalName == "c"))
            {
                var reference = cell.Attribute("r")?.Value;
                if (reference == null || reference.Length == 0)
                {
                    continue;
                }

                var col = ColumnIndex(reference);
                var row = RowIndex(reference);
                if (col < 0 || row < 0)
                {
                    continue;
                }

                var type = cell.Attribute("t")?.Value;
                var value = CellValue(cell, type, shared, dateStyles);
                if (value == null)
                {
                    continue;
                }

                if (!byRow.TryGetValue(row, out var cells))
                {
                    cells = new Dictionary<int, string>();
                    byRow[row] = cells;
                }

                cells[col] = value;
                maxRow = Math.Max(maxRow, row);
                maxCol = Math.Max(maxCol, col);
            }

            for (var r = 0; r <= maxRow; r++)
            {
                var row = new List<string>();
                byRow.TryGetValue(r, out var cells);
                for (var c = 0; c <= maxCol; c++)
                {
                    row.Add(cells != null && cells.TryGetValue(c, out var v) ? v : string.Empty);
                }

                grid.Add(row);
            }

            return grid;
        }

        private static string? CellValue(XElement cell, string? type, List<string> shared, HashSet<int> dateStyles)
        {
            var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
            switch (type)
            {
                case "s":
                    return value != null && int.TryParse(value, out var index) && index >= 0 && index < shared.Count ? shared[index] : string.Empty;
                case "inlineStr":
                    return string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value));
                case "str":
                    return value ?? string.Empty;
                case "b":
                    return value == "1" ? "true" : "false";
                case "d":
                    // ISO 8601 date cell (ECMA-376 18.18.11).
                    return value != null && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)
                        ? iso.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : value ?? string.Empty;
                case "e":
                    return value ?? string.Empty;
                default:
                    if (value == null)
                    {
                        return string.Empty;
                    }

                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        var style = int.TryParse(cell.Attribute("s")?.Value, out var s) ? s : -1;
                        if (dateStyles.Contains(style))
                        {
                            try
                            {
                                return DateTime.FromOADate(number).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                            }
                            catch (ArgumentException)
                            {
                                // out of range: keep the number
                            }
                        }

                        return number.ToString("0.############", CultureInfo.InvariantCulture);
                    }

                    return value;
            }
        }

        private static string ResolveSheetPath(ZipArchive zip, string? sheetName)
        {
            var workbook = zip.GetEntry("xl/workbook.xml");
            var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbook == null)
            {
                return "xl/worksheets/sheet1.xml";
            }

            XDocument doc;
            using (var stream = workbook.Open())
            {
                doc = Load(stream);
            }

            var sheet = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "sheet" &&
                (sheetName == null || string.Equals(e.Attribute("name")?.Value, sheetName, StringComparison.OrdinalIgnoreCase)));
            var id = sheet?.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))?.Value;
            if (id != null && rels != null)
            {
                XDocument relDoc;
                using (var stream = rels.Open())
                {
                    relDoc = Load(stream);
                }

                var rel = relDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Relationship" && e.Attribute("Id")?.Value == id);
                var target = rel?.Attribute("Target")?.Value;
                if (!string.IsNullOrEmpty(target))
                {
                    return SheetEntry(target!);
                }
            }

            return "xl/worksheets/sheet1.xml";
        }

        /// <summary>Relationship targets are relative to xl/ ("worksheets/sheet1.xml"), absolute ("/xl/…") or already rooted.</summary>
        private static string SheetEntry(string target)
        {
            var path = target.Replace('\\', '/');
            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                return path.TrimStart('/');
            }

            return path.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? path : "xl/" + path;
        }

        private static int ColumnIndex(string reference)
        {
            var index = 0;
            foreach (var c in reference)
            {
                if (c >= 'A' && c <= 'Z')
                {
                    index = index * 26 + (c - 'A' + 1);
                }
                else if (c >= 'a' && c <= 'z')
                {
                    index = index * 26 + (c - 'a' + 1);
                }
                else
                {
                    break;
                }
            }

            return index - 1;
        }

        private static int RowIndex(string reference)
        {
            var start = 0;
            while (start < reference.Length && (reference[start] < '0' || reference[start] > '9'))
            {
                start++;
            }

            return start < reference.Length && int.TryParse(reference.Substring(start), out var row) ? row - 1 : -1;
        }

        private static XDocument Load(Stream stream)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader);
        }
    }
}
