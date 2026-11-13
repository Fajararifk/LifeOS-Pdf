namespace LifeOS.Pdf
{
    /// <summary>Options that control how a document is written.</summary>
    public sealed class PdfSaveOptions
    {
        /// <summary>
        /// Compress page content with FlateDecode. On by default; turn it off to read the
        /// raw drawing operators in a text editor while debugging.
        /// </summary>
        public bool CompressContent { get; set; } = true;
    }
}
