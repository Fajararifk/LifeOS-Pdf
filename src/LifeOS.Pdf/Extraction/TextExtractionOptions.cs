namespace LifeOS.Pdf.Extraction
{
    /// <summary>Options for <see cref="PdfPage.ExtractText"/>.</summary>
    public sealed class TextExtractionOptions
    {
        /// <summary>Only text whose centre lies in this area (page as displayed, top-left origin).</summary>
        public PdfRect? Area { get; set; }

        /// <summary>Keep the horizontal arrangement with spaces (like a fixed-width printout; good for tables).</summary>
        public bool Layout { get; set; }

        /// <summary>
        /// Read multi-column pages column by column (finds empty vertical gutters). Off by default
        /// because tables would otherwise be read column by column too.
        /// </summary>
        public bool DetectColumns { get; set; }

        /// <summary>Include invisible text (rendering mode 3, e.g. the OCR layer of scans). Default true.</summary>
        public bool IncludeInvisibleText { get; set; } = true;
    }
}
