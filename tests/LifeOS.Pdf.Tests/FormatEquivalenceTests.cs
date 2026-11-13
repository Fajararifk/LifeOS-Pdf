using System;
using System.Globalization;
using System.Text;
using LifeOS.Pdf.Internal;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>
    /// The fast number/hex writers must produce exactly what the original (framework-formatting)
    /// implementation produced, so performance work never changes output bytes.
    /// </summary>
    public class FormatEquivalenceTests
    {
        private static string Reference(double value)
        {
            var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
            if (rounded == 0)
            {
                return "0";
            }

            if (rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e15)
            {
                return ((long)rounded).ToString(CultureInfo.InvariantCulture);
            }

            return rounded.ToString("0.####", CultureInfo.InvariantCulture);
        }

        [Fact]
        public void Number_MatchesReference_OnManyValues()
        {
            var random = new Random(42);
            var sb = new StringBuilder();
            var edge = new[]
            {
                0, -0.0, 1, -1, 0.5, 0.00005, -0.00005, 0.00004999, 0.12345, 0.99995, 9.99995, 1e-9, 123456789.12345,
                841.8897637795, 595.2755905512, 72.0000001, 99999999999999.0, 1e13 + 0.5, -2.5e-5, 0.1137254902, 1.0 / 3,
                2.0 / 3, 1234.56785, 0.30000000000000004, 14400, -14400.00004,
            };
            for (var i = 0; i < 200000 + edge.Length; i++)
            {
                double v;
                if (i < edge.Length)
                {
                    v = edge[i];
                }
                else
                {
                    switch (i % 4)
                    {
                        case 0: v = (random.NextDouble() - 0.5) * 2000; break;
                        case 1: v = Math.Round((random.NextDouble() - 0.5) * 1000, random.Next(0, 6)); break;
                        case 2: v = random.Next(-100000, 100000) / 255.0; break;
                        default: v = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-6, 14)); break;
                    }
                }

                var expected = Reference(v);
                Assert.Equal(expected, PdfFormat.Number(v));
                sb.Clear();
                PdfFormat.AppendNumber(sb, v);
                Assert.Equal(expected, sb.ToString());
            }
        }

        [Fact]
        public void Hex_MatchesReference()
        {
            var bytes = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                bytes[i] = (byte)i;
            }

            var expected = new StringBuilder();
            foreach (var b in bytes)
            {
                expected.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }

            Assert.Equal(expected.ToString(), PdfFormat.Hex(bytes));
            var sb = new StringBuilder();
            PdfFormat.AppendHex(sb, bytes, 0, bytes.Length);
            Assert.Equal(expected.ToString(), sb.ToString());
        }
    }
}
