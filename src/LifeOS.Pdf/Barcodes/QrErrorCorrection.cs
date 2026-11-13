namespace LifeOS.Pdf.Barcodes
{
    /// <summary>
    /// QR error correction level: how much of the symbol may be damaged (or covered by a logo)
    /// and still be read. Higher levels make the code larger for the same data.
    /// </summary>
    public enum QrErrorCorrection
    {
        /// <summary>About 7% of codewords can be restored.</summary>
        Low,

        /// <summary>About 15% (the usual default).</summary>
        Medium,

        /// <summary>About 25%.</summary>
        Quartile,

        /// <summary>About 30% — use when a logo is placed on the code.</summary>
        High,
    }
}
