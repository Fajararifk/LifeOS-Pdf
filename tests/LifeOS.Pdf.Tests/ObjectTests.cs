using System.IO;
using System.Text;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Objects;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class ObjectTests
    {
        private static string Write(PdfObject obj)
        {
            using (var ms = new MemoryStream())
            {
                obj.WriteTo(new PdfOutput(ms));
                return Encoding.ASCII.GetString(ms.ToArray());
            }
        }

        [Theory]
        [InlineData(0, "0")]
        [InlineData(-0.00001, "0")]
        [InlineData(12, "12")]
        [InlineData(1.5, "1.5")]
        [InlineData(1.23456, "1.2346")]
        [InlineData(-3.1, "-3.1")]
        [InlineData(123456789.5, "123456789.5")]
        public void Numbers_AreFormattedWithoutExponentOrCulture(double value, string expected)
        {
            Assert.Equal(expected, PdfFormat.Number(value));
        }

        [Fact]
        public void Names_EscapeDelimitersAndSpaces()
        {
            Assert.Equal("/Type", Write(new PdfName("Type")));
            Assert.Equal("/A#20B#2FC#23", Write(new PdfName("A B/C#")));
        }

        [Fact]
        public void LiteralStrings_EscapeParensAndBackslash()
        {
            Assert.Equal(@"(a\(b\)c\\)", Write(PdfString.Text(@"a(b)c\")));
        }

        [Fact]
        public void Zlib_RoundTrips()
        {
            var data = Encoding.ASCII.GetBytes("q 1 0 0 1 0 0 cm Q\n" + new string('x', 1000));
            var compressed = Zlib.Compress(data);

            Assert.Equal(0x78, compressed[0]);
            Assert.True(compressed.Length < data.Length);
            Assert.Equal(data, Zlib.Decompress(compressed));
        }

        [Fact]
        public void Adler32_KnownValue()
        {
            // RFC 1950 reference: Adler-32 of "Wikipedia" is 0x11E60398.
            Assert.Equal(0x11E60398u, Zlib.Adler32(Encoding.ASCII.GetBytes("Wikipedia")));
        }
    }
}
