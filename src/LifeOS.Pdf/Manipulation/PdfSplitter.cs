using System;
using System.Collections.Generic;
using System.Globalization;

namespace LifeOS.Pdf.Manipulation
{
    /// <summary>
    /// Splits a PDF into several documents. Each part keeps the document information and the
    /// bookmarks that point into it.
    /// <code>
    /// var parts = PdfSplitter.SplitEvery(PdfDocument.Open("big.pdf"), 10);
    /// for (var i = 0; i &lt; parts.Count; i++) parts[i].Save($"part-{i + 1}.pdf");
    /// </code>
    /// </summary>
    public static class PdfSplitter
    {
        /// <summary>One document per page.</summary>
        public static IReadOnlyList<PdfDocument> SplitPages(PdfDocument document) => SplitEvery(document, 1);

        /// <summary>Documents of <paramref name="pagesPerPart"/> pages (the last one may be shorter).</summary>
        public static IReadOnlyList<PdfDocument> SplitEvery(PdfDocument document, int pagesPerPart)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (pagesPerPart < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pagesPerPart), pagesPerPart, "Each part needs at least one page.");
            }

            var ranges = new List<PageRange>();
            for (var start = 1; start <= document.Pages.Count; start += pagesPerPart)
            {
                var end = Math.Min(document.Pages.Count, start + pagesPerPart - 1);
                ranges.Add(PageRange.Parse(start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture)));
            }

            return Split(document, ranges);
        }

        /// <summary>One document per range, e.g. <c>SplitByRanges(doc, "1-3", "4-", "1,5")</c>.</summary>
        public static IReadOnlyList<PdfDocument> SplitByRanges(PdfDocument document, params string[] ranges)
        {
            if (ranges == null || ranges.Length == 0)
            {
                throw new ArgumentException("Give at least one page range, e.g. \"1-3\".", nameof(ranges));
            }

            var parsed = new List<PageRange>();
            foreach (var r in ranges)
            {
                parsed.Add(PageRange.Parse(r));
            }

            return Split(document, parsed);
        }

        /// <summary>A new document with the selected pages, in selection order.</summary>
        public static PdfDocument Extract(PdfDocument document, PageRange range) => Split(document, new[] { range })[0];

        private static IReadOnlyList<PdfDocument> Split(PdfDocument document, IReadOnlyList<PageRange> ranges)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            // One snapshot for all parts when the source has unsaved drawing.
            var source = AllPristine(document) ? document : PdfDocument.Open(document.ToArray());
            var parts = new List<PdfDocument>();
            foreach (var range in ranges)
            {
                var indexes = range.Resolve(document.Pages.Count);
                var part = new PdfDocument();
                CopyInfo(document.Info, part.Info);
                var map = new Dictionary<PdfPage, PdfPage>();
                foreach (var i in indexes)
                {
                    var page = part.ImportPage(source.Pages[i]);
                    if (!map.ContainsKey(document.Pages[i]))
                    {
                        map[document.Pages[i]] = page;
                    }
                }

                OutlineCopier.Copy(document.Outline.Items, map, null, part.Outline.Add);
                parts.Add(part);
            }

            return parts;
        }

        private static bool AllPristine(PdfDocument document)
        {
            foreach (var p in document.Pages)
            {
                if (!p.IsPristine)
                {
                    return false;
                }
            }

            return true;
        }

        internal static void CopyInfo(PdfDocumentInfo from, PdfDocumentInfo to)
        {
            to.Title = from.Title;
            to.Author = from.Author;
            to.Subject = from.Subject;
            to.Keywords = from.Keywords;
            to.Creator = from.Creator;
            to.CreationDate = from.CreationDate;
            to.ModificationDate = from.ModificationDate;
        }
    }
}
