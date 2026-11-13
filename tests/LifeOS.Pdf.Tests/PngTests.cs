using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Images.Png;
using LifeOS.Pdf.Internal;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class PngTests
    {
        private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "TestAssets", "png");

        public static IEnumerable<object[]> SuiteFiles() =>
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestAssets", "png"), "*.png")
                .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

        /// <summary>
        /// Every colour type × bit depth × interlace combination, compared pixel by pixel
        /// with the reference decoded by an independent implementation (pypng).
        /// </summary>
        [Theory]
        [MemberData(nameof(SuiteFiles))]
        public void Decode_MatchesReferencePixels(string name)
        {
            var png = PngImage.Decode(File.ReadAllBytes(Path.Combine(Folder, name + ".png")), forceDecode: true);
            var expected = File.ReadAllBytes(Path.Combine(Folder, name + ".ref"));

            var actual = png.DecodedColor!.Concat(png.DecodedAlpha!).ToArray();
            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("gray8", true)]
        [InlineData("gray1", true)]
        [InlineData("rgb16", true)]
        [InlineData("pal4", true)]
        [InlineData("rgb8-i", false)]   // interlaced: must decode
        [InlineData("rgba8", false)]    // alpha: must decode
        [InlineData("pala8", false)]    // palette transparency
        [InlineData("gray8-trns", false)]
        public void FastPath_OnlyForOpaqueNonInterlaced(string name, bool fast)
        {
            Assert.Equal(fast, PngImage.Decode(File.ReadAllBytes(Path.Combine(Folder, name + ".png"))).UsedFastPath);
        }

        [Fact]
        public void PaletteImage_UsesIndexedColorSpaceAndPredictor()
        {
            var pdf = Embed("pal8");

            Assert.Contains("/ColorSpace [/Indexed /DeviceRGB 255 <", pdf.Text);
            Assert.Contains("/DecodeParms << /Predictor 15 /Colors 1 /BitsPerComponent 8 /Columns 13 >>", pdf.Text);
        }

        [Fact]
        public void AlphaImage_GetsSoftMask()
        {
            var image = PdfImage.FromFile(Path.Combine(Folder, "rgba8.png"));
            Assert.True(image.HasAlpha);
            Assert.Equal(PdfImageFormat.Png, image.Format);

            var pdf = Embed("rgba8");
            Assert.Matches(@"/SMask \d+ 0 R", pdf.Text);
            Assert.Contains("/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", pdf.Text);
        }

        [Fact]
        public void OpaqueAlphaChannel_DropsSoftMask()
        {
            // one-pixel.png has alpha 128 → mask; a fully opaque RGBA image needs none.
            Assert.True(PdfImage.FromFile(Path.Combine(Folder, "one-pixel.png")).HasAlpha);
            var opaque = BuildPng(2, 2, 6, 8, new byte[] { 0, 1, 2, 3, 255, 4, 5, 6, 255, 0, 7, 8, 9, 255, 10, 11, 12, 255 }, extraChunks: null);
            Assert.False(PdfImage.FromBytes(opaque).HasAlpha);
        }

        [Fact]
        public void SixteenBitImage_KeepsDepth()
        {
            Assert.Contains("/BitsPerComponent 16", Embed("rgba16").Text);
        }

        [Fact]
        public void Dpi_IsReadFromPhys()
        {
            var phys = new byte[9];
            BigEndian(phys, 0, 11811); // 300 dpi in pixels per metre
            BigEndian(phys, 4, 11811);
            phys[8] = 1;
            var png = BuildPng(1, 1, 0, 8, new byte[] { 0, 128 }, new[] { Tuple.Create("pHYs", phys) });

            var image = PdfImage.FromBytes(png);
            Assert.Equal(300, image.DpiX!.Value, 0);
        }

        [Fact]
        public void CorruptCrc_IsRejected()
        {
            var data = File.ReadAllBytes(Path.Combine(Folder, "rgb8.png"));
            data[40] ^= 0xFF;
            var ex = Assert.Throws<FormatException>(() => PdfImage.FromBytes(data));
            Assert.Contains("CRC", ex.Message);
        }

        [Fact]
        public void TruncatedData_IsRejected()
        {
            var data = File.ReadAllBytes(Path.Combine(Folder, "rgb8.png"));
            Assert.Throws<FormatException>(() => PdfImage.FromBytes(data.Take(data.Length - 30).ToArray()));
        }

        [Fact]
        public void ShortImageData_IsRejected_NotPaddedSilently()
        {
            // IHDR says 4×4 but the compressed data only holds one row.
            var png = BuildPng(4, 4, 0, 8, new byte[] { 0, 1, 2, 3, 4 }, extraChunks: null);
            Assert.Contains("truncated", Assert.Throws<FormatException>(() => PdfImage.FromBytes(png)).Message);
        }

        [Fact]
        public void HugeDimensions_AreRejectedBeforeAllocating()
        {
            var png = BuildPng(30001, 1, 0, 8, new byte[] { 0, 0 }, extraChunks: null);
            Assert.Throws<NotSupportedException>(() => PdfImage.FromBytes(png));
        }

        [Fact]
        public void UnknownCriticalChunk_IsRejected()
        {
            var png = BuildPng(1, 1, 0, 8, new byte[] { 0, 0 }, new[] { Tuple.Create("ABCD", new byte[0]) });
            Assert.Throws<NotSupportedException>(() => PdfImage.FromBytes(png));
        }

        [Fact]
        public void TransparentPng_RendersOverBackground()
        {
            var doc = new PdfDocument();
            var g = doc.AddPage(new PageSize(100, 100)).Graphics;
            g.FillRectangle(PdfColor.Red, 0, 0, 100, 100);
            g.DrawImage(PdfImage.FromFile(Path.Combine(Folder, "rgba8-i.png")), 10, 10, 80, 40);

            PdfStructure.Verify(doc.ToArray());
        }

        private static PdfStructure Embed(string name)
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawImage(PdfImage.FromFile(Path.Combine(Folder, name + ".png")), 10, 10, 100);
            return PdfStructure.Verify(doc.ToArray());
        }

        /// <summary>Builds a PNG with filter type 0 rows: <paramref name="raw"/> must already contain the filter bytes.</summary>
        private static byte[] BuildPng(int width, int height, int colorType, int bitDepth, byte[] raw, IEnumerable<Tuple<string, byte[]>>? extraChunks)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var ihdr = new byte[13];
                BigEndian(ihdr, 0, width);
                BigEndian(ihdr, 4, height);
                ihdr[8] = (byte)bitDepth;
                ihdr[9] = (byte)colorType;
                Chunk(ms, "IHDR", ihdr);
                if (extraChunks != null)
                {
                    foreach (var c in extraChunks)
                    {
                        Chunk(ms, c.Item1, c.Item2);
                    }
                }

                Chunk(ms, "IDAT", Zlib.Compress(raw));
                Chunk(ms, "IEND", new byte[0]);
                return ms.ToArray();
            }
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var header = new byte[8];
            BigEndian(header, 0, data.Length);
            for (var i = 0; i < 4; i++)
            {
                header[4 + i] = (byte)type[i];
            }

            s.Write(header, 0, 8);
            s.Write(data, 0, data.Length);
            var crcInput = new byte[4 + data.Length];
            Array.Copy(header, 4, crcInput, 0, 4);
            Array.Copy(data, 0, crcInput, 4, data.Length);
            var crc = new byte[4];
            BigEndian(crc, 0, (int)Crc32.Compute(crcInput, 0, crcInput.Length));
            s.Write(crc, 0, 4);
        }

        private static void BigEndian(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }
    }
}
