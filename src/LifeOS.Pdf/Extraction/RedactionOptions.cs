using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Extraction
{
    /// <summary>Options for <see cref="PdfRedactor"/>.</summary>
    public sealed class RedactionOptions
    {
        /// <summary>Colour of the box drawn over each area (default black); null draws nothing.</summary>
        public PdfColor? FillColor { get; set; } = PdfColor.Black;

        /// <summary>Extra margin around search matches in points (default 1).</summary>
        public double Padding { get; set; } = 1;

        /// <summary>
        /// <see cref="PdfRedactor.RedactText"/> also replaces the text in the document information
        /// (title, subject, keywords …) and bookmark titles. Default true.
        /// </summary>
        public bool ScrubMetadata { get; set; } = true;

        /// <summary>What replaces scrubbed text in metadata and bookmarks.</summary>
        public string Replacement { get; set; } = "[redacted]";
    }
}
