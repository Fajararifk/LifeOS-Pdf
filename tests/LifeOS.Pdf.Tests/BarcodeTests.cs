using System;
using System.Linq;
using System.Text;
using LifeOS.Pdf.Barcodes;
using LifeOS.Pdf.Barcodes.Linear;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Templates;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>
    /// Linear barcodes, QRIS and integration. Readability of every symbology is additionally
    /// verified outside .NET with zxing-cpp (tools/validate_pdf.py --expect-code).
    /// </summary>
    public class BarcodeTests
    {
        private static string Bars(Barcode b) => new string(Enumerable.Range(0, b.ModuleCount).Select(i => b.IsBar(i) ? '1' : '0').ToArray());

        [Fact]
        public void Ean13_ModulePatternMatchesTheStandard()
        {
            // 4006381333931 — a well-known valid EAN-13. Start guard, L/G digits by parity, center, R digits, end guard.
            var bars = Bars(Barcode.Ean13("4006381333931"));
            Assert.Equal(95, bars.Length);
            Assert.StartsWith("101", bars);
            Assert.EndsWith("101", bars);
            Assert.Equal("01010", bars.Substring(45, 5));
            Assert.Equal("0001101", bars.Substring(3, 7));   // '0' in L (first digit 4 → parity LGLLGG)
            Assert.Equal("0100111", bars.Substring(10, 7));  // '0' in G
        }

        [Theory]
        [InlineData("400638133393", "4006381333931")]
        [InlineData("899123456789", "8991234567891")]
        public void Ean13_CalculatesCheckDigit(string input, string expected) => Assert.Equal(expected, Barcode.Ean13(input).Text);

        [Fact]
        public void CheckDigits_AreVerified()
        {
            Assert.Contains("should be 1", Assert.Throws<ArgumentException>(() => Barcode.Ean13("4006381333930")).Message);
            Assert.Equal("036000291452", Barcode.UpcA("03600029145").Text);
            Assert.Equal("89912348", Barcode.Ean8("8991234").Text);
            Assert.Equal("18991234567898", Barcode.Itf14("1899123456789").Text);
            Assert.Equal(7, LinearEncoders.GtinCheckDigit("0000000000000") + 7);
            Assert.Throws<ArgumentException>(() => Barcode.Ean13("12345"));
            Assert.Throws<ArgumentException>(() => Barcode.Ean13("89912345678A"));
        }

        [Fact]
        public void Code128_UsesSetCForDigits_AndChecksum()
        {
            // Digits only, even count: start C + 1 symbol per digit pair + checksum + stop.
            var digits = Barcode.Code128("123456");
            Assert.Equal(11 * (1 + 3 + 1) + 13, digits.ModuleCount);

            var text = Barcode.Code128("ab");
            Assert.Equal(11 * (1 + 2 + 1) + 13, text.ModuleCount);

            // Every symbol is 11 modules wide and begins with a bar; stop ends with a 2-module bar.
            var bars = Bars(Barcode.Code128("INV-2026-0042"));
            Assert.Equal(0, (bars.Length - 13) % 11);
            Assert.EndsWith("11", bars);
            Assert.StartsWith("11010010000", bars); // start code B
        }

        [Fact]
        public void Code128_HandlesControlCharactersAndRejectsNonAscii()
        {
            Assert.True(Barcode.Code128("A\tB").ModuleCount > 0);
            Assert.Contains("ASCII", Assert.Throws<ArgumentException>(() => Barcode.Code128("Rp1.000 €")).Message);
            Assert.Throws<ArgumentException>(() => Barcode.Code128(string.Empty));
        }

        [Fact]
        public void Code39_CheckCharacterAndCharset()
        {
            Assert.Equal("LIFEOS-42X", Barcode.Code39("LIFEOS-42", checkDigit: true).Text); // 162 mod 43 = 33 → X
            Assert.Contains("upper case", Assert.Throws<ArgumentException>(() => Barcode.Code39("lower")).Message);
        }

        [Fact]
        public void Itf_NeedsEvenDigits()
        {
            Assert.Contains("leading 0", Assert.Throws<ArgumentException>(() => Barcode.Itf("123")).Message);
            Assert.Equal(4 + 2 * 18 + 5, Barcode.Itf("1234").ModuleCount); // start + 2 pairs × (5 narrow×2 + 2 wide×2×… ) + stop
        }

        [Fact]
        public void Create_ByName()
        {
            Assert.Equal(BarcodeSymbology.Ean13, Barcode.Create("EAN-13", "899123456789").Symbology);
            Assert.Equal(BarcodeSymbology.Code128, Barcode.Create("code128", "x").Symbology);
            Assert.Throws<ArgumentException>(() => Barcode.Create("pdf417", "x"));
        }

        [Fact]
        public void PngAndSvg_Export()
        {
            var code = Barcode.Code128("TEST");
            var image = Images.PdfImage.FromBytes(code.ToPng(moduleWidth: 2, height: 40));
            Assert.Equal((code.ModuleCount + 2 * code.QuietZone) * 2, image.PixelWidth);
            Assert.Equal(40, image.PixelHeight);
            Assert.Contains("<path", code.ToSvg());
        }

        [Fact]
        public void DrawBarcode_ValidPdf_AndRejectsTooLowHeight()
        {
            var doc = new PdfDocument();
            var g = doc.AddPage().Graphics;
            g.DrawBarcode(Barcode.Ean13("899123456789"), 50, 50, 200, 60);
            PdfStructure.Verify(doc.ToArray());

            Assert.Throws<ArgumentOutOfRangeException>(() => g.DrawBarcode(Barcode.Code128("x"), 0, 0, 100, 5));
            g.DrawBarcode(Barcode.Code128("x"), 0, 0, 100, 5, showText: false);
        }

        // ------------------------------------------------------------------ QRIS

        private const string StaticQris = "00020101021126570011ID.DANA.WWW011893600915012345678902091234567890303UMI51440014ID.CO.QRIS.WWW0215ID10200000000000303UMI5204581253033605802ID5920FAJAR DIGITAL STUDIO6015JAKARTA SELATAN610512160630480D9";

        [Fact]
        public void Crc16_StandardCheckValue() => Assert.Equal(0x29B1, Qris.ComputeCrc("123456789"));

        [Fact]
        public void Qris_ValidatesCrcAndStructure()
        {
            Assert.True(Qris.IsValid(StaticQris));
            Assert.False(Qris.IsValid(StaticQris.Substring(0, StaticQris.Length - 1) + "0")); // wrong CRC
            Assert.False(Qris.IsValid("hello"));
            Assert.False(Qris.IsValid(StaticQris.Substring(0, 40)));
            Assert.Equal("FAJAR DIGITAL STUDIO", Qris.Parse(StaticQris).Single(f => f.Key == "59").Value);
        }

        [Fact]
        public void Qris_WithAmount_MakesDynamicPayload()
        {
            var dynamic = Qris.WithAmount(StaticQris, 125000);
            var fields = Qris.Parse(dynamic).ToList();

            Assert.True(Qris.IsValid(dynamic));
            Assert.Equal("12", fields.Single(f => f.Key == "01").Value);
            Assert.Equal("125000", fields.Single(f => f.Key == "54").Value);
            Assert.Equal("53", fields[fields.FindIndex(f => f.Key == "54") - 1].Key); // directly after the currency
            Assert.Equal("58", fields[fields.FindIndex(f => f.Key == "54") + 1].Key);

            // Applying it again replaces the amount instead of adding a second one.
            var again = Qris.WithAmount(dynamic, 50000.5m);
            Assert.Equal("50000.5", Qris.Parse(again).Single(f => f.Key == "54").Value);
            Assert.Single(Qris.Parse(again), f => f.Key == "54");

            Assert.Throws<ArgumentOutOfRangeException>(() => Qris.WithAmount(StaticQris, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Qris.WithAmount(StaticQris, 1.005m));
            Assert.Throws<ArgumentException>(() => Qris.WithAmount("000201xx", 1000));
        }

        // ------------------------------------------------------------------ layout & templates

        [Fact]
        public void FlowDocument_QrAndBarcodeBlocks()
        {
            var doc = new FlowDocument();
            var s = doc.AddSection();
            s.AddQrCode("https://lifeos.app", 2000, ParagraphAlignment.Center); // larger than the column: shrinks, stays square
            s.AddBarcode(Barcode.Code128("X-1"), 150, 40, ParagraphAlignment.Right);

            var pdf = PdfStructure.Verify(doc.Render().ToArray());
            Assert.Single(pdf.PageContents());
        }

        [Fact]
        public void Template_QrCodeAndBarcodeElements()
        {
            var t = PdfTemplate.Parse(@"<document><section>
                <qrcode value='{{ Url }}' size='80' ecc='H' align='right'/>
                <barcode type='ean13' value='{{ Sku }}' width='160' height='50' showText='false'/>
                <qrcode value='{{ Missing }}'/>
              </section></document>");
            var section = t.Bind(new { Url = "https://lifeos.app", Sku = "899123456789" }).Sections[0];
            var blocks = section.Blocks.OfType<BarcodeBlock>().ToList();

            Assert.Equal(2, blocks.Count); // empty value = nothing drawn
            Assert.Equal(QrErrorCorrection.High, blocks[0].QrCode!.ErrorCorrection);
            Assert.Equal("8991234567891", blocks[1].Barcode!.Text);
            Assert.False(blocks[1].ShowText);

            var bad = Assert.Throws<TemplateException>(() => PdfTemplate.Parse("<document><section><barcode type='ean13' value='123'/></section></document>").Bind(null));
            Assert.Contains("EAN-13", bad.Message);
        }

        [Fact]
        public void InvoiceTemplate_ShowsQrisWhenProvided()
        {
            var data = TemplateData.FromJson("{\"Number\":\"1\",\"Seller\":{\"Name\":\"S\"},\"Customer\":{\"Name\":\"C\"},\"Items\":[],\"PaymentQr\":\"" + StaticQris + "\"}");
            var doc = BuiltInTemplates.Invoice.Bind(data);
            Assert.Contains(doc.Sections[0].Blocks.OfType<Table>(), t => t.Rows.Any(r => r.Cells.Any(c => c.Blocks.OfType<BarcodeBlock>().Any())));
        }
    }
}
