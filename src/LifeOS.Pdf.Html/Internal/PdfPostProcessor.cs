namespace LifeOS.Pdf.Html.Internal
{
    /// <summary>
    /// Re-saves the browser's PDF with the LifeOS.Pdf writer. That rewrites the cross-reference table and
    /// derives /ID from the content (ISO 32000-1 §14.4, CLAUDE.md #5), replacing Skia's random ID and
    /// timestamps, and applies the metadata from the options (§14.3.3 document information dictionary).
    /// </summary>
    internal static class PdfPostProcessor
    {
        internal static byte[] Apply(byte[] pdf, HtmlToPdfOptions options, string? pageTitle, string creator)
        {
            var document = PdfDocument.Open(pdf);
            var info = document.Info;
            var title = options.Title ?? pageTitle;
            info.Title = string.IsNullOrWhiteSpace(title) ? null : title;
            info.Author = options.Author;
            info.Subject = options.Subject;
            info.Keywords = null;
            info.Creator = creator;
            info.Producer = new PdfDocumentInfo().Producer;
            if (options.CreationDate.HasValue)
            {
                info.CreationDate = options.CreationDate;
            }

            // The browser's ModDate is the moment of printing; dropping it keeps the output reproducible.
            info.ModificationDate = null;
            return document.ToArray();
        }
    }
}
