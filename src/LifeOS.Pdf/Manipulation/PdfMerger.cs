using System;
using System.Collections.Generic;
using System.IO;
using LifeOS.Pdf.Navigation;

namespace LifeOS.Pdf.Manipulation
{
    /// <summary>
    /// Combines several PDFs into one, keeping each page's content, annotations and rotation, and
    /// (optionally) the bookmarks of every input under a bookmark per input.
    /// <code>
    /// PdfMerger.Merge(new[] { "invoice.pdf", "receipt.pdf" }).Save("all.pdf");
    /// </code>
    /// </summary>
    public static class PdfMerger
    {
        /// <summary>Merges files in the given order.</summary>
        public static PdfDocument Merge(IEnumerable<string> paths, MergeOptions? options = null)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            options = options ?? new MergeOptions();
            var result = new PdfDocument();
            var count = 0;
            foreach (var path in paths)
            {
                var doc = PdfDocument.Open(path, options.ReadOptions);
                Append(result, doc, Path.GetFileNameWithoutExtension(path), options);
                count++;
            }

            RequireInput(count);
            return result;
        }

        /// <summary>Merges documents (opened or created in memory) in the given order.</summary>
        public static PdfDocument Merge(IEnumerable<PdfDocument> documents, MergeOptions? options = null)
        {
            if (documents == null)
            {
                throw new ArgumentNullException(nameof(documents));
            }

            options = options ?? new MergeOptions();
            var result = new PdfDocument();
            var count = 0;
            foreach (var doc in documents)
            {
                if (doc == null)
                {
                    throw new ArgumentException("The list of documents contains null.", nameof(documents));
                }

                count++;
                Append(result, doc, "Document " + count, options);
            }

            RequireInput(count);
            return result;
        }

        /// <summary>Appends all pages of <paramref name="source"/> to <paramref name="target"/>, with its bookmarks.</summary>
        public static void Append(PdfDocument target, PdfDocument source, string? bookmarkTitle = null, MergeOptions? options = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            options = options ?? new MergeOptions();
            var pages = target.ImportPages(source);
            var map = new Dictionary<PdfPage, PdfPage>();
            for (var i = 0; i < pages.Count; i++)
            {
                map[source.Pages[i]] = pages[i];
            }

            Func<string, PdfDestination, PdfOutlineItem> add = target.Outline.Add;
            if (options.BookmarkPerDocument)
            {
                var title = !string.IsNullOrWhiteSpace(source.Info.Title) ? source.Info.Title! : bookmarkTitle ?? "Document";
                var parent = target.Outline.Add(title, PdfDestination.ToPage(pages[0]));
                parent.IsOpen = false;
                add = parent.Add;
            }

            if (options.KeepBookmarks)
            {
                OutlineCopier.Copy(source.Outline.Items, map, pages[0], add);
            }
        }

        private static void RequireInput(int count)
        {
            if (count == 0)
            {
                throw new ArgumentException("Nothing to merge: the list is empty.");
            }
        }
    }
}
