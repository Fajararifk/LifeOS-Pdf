using System;
using System.Collections.Generic;
using System.Text;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>Finds text across words and lines; whitespace in the query matches any word or line break.</summary>
    internal static class TextSearch
    {
        public static List<SearchResult> Find(List<TextLine> lines, int pageIndex, string query, bool ignoreCase)
        {
            var results = new List<SearchResult>();
            var needle = Normalize(query, ignoreCase);
            if (needle.Length == 0)
            {
                return results;
            }

            var text = new StringBuilder();
            var original = new StringBuilder();
            var owners = new List<(int Line, TextGlyph? Glyph)>();
            for (var l = 0; l < lines.Count; l++)
            {
                if (l > 0)
                {
                    Add(' ', ' ', l, null);
                }

                var words = lines[l].Words;
                for (var w = 0; w < words.Count; w++)
                {
                    if (w > 0)
                    {
                        Add(' ', ' ', l, null);
                    }

                    foreach (var g in words[w].Glyphs)
                    {
                        foreach (var c in g.Text)
                        {
                            Add(ignoreCase ? char.ToUpperInvariant(c) : c, c, l, g);
                        }
                    }
                }
            }

            void Add(char c, char raw, int line, TextGlyph? glyph)
            {
                text.Append(c);
                original.Append(raw);
                owners.Add((line, glyph));
            }

            var haystack = text.ToString();
            var at = 0;
            while (results.Count < 10000 && (at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                var rects = new List<PdfRect>();
                var byLine = new SortedDictionary<int, PdfRect>();
                for (var i = at; i < at + needle.Length; i++)
                {
                    var (line, glyph) = owners[i];
                    if (glyph == null)
                    {
                        continue;
                    }

                    byLine[line] = byLine.TryGetValue(line, out var r) ? r.Union(glyph.Bounds) : glyph.Bounds;
                }

                rects.AddRange(byLine.Values);
                if (rects.Count > 0)
                {
                    results.Add(new SearchResult(pageIndex, original.ToString(at, needle.Length), rects));
                }

                at += needle.Length;
            }

            return results;
        }

        private static string Normalize(string query, bool ignoreCase)
        {
            var sb = new StringBuilder();
            var space = false;
            foreach (var c in query.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }

                if (space && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                space = false;
                sb.Append(ignoreCase ? char.ToUpperInvariant(c) : c);
            }

            return sb.ToString();
        }
    }
}
