namespace LifeOS.Pdf.Extraction
{
    /// <summary>File format <see cref="ExtractedImage.GetBytes"/> returns.</summary>
    public enum ExtractedImageFormat
    {
        /// <summary>Decoded by LifeOS.Pdf and written as PNG.</summary>
        Png,

        /// <summary>JPEG data exactly as stored in the PDF.</summary>
        Jpeg,

        /// <summary>JPEG 2000 codestream as stored.</summary>
        Jpeg2000,

        /// <summary>Raw JBIG2 data (embedded globals are not included).</summary>
        Jbig2,

        /// <summary>Raw CCITT fax data.</summary>
        Ccitt,

        /// <summary>A filter or colour space that cannot be exported.</summary>
        Unsupported,
    }
}
