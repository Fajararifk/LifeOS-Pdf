namespace LifeOS.Pdf.BarcodeScanning
{
    /// <summary>A decoded barcode or QR code.</summary>
    public sealed class BarcodeReadResult
    {
        public BarcodeReadResult(string text, BarcodeFormat format)
        {
            Text = text;
            Format = format;
        }

        /// <summary>The decoded payload.</summary>
        public string Text { get; }

        public BarcodeFormat Format { get; }
    }
}
