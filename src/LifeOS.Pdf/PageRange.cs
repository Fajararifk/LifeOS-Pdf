using System;
using System.Collections.Generic;
using System.Globalization;

namespace LifeOS.Pdf
{
    /// <summary>
    /// A selection of pages written like in a print dialog: <c>"1-3,5,8-"</c>. Page numbers
    /// start at 1; <c>"8-"</c> runs to the last page, <c>"-3"</c> starts at the first, <c>"last"</c>
    /// is the last page, and a descending range such as <c>"5-1"</c> lists pages in reverse.
    /// </summary>
    public sealed class PageRange
    {
        private readonly List<(int From, int To)> _parts;

        private PageRange(List<(int From, int To)> parts, string text)
        {
            _parts = parts;
            Text = text;
        }

        /// <summary>Every page.</summary>
        public static PageRange All { get; } = new PageRange(new List<(int, int)> { (1, int.MaxValue) }, "1-");

        public string Text { get; }

        /// <summary>Parses a selection; throws <see cref="FormatException"/> with the offending part.</summary>
        public static PageRange Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new FormatException("A page range must not be empty. Use for example \"1-3,5,8-\".");
            }

            var parts = new List<(int, int)>();
            foreach (var raw in text.Split(','))
            {
                var part = raw.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                var dash = part.IndexOf('-');
                if (dash < 0)
                {
                    var n = Number(part, text);
                    parts.Add((n, n));
                    continue;
                }

                var left = part.Substring(0, dash).Trim();
                var right = part.Substring(dash + 1).Trim();
                var from = left.Length == 0 ? 1 : Number(left, text);
                var to = right.Length == 0 ? int.MaxValue : Number(right, text);
                parts.Add((from, to));
            }

            if (parts.Count == 0)
            {
                throw new FormatException("'" + text + "' selects no pages. Use for example \"1-3,5,8-\".");
            }

            return new PageRange(parts, text);
        }

        private static int Number(string part, string text)
        {
            if (string.Equals(part, "last", StringComparison.OrdinalIgnoreCase) || part == "z")
            {
                return int.MaxValue;
            }

            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
            {
                throw new FormatException("'" + part + "' in page range '" + text + "' is not a page number (pages start at 1).");
            }

            return n;
        }

        /// <summary>Zero-based page indexes for a document of <paramref name="pageCount"/> pages, in selection order.</summary>
        public IReadOnlyList<int> Resolve(int pageCount)
        {
            var result = new List<int>();
            foreach (var (fromRaw, toRaw) in _parts)
            {
                var from = Math.Min(fromRaw, pageCount);
                var to = Math.Min(toRaw, pageCount);
                if (fromRaw > pageCount && fromRaw != int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(pageCount), "Page " + fromRaw + " in range '" + Text + "' does not exist; the document has " + pageCount + " page(s).");
                }

                if (toRaw > pageCount && toRaw != int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(pageCount), "Page " + toRaw + " in range '" + Text + "' does not exist; the document has " + pageCount + " page(s).");
                }

                var step = to >= from ? 1 : -1;
                for (var p = from; ; p += step)
                {
                    result.Add(p - 1);
                    if (p == to)
                    {
                        break;
                    }
                }
            }

            return result;
        }

        public override string ToString() => Text;
    }
}
