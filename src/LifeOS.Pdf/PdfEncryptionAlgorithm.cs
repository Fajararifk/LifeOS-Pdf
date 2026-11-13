namespace LifeOS.Pdf
{
    /// <summary>The encryption algorithm used when writing an encrypted PDF.</summary>
    public enum PdfEncryptionAlgorithm
    {
        /// <summary>RC4, 128-bit key (Revision 3).</summary>
        RC4_128,

        /// <summary>AES-128 in CBC mode (Revision 4). Recommended for compatibility.</summary>
        AES_128,

        /// <summary>AES-256 (Revision 6, PDF 2.0). Strongest, needs a modern viewer.</summary>
        AES_256,
    }
}
