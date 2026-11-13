using System;
using System.Collections.Generic;
using System.IO;
using LifeOS.Pdf.Attachments;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Navigation;

namespace LifeOS.Pdf
{
    /// <summary>
    /// A PDF document under construction. Add pages, draw on them, then save.
    /// <code>
    /// var doc = new PdfDocument();
    /// var page = doc.AddPage();
    /// page.Graphics.DrawText("Halo dunia", new PdfFont(StandardFont.Helvetica, 24), 72, 72);
    /// doc.Save("halo.pdf");
    /// </code>
    /// </summary>
    public sealed class PdfDocument
    {
        private readonly List<PdfPage> _pages = new List<PdfPage>();

        /// <summary>Title, author and other properties.</summary>
        public PdfDocumentInfo Info { get; } = new PdfDocumentInfo();

        public IReadOnlyList<PdfPage> Pages => _pages;

        /// <summary>Bookmarks shown in the viewer's outline panel.</summary>
        public PdfOutline Outline { get; } = new PdfOutline();

        /// <summary>Named destinations, the targets of <see cref="PdfDestination.Named"/> links.</summary>
        public IDictionary<string, PdfDestination> NamedDestinations { get; } = new SortedDictionary<string, PdfDestination>(StringComparer.Ordinal);

        /// <summary>Page numbers shown by the viewer (i, ii, 1, 2 …).</summary>
        public PdfPageLabels PageLabels { get; } = new PdfPageLabels();

        /// <summary>How the document is presented when opened.</summary>
        public PdfViewerPreferences ViewerPreferences { get; } = new PdfViewerPreferences();

        /// <summary>Files embedded in the document.</summary>
        public PdfAttachmentCollection Attachments { get; } = new PdfAttachmentCollection();

        internal ResourceRegistry Resources { get; } = new ResourceRegistry();

        /// <summary>Adds an A4 portrait page.</summary>
        public PdfPage AddPage() => AddPage(PageSize.A4);

        /// <summary>Adds a page of the given size.</summary>
        public PdfPage AddPage(PageSize size)
        {
            if (size.Width <= 0 || size.Height <= 0)
            {
                // default(PageSize) bypasses the constructor's validation.
                throw new ArgumentException("Page size is not initialized. Use PageSize.A4 or new PageSize(w, h).", nameof(size));
            }

            var page = new PdfPage(this, size);
            _pages.Add(page);
            return page;
        }

        /// <summary>Inserts a new blank page at <paramref name="index"/> (0 = first).</summary>
        public PdfPage InsertPage(int index, PageSize size)
        {
            RequireInsertIndex(index);
            var page = AddPage(size);
            _pages.RemoveAt(_pages.Count - 1);
            _pages.Insert(index, page);
            return page;
        }

        // ------------------------------------------------------------------ opening & editing

        /// <summary>Opens an existing PDF. Its pages can be edited, drawn on, removed or copied to other documents.</summary>
        public static PdfDocument Open(string path, PdfReadOptions? options = null)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return Open(File.ReadAllBytes(path), options);
        }

        /// <summary>Opens an existing PDF from a stream (read to the end; the stream is not closed).</summary>
        public static PdfDocument Open(Stream stream, PdfReadOptions? options = null)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return Open(ms.ToArray(), options);
            }
        }

        /// <summary>Opens an existing PDF from bytes. The array is used as is; do not change it while the document is in use.</summary>
        public static PdfDocument Open(byte[] data, PdfReadOptions? options = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var parser = new Parsing.PdfParser(data, options ?? new PdfReadOptions());
            var source = Parsing.SourceDocument.Load(parser);
            var doc = new PdfDocument();
            doc.Info.Title = source.InfoText("Title");
            doc.Info.Author = source.InfoText("Author");
            doc.Info.Subject = source.InfoText("Subject");
            doc.Info.Keywords = source.InfoText("Keywords");
            doc.Info.Creator = source.InfoText("Creator");
            doc.Info.CreationDate = source.InfoDate("CreationDate");
            doc.Info.ModificationDate = source.InfoDate("ModDate");
            doc.PdfVersion = parser.Version;
            foreach (var sp in source.Pages)
            {
                var m = sp.MediaBox;
                doc._pages.Add(new PdfPage(doc, PageSize.Unchecked(m[2] - m[0], m[3] - m[1]), sp));
            }

            Parsing.OutlineImporter.Import(source, doc);
            return doc;
        }

        /// <summary>PDF version from the header of an opened file (e.g. "1.4"), or null for a new document.</summary>
        public string? PdfVersion { get; private set; }

        /// <summary>
        /// Copies a page from another document (or this one) to the end. The page keeps its content,
        /// resources, annotations and rotation. Pages that were drawn on or created in memory are
        /// copied from a saved snapshot of their document.
        /// </summary>
        public PdfPage ImportPage(PdfPage page) => InsertImportedPage(_pages.Count, page);

        /// <summary>Copies a page from another document to position <paramref name="index"/>.</summary>
        public PdfPage InsertImportedPage(int index, PdfPage page)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            RequireInsertIndex(index);
            var source = Pristine(page);
            var copy = new PdfPage(this, source.Size, source.Source) { Rotation = source.Rotation };
            _pages.Insert(index, copy);
            return copy;
        }

        /// <summary>Copies pages of <paramref name="document"/> (all, or the selected <paramref name="range"/>) to the end.</summary>
        public IReadOnlyList<PdfPage> ImportPages(PdfDocument document, PageRange? range = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var indexes = (range ?? PageRange.All).Resolve(document.Pages.Count);
            var allPristine = true;
            foreach (var i in indexes)
            {
                allPristine &= document.Pages[i].IsPristine;
            }

            // One snapshot for the whole batch instead of one per page.
            var source = allPristine ? document : Open(document.ToArray());
            var result = new List<PdfPage>();
            foreach (var i in indexes)
            {
                result.Add(ImportPage(source.Pages[i]));
            }

            return result;
        }

        /// <summary>Removes the page at <paramref name="index"/>. Bookmarks to it move to the nearest remaining page.</summary>
        public void RemovePage(int index)
        {
            if (index < 0 || index >= _pages.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "The document has " + _pages.Count + " page(s).");
            }

            if (_pages.Count == 1)
            {
                throw new InvalidOperationException("A PDF needs at least one page; the last page cannot be removed.");
            }

            var removed = _pages[index];
            _pages.RemoveAt(index);
            var replacement = _pages[Math.Min(index, _pages.Count - 1)];
            Retarget(Outline.Items, removed, replacement);
            var stale = new List<string>();
            foreach (var pair in NamedDestinations)
            {
                if (ReferenceEquals(pair.Value.Page, removed))
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (var key in stale)
            {
                NamedDestinations.Remove(key);
            }

            if (ViewerPreferences.InitialPage >= _pages.Count)
            {
                ViewerPreferences.InitialPage = 0;
            }
        }

        /// <summary>Removes <paramref name="page"/> from this document.</summary>
        public void RemovePage(PdfPage page)
        {
            var index = _pages.IndexOf(page);
            if (index < 0)
            {
                throw new ArgumentException("The page does not belong to this document.", nameof(page));
            }

            RemovePage(index);
        }

        /// <summary>Moves a page from index <paramref name="from"/> to index <paramref name="to"/>.</summary>
        public void MovePage(int from, int to)
        {
            if (from < 0 || from >= _pages.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(from), from, "The document has " + _pages.Count + " page(s).");
            }

            if (to < 0 || to >= _pages.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(to), to, "The document has " + _pages.Count + " page(s).");
            }

            var page = _pages[from];
            _pages.RemoveAt(from);
            _pages.Insert(to, page);
        }

        /// <summary>An imported page as it is in its file: the page itself, or the same page in a fresh snapshot.</summary>
        internal static PdfPage Pristine(PdfPage page)
        {
            if (page.IsPristine)
            {
                return page;
            }

            var owner = page.Document;
            var index = -1;
            for (var i = 0; i < owner._pages.Count; i++)
            {
                if (ReferenceEquals(owner._pages[i], page))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                throw new ArgumentException("The page was removed from its document.", nameof(page));
            }

            return Open(owner.ToArray()).Pages[index];
        }

        private static void Retarget(IReadOnlyList<PdfOutlineItem> items, PdfPage removed, PdfPage replacement)
        {
            foreach (var item in items)
            {
                if (ReferenceEquals(item.Destination.Page, removed))
                {
                    item.Destination = PdfDestination.ToPage(replacement);
                }

                Retarget(item.Children, removed, replacement);
            }
        }

        private void RequireInsertIndex(int index)
        {
            if (index < 0 || index > _pages.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be between 0 and " + _pages.Count + ".");
            }
        }

        /// <summary>Text of all pages in reading order; pages are separated by a form feed ('\f').</summary>
        public string ExtractText(Extraction.TextExtractionOptions? options = null)
        {
            var parts = new List<string>();
            foreach (var page in Extraction.Engine.Analyzer.Pages(this))
            {
                parts.Add(page.ExtractText(options));
            }

            return string.Join("\f", parts);
        }

        /// <summary>Finds <paramref name="text"/> on every page.</summary>
        public IReadOnlyList<Extraction.SearchResult> Search(string text, bool ignoreCase = true)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var results = new List<Extraction.SearchResult>();
            var pages = Extraction.Engine.Analyzer.Pages(this);
            for (var i = 0; i < pages.Count; i++)
            {
                results.AddRange(Extraction.Engine.TextSearch.Find(Extraction.Engine.Analyzer.For(pages[i]).Lines, i, text, ignoreCase));
            }

            return results;
        }

        /// <summary>Writes the document to a file, replacing it if it exists.</summary>
        public void Save(string path, PdfSaveOptions? options = null)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            // Build in memory first so a failure never leaves a half-written file behind.
            var bytes = ToArray(options);
            File.WriteAllBytes(path, bytes);
        }

        /// <summary>Writes the document to a stream. The stream is not closed.</summary>
        public void Save(Stream stream, PdfSaveOptions? options = null)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var bytes = ToArray(options);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Returns the complete PDF file as bytes.</summary>
        public byte[] ToArray(PdfSaveOptions? options = null)
        {
            if (_pages.Count == 0)
            {
                throw new InvalidOperationException("A PDF needs at least one page. Call AddPage() before saving.");
            }

            return DocumentWriter.Write(this, options ?? new PdfSaveOptions());
        }
    }
}
