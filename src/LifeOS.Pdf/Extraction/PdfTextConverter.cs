using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LifeOS.Pdf.Extraction.Engine;

namespace LifeOS.Pdf.Extraction
{
    /// <summary>
    /// Converts a PDF to plain text or to simple, semantic HTML (headings by font size, paragraphs,
    /// bold/italic, tables) — for search indexes, e-mail bodies or quick previews.
    /// </summary>
    public static class PdfTextConverter
    {
        /// <summary>Text of every page; pages are separated by a form feed.</summary>
        public static string ToText(PdfDocument document, TextExtractionOptions? options = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            return document.ExtractText(options);
        }

        /// <summary>A standalone HTML5 document with one &lt;section&gt; per page.</summary>
        public static string ToHtml(PdfDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var pages = Analyzer.Pages(document);
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>")
              .Append(Escape(document.Info.Title ?? "Document")).Append("</title>\n<style>\n")
              .Append("body{font-family:system-ui,sans-serif;max-width:52rem;margin:2rem auto;padding:0 1rem;line-height:1.5}\n")
              .Append("section.page{border-bottom:1px solid #ddd;padding-bottom:1.5rem;margin-bottom:1.5rem}\n")
              .Append("table{border-collapse:collapse;margin:1rem 0}td,th{border:1px solid #ccc;padding:.25rem .5rem;text-align:left}\n")
              .Append("</style>\n</head>\n<body>\n");
            for (var p = 0; p < pages.Count; p++)
            {
                sb.Append("<section class=\"page\" id=\"page-").Append((p + 1).ToString(CultureInfo.InvariantCulture)).Append("\">\n");
                Page(pages[p], sb);
                sb.Append("</section>\n");
            }

            sb.Append("</body>\n</html>\n");
            return sb.ToString();
        }

        private static void Page(PdfPage page, StringBuilder sb)
        {
            var lines = page.GetTextLines();
            if (lines.Count == 0)
            {
                return;
            }

            var tables = page.ExtractTables();
            var sizes = lines.SelectMany(l => l.Words).Select(w => w.FontSize).OrderBy(s => s).ToList();
            var body = sizes.Count > 0 ? sizes[sizes.Count / 2] : 10;
            var paragraph = new List<TextLine>();
            var emitted = new HashSet<ExtractedTable>();
            TextLine? previous = null;

            void Flush()
            {
                if (paragraph.Count == 0)
                {
                    return;
                }

                var size = paragraph[0].Words[0].FontSize;
                var tag = size >= body * 1.8 ? "h1" : size >= body * 1.3 ? "h2" : paragraph.All(l => l.Words.All(w => w.Bold)) && paragraph.Count == 1 ? "h3" : "p";
                sb.Append('<').Append(tag).Append('>');
                for (var i = 0; i < paragraph.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(tag == "p" ? "<br>\n" : " ");
                    }

                    Line(paragraph[i], sb, tag == "p");
                }

                sb.Append("</").Append(tag).Append(">\n");
                paragraph.Clear();
            }

            foreach (var line in lines)
            {
                var table = tables.FirstOrDefault(t => t.Bounds.Contains(line.Bounds.X + line.Bounds.Width / 2, line.Bounds.Y + line.Bounds.Height / 2));
                if (table != null)
                {
                    Flush();
                    if (emitted.Add(table))
                    {
                        Table(table, sb);
                    }

                    previous = null;
                    continue;
                }

                var size = line.Words[0].FontSize;
                if (previous != null)
                {
                    var gap = line.Bounds.Y - previous.Bounds.Bottom;
                    var sameStyle = Math.Abs(previous.Words[0].FontSize - size) < 0.5;
                    if (gap > previous.Bounds.Height * 0.9 || !sameStyle)
                    {
                        Flush();
                    }
                }

                paragraph.Add(line);
                previous = line;
            }

            Flush();
        }

        private static void Line(TextLine line, StringBuilder sb, bool inline)
        {
            for (var i = 0; i < line.Words.Count; i++)
            {
                var w = line.Words[i];
                if (i > 0)
                {
                    sb.Append(' ');
                }

                var bold = inline && w.Bold;
                var italic = inline && w.Italic;
                if (bold)
                {
                    sb.Append("<strong>");
                }

                if (italic)
                {
                    sb.Append("<em>");
                }

                sb.Append(Escape(w.Text));
                if (italic)
                {
                    sb.Append("</em>");
                }

                if (bold)
                {
                    sb.Append("</strong>");
                }
            }
        }

        private static void Table(ExtractedTable table, StringBuilder sb)
        {
            sb.Append("<table>\n");
            for (var r = 0; r < table.RowCount; r++)
            {
                var cell = r == 0 ? "th" : "td";
                sb.Append("<tr>");
                foreach (var v in table.Rows[r])
                {
                    sb.Append('<').Append(cell).Append('>').Append(Escape(v)).Append("</").Append(cell).Append('>');
                }

                sb.Append("</tr>\n");
            }

            sb.Append("</table>\n");
        }

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
