using LifeOS.Pdf.BarcodeScanning;
using LifeOS.Pdf.Barcodes;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class ScanningTests
    {
        [Fact]
        public void QrCode_RoundTrip_ReadsBack()
        {
            var qr = QrCode.Encode("https://github.com/Fajararifk/LifeOS-Pdf");
            var png = qr.ToPng(moduleSize: 8);

            var reader = new BarcodeReader();
            var result = reader.ReadPng(png);

            Assert.NotNull(result);
            Assert.Equal("https://github.com/Fajararifk/LifeOS-Pdf", result!.Text);
            Assert.Equal(BarcodeFormat.QrCode, result.Format);
        }

        [Fact]
        public void Code128_RoundTrip_ReadsBack()
        {
            var barcode = Barcode.Code128("LIFEOS-42X");
            var png = barcode.ToPng(moduleWidth: 3, height: 60);

            var result = new BarcodeReader().ReadPng(png);

            Assert.NotNull(result);
            Assert.Equal("LIFEOS-42X", result!.Text);
        }

        [Fact]
        public void EmptyImage_ReturnsNull()
        {
            var png = QrCode.Encode("x").ToPng(moduleSize: 8);
            // A blank image (all white) has no barcode.
            var blank = new byte[png.Length];
            blank[0] = 0x89; // corrupt signature → decode fails
            Assert.Null(new BarcodeReader().ReadPng(blank));
        }
    }
}
