using System;
using System.Collections.Generic;
using System.IO;
using LifeOS.Pdf.Layout.Engine;

namespace LifeOS.Pdf.Layout
{
    /// <summary>
    /// A document described as flowing content (paragraphs, tables, lists, images) that
    /// the layout engine paginates. Render it to a <see cref="PdfDocument"/> or save directly.
    /// <code>
    /// var doc = new FlowDocument();
    /// var section = doc.AddSection();
    /// section.AddHeading("Ringkasan");
    /// section.AddParagraph("Total pengeluaran bulan ini Rp1.924.500.");
    /// doc.Save("laporan.pdf");
    /// </code>
    /// </summary>
    public sealed class FlowDocument
    {
        private readonly List<Section> _sections = new List<Section>();

        public PdfDocumentInfo Info { get; } = new PdfDocumentInfo();

        public DocumentStyles Styles { get; } = new DocumentStyles();

        public IReadOnlyList<Section> Sections => _sections;

        /// <summary>Adds a section (new page). Without a setup, A4 portrait with 2 cm margins is used.</summary>
        public Section AddSection(PageSetup? setup = null)
        {
            var section = new Section(setup ?? new PageSetup());
            _sections.Add(section);
            return section;
        }

        /// <summary>Lays out the content and returns the resulting PDF document (pages can still be added).</summary>
        public PdfDocument Render()
        {
            if (_sections.Count == 0)
            {
                throw new InvalidOperationException("The document has no sections. Call AddSection() first.");
            }

            var pdf = new PdfDocument();
            CopyInfo(pdf.Info);
            new Renderer(this).Render(pdf);
            return pdf;
        }

        public void Save(string path, PdfSaveOptions? options = null) => Render().Save(path, options);

        public void Save(Stream stream, PdfSaveOptions? options = null) => Render().Save(stream, options);

        private void CopyInfo(PdfDocumentInfo target)
        {
            target.Title = Info.Title;
            target.Author = Info.Author;
            target.Subject = Info.Subject;
            target.Keywords = Info.Keywords;
            target.Creator = Info.Creator;
            target.Producer = Info.Producer;
            target.CreationDate = Info.CreationDate;
            target.ModificationDate = Info.ModificationDate;
        }
    }
}
