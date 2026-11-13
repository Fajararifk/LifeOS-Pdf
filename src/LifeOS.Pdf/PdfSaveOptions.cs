using System.IO.Compression;

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

        /// <summary>
        /// Trade-off for compressing page content: <see cref="System.IO.Compression.CompressionLevel.Optimal"/>
        /// (default, smallest files) or <see cref="System.IO.Compression.CompressionLevel.Fastest"/>
        /// (several times faster to save, files typically 10–30% larger). Output stays deterministic either way.
        /// </summary>
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

        /// <summary>
        /// Pack dictionaries into compressed object streams with a cross-reference stream (PDF 1.5,
        /// ISO 32000-1 §7.5.7–7.5.8). Files get noticeably smaller; every current viewer reads them.
        /// Off by default for maximum compatibility with very old tools.
        /// </summary>
        public bool UseObjectStreams { get; set; }

        /// <summary>Encrypt the document with a password (§7.6); null (default) writes it unencrypted.</summary>
        public PdfEncryptionOptions? Encryption { get; set; }
    }
}
