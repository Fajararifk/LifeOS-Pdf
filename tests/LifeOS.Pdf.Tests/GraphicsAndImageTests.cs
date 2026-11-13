using System;
using System.IO;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class GraphicsAndImageTests
    {
        private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

        private static string Draw(Action<PdfGraphics> draw)
        {
            var doc = new PdfDocument();
            draw(doc.AddPage(new PageSize(200, 100)).Graphics);
            return PdfStructure.Verify(doc.ToArray()).PageContents()[0];
        }

        [Fact]
        public void Rectangle_ConvertsTopLeftToPdfCoordinates()
        {
            var content = Draw(g => g.FillRectangle(PdfColor.Black, 10, 20, 30, 40));

            // PDF y of the bottom edge = 100 - 20 - 40 = 40
            Assert.Contains("10 40 30 40 re f\n", content);
        }

        [Fact]
        public void StrokeAndFill_UseB_Operator()
        {
            var content = Draw(g => g.DrawRectangle(new PdfPen(PdfColor.Red, 2), PdfColor.White, 0, 0, 10, 10));

            Assert.Contains("2 w", content);
            Assert.Contains(" re B\n", content);
        }

        [Fact]
        public void DashedPen_WritesDashPattern()
        {
            var content = Draw(g => g.DrawLine(new PdfPen(PdfColor.Gray).WithDash(4, 2), 0, 0, 100, 0));

            Assert.Contains("[4 2] 0 d", content);
        }

        [Fact]
        public void EveryOperationIsIsolatedInSaveRestore()
        {
            var content = Draw(g =>
            {
                g.DrawLine(new PdfPen(PdfColor.Black), 0, 0, 10, 10);
                g.DrawEllipse(null, PdfColor.Green, 0, 0, 20, 20);
                g.DrawRoundedRectangle(new PdfPen(PdfColor.Blue), null, 0, 0, 50, 30, 5);
            });

            Assert.Equal(3, CountOf(content, "q\n"));
            Assert.Equal(3, CountOf(content, "Q\n"));
            Assert.Equal(8, CountOf(content, " c\n")); // 4 curves ellipse + 4 corners
        }

        [Fact]
        public void NothingToPaint_Throws()
        {
            var doc = new PdfDocument();
            var g = doc.AddPage().Graphics;

            Assert.Throws<ArgumentException>(() => g.DrawRectangle(null, null, 0, 0, 10, 10));
        }

        [Fact]
        public void InvalidCoordinates_Throw()
        {
            var g = new PdfDocument().AddPage().Graphics;

            Assert.Throws<ArgumentException>(() => g.DrawLine(new PdfPen(PdfColor.Black), double.NaN, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.FillRectangle(PdfColor.Black, 0, 0, -5, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPen(PdfColor.Black, -1));
        }

        [Theory]
        [InlineData("rgb.jpg", 3, "/DeviceRGB")]
        [InlineData("gray.jpg", 1, "/DeviceGray")]
        [InlineData("cmyk.jpg", 4, "/DeviceCMYK")]
        public void Jpeg_IsEmbeddedWithCorrectHeader(string file, int components, string colorSpace)
        {
            var image = PdfImage.FromFile(Asset(file));
            Assert.Equal(64, image.PixelWidth);
            Assert.Equal(32, image.PixelHeight);
            Assert.Equal(components, image.Components);

            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawImage(image, 10, 10, 128);
            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Contains("/Subtype /Image /Width 64 /Height 32 /ColorSpace " + colorSpace, pdf.Text);
            Assert.Contains("/Filter /DCTDecode", pdf.Text);
            Assert.Contains("128 0 0 64 10 ", pdf.PageContents()[0]); // height kept at 2:1 ratio
            Assert.Contains("/Im1 Do", pdf.PageContents()[0]);
        }

        [Fact]
        public void CmykJpegFromPhotoshopStyleEncoder_GetsDecodeArray()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawImage(PdfImage.FromFile(Asset("cmyk.jpg")), 0, 0, 64, 32);

            Assert.Contains("/Decode [1 0 1 0 1 0 1 0]", PdfStructure.Verify(doc.ToArray()).Text);
        }

        [Fact]
        public void Jpeg_IsEmbeddedByteForByte()
        {
            var bytes = File.ReadAllBytes(Asset("rgb.jpg"));
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawImage(PdfImage.FromBytes(bytes), 0, 0, 64, 32);
            var pdf = PdfStructure.Verify(doc.ToArray());

            foreach (var obj in pdf.Objects)
            {
                if (obj.Value.Contains("/Subtype /Image"))
                {
                    Assert.Equal(bytes, pdf.StreamData(obj.Key));
                    return;
                }
            }

            Assert.Fail("Image object not found");
        }

        [Fact]
        public void SameImage_IsStoredOnce()
        {
            var image = PdfImage.FromFile(Asset("rgb.jpg"));
            var doc = new PdfDocument();
            for (var i = 0; i < 3; i++)
            {
                var g = doc.AddPage().Graphics;
                g.DrawImage(image, 0, 0, 64, 32);
                g.DrawImage(image, 100, 0, 64, 32);
            }

            Assert.Equal(1, PdfStructure.Verify(doc.ToArray()).CountObjects("/Subtype /Image"));
        }

        [Fact]
        public void NonJpeg_IsRejectedWithClearMessage()
        {
            var ex = Assert.Throws<NotSupportedException>(() => PdfImage.FromFile(Asset("not-a-jpeg.png")));
            Assert.Contains("JPEG", ex.Message);
        }

        [Fact]
        public void TruncatedJpeg_IsRejected()
        {
            Assert.Throws<FormatException>(() => PdfImage.FromBytes(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }));
        }

        [Fact]
        public void Color_FromHex()
        {
            Assert.Equal(new PdfColor(0x12, 0xAB, 0xEF), PdfColor.FromHex("#12abef"));
            Assert.Equal(new PdfColor(0xFF, 0x00, 0xCC), PdfColor.FromHex("f0c"));
            Assert.Throws<FormatException>(() => PdfColor.FromHex("#12345"));
        }

        [Fact]
        public void PageSize_Validation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PageSize(1, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PageSize(100, 20000));
            Assert.True(PageSize.A4.Landscape.Width > PageSize.A4.Landscape.Height);
        }

        private static int CountOf(string haystack, string needle)
        {
            var n = 0;
            var i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                n++;
                i += needle.Length;
            }

            return n;
        }
    }
}
