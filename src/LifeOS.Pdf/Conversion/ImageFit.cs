namespace LifeOS.Pdf.Conversion
{
    /// <summary>How an image is placed in the page area.</summary>
    public enum ImageFit
    {
        /// <summary>Whole image visible, centered; empty space may remain.</summary>
        Contain,

        /// <summary>Area completely filled, centered; edges of the image may be cut off.</summary>
        Cover,
    }
}
