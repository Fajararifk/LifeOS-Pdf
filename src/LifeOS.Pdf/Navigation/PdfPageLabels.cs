using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LifeOS.Pdf.Navigation
{
    /// <summary>
    /// Page labels: the page "numbers" a viewer shows (e.g. i, ii for a cover and
    /// table of contents, then 1, 2, 3 for the body). Each range starts at a page index
    /// and runs until the next range.
    /// </summary>
    public sealed class PdfPageLabels
    {
        private readonly SortedDictionary<int, PageLabelRange> _ranges = new SortedDictionary<int, PageLabelRange>();

        public int Count => _ranges.Count;

        internal IEnumerable<KeyValuePair<int, PageLabelRange>> Ranges => _ranges;

        /// <summary>Starts a label range at <paramref name="startPageIndex"/> (0-based). Replaces a range at the same page.</summary>
        /// <param name="startPageIndex">First page of the range (0 = first page of the document).</param>
        /// <param name="style">Numbering style.</param>
        /// <param name="prefix">Text before the number, e.g. "A-" for A-1, A-2.</param>
        /// <param name="firstNumber">Number of the first page in the range (1 or more).</param>
        public void Add(int startPageIndex, PageLabelStyle style, string? prefix = null, int firstNumber = 1)
        {
            if (startPageIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startPageIndex), startPageIndex, "Page index must not be negative.");
            }

            if (firstNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(firstNumber), firstNumber, "Page labels start at 1 or higher.");
            }

            _ranges[startPageIndex] = new PageLabelRange(style, prefix, firstNumber);
        }

        public void Clear() => _ranges.Clear();

        /// <summary>The label a viewer shows for the page at <paramref name="pageIndex"/>.</summary>
        public string GetLabel(int pageIndex)
        {
            if (pageIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "Page index must not be negative.");
            }

            var start = _ranges.Keys.Where(k => k <= pageIndex).DefaultIfEmpty(-1).Max();
            if (start < 0)
            {
                return (pageIndex + 1).ToString(CultureInfo.InvariantCulture); // no range yet: plain page number
            }

            var range = _ranges[start];
            var number = range.FirstNumber + pageIndex - start;
            return (range.Prefix ?? string.Empty) + Format(range.Style, number);
        }

        internal static string Format(PageLabelStyle style, int n)
        {
            switch (style)
            {
                case PageLabelStyle.Decimal: return n.ToString(CultureInfo.InvariantCulture);
                case PageLabelStyle.UpperRoman: return Roman(n);
                case PageLabelStyle.LowerRoman: return Roman(n).ToLowerInvariant();
                case PageLabelStyle.UpperLetters: return Letters(n);
                case PageLabelStyle.LowerLetters: return Letters(n).ToLowerInvariant();
                default: return string.Empty;
            }
        }

        private static string Roman(int n)
        {
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new StringBuilder();
            for (var i = 0; i < values.Length; i++)
            {
                while (n >= values[i])
                {
                    sb.Append(symbols[i]);
                    n -= values[i];
                }
            }

            return sb.ToString();
        }

        // ISO 32000-1 §12.4.2: A–Z, then AA–ZZ, AAA–ZZZ (letter repeated).
        private static string Letters(int n) => new string((char)('A' + (n - 1) % 26), (n - 1) / 26 + 1);
    }

    internal sealed class PageLabelRange
    {
        public PageLabelRange(PageLabelStyle style, string? prefix, int firstNumber)
        {
            Style = style;
            Prefix = prefix;
            FirstNumber = firstNumber;
        }

        public PageLabelStyle Style { get; }

        public string? Prefix { get; }

        public int FirstNumber { get; }
    }
}
