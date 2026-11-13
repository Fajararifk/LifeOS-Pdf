namespace LifeOS.Pdf.Barcodes
{
    /// <summary>Linear (1D) barcode types supported by <see cref="Barcode"/>.</summary>
    public enum BarcodeSymbology
    {
        /// <summary>Any ASCII text; compact for digits. Shipping labels, invoices, internal IDs.</summary>
        Code128,

        /// <summary>Upper-case letters, digits and - . $ / + % space. Older industrial labels.</summary>
        Code39,

        /// <summary>13-digit retail product code (international).</summary>
        Ean13,

        /// <summary>8-digit retail code for small packages.</summary>
        Ean8,

        /// <summary>12-digit retail code (North America).</summary>
        UpcA,

        /// <summary>Interleaved 2 of 5: an even number of digits.</summary>
        Itf,

        /// <summary>14-digit shipping container code (GTIN-14) in ITF.</summary>
        Itf14,
    }
}
