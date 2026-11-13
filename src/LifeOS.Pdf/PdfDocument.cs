using System;
using System.Collections.Generic;
using System.IO;
using LifeOS.Pdf.Internal;

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
