using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>
    /// Glyph name → Unicode (§9.10.2): the Adobe Glyph List (BSD-3-Clause, embedded), plus the
    /// "uniXXXX", "uXXXX[XX]" conventions, ligature names joined with '_' and suffixes after '.'.
    /// </summary>
    internal static class GlyphList
    {
        private static Dictionary<string, string>? _table;
        private static readonly object Gate = new object();

        public static string? ToUnicode(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var dot = name.IndexOf('.');
            if (dot == 0)
            {
                return null; // ".notdef", ".null"
            }

            var baseName = dot > 0 ? name.Substring(0, dot) : name;
            if (baseName.IndexOf('_') > 0)
            {
                var sb = new StringBuilder();
                foreach (var part in baseName.Split('_'))
                {
                    var u = Single(part);
                    if (u == null)
                    {
                        return null;
                    }

                    sb.Append(u);
                }

                return sb.ToString();
            }

            return Single(baseName);
        }

        private static string? Single(string name)
        {
            if (Table.TryGetValue(name, out var known))
            {
                return known;
            }

            if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length >= 7 && (name.Length - 3) % 4 == 0)
            {
                var sb = new StringBuilder();
                for (var i = 3; i < name.Length; i += 4)
                {
                    if (!int.TryParse(name.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v) || (v >= 0xD800 && v <= 0xDFFF))
                    {
                        return null;
                    }

                    sb.Append((char)v);
                }

                return sb.ToString();
            }

            if (name.Length >= 5 && name.Length <= 7 && name[0] == 'u' &&
                int.TryParse(name.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var cp) &&
                cp <= 0x10FFFF && (cp < 0xD800 || cp > 0xDFFF))
            {
                return char.ConvertFromUtf32(cp);
            }

            return null;
        }

        private static Dictionary<string, string> Table
        {
            get
            {
                if (_table != null)
                {
                    return _table;
                }

                lock (Gate)
                {
                    if (_table == null)
                    {
                        var table = new Dictionary<string, string>(4400, StringComparer.Ordinal);
                        using (var stream = typeof(GlyphList).Assembly.GetManifestResourceStream("LifeOS.Pdf.glyphlist.txt.gz")
                            ?? throw new InvalidOperationException("Internal error: the glyph list resource is missing."))
                        using (var gz = new GZipStream(stream, CompressionMode.Decompress))
                        using (var reader = new StreamReader(gz, Encoding.ASCII))
                        {
                            string? line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                var space = line.IndexOf(' ');
                                if (space > 0)
                                {
                                    var v = int.Parse(line.Substring(space + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                                    table[line.Substring(0, space)] = ((char)v).ToString();
                                }
                            }
                        }

                        _table = table;
                    }

                    return _table;
                }
            }
        }
    }
}
