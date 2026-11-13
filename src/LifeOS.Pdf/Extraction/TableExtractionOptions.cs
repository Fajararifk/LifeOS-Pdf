namespace LifeOS.Pdf.Extraction
{
    /// <summary>How tables are found.</summary>
    public enum TableDetection
    {
        /// <summary>Ruled tables first, then text aligned in columns elsewhere on the page.</summary>
        Auto,

        /// <summary>Only tables drawn with lines (cell borders).</summary>
        Lattice,

        /// <summary>Only text aligned in columns (statements, invoices without borders).</summary>
        Stream,
    }

    /// <summary>Options for <see cref="PdfPage.ExtractTables"/>.</summary>
    public sealed class TableExtractionOptions
    {
        public TableDetection Detection { get; set; } = TableDetection.Auto;

        /// <summary>Only look in this area (page as displayed, top-left origin).</summary>
        public PdfRect? Area { get; set; }

        /// <summary>Smallest table reported (default 2 rows × 2 columns).</summary>
        public int MinRows { get; set; } = 2;

        public int MinColumns { get; set; } = 2;
    }
}
