using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LifeOS.Pdf.Barcodes;
using LifeOS.Pdf.Barcodes.Qr;
using LifeOS.Pdf.Templates;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class QrCodeTests
    {
        private static readonly Lazy<IDictionary<string, object?>> References = new Lazy<IDictionary<string, object?>>(() =>
            (IDictionary<string, object?>)TemplateData.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "qr", "references.json")))!);

        private static IEnumerable<IDictionary<string, object?>> Cases(string kind) =>
            ((List<object?>)References.Value[kind]!).Cast<IDictionary<string, object?>>();

        public static IEnumerable<object[]> FixedCases() => Cases("fixed").Select((c, i) => new object[] { i });

        public static IEnumerable<object[]> AutoCases() => Cases("auto").Select((c, i) => new object[] { i });

        private static QrErrorCorrection Level(string e) =>
            e == "L" ? QrErrorCorrection.Low : e == "M" ? QrErrorCorrection.Medium : e == "Q" ? QrErrorCorrection.Quartile : QrErrorCorrection.High;

        /// <summary>Every module must equal the reference encoder's matrix for the same version, level and mask.</summary>
        [Theory]
        [MemberData(nameof(FixedCases))]
        public void Matrix_MatchesReferenceEncoder(int index)
        {
            var c = Cases("fixed").ElementAt(index);
            var version = (int)(decimal)c["version"]!;
            var qr = QrCode.Encode((string)c["text"]!, Level((string)c["error"]!), version, version, (int)(decimal)c["mask"]!);
            var rows = ((List<object?>)c["rows"]!).Cast<string>().ToList();

            Assert.Equal(rows.Count, qr.Size);
            for (var y = 0; y < qr.Size; y++)
            {
                var bits = string.Concat(rows[y].Select(h => Convert.ToString(Convert.ToInt32(h.ToString(), 16), 2).PadLeft(4, '0')));
                var actual = new StringBuilder();
                for (var x = 0; x < qr.Size; x++)
                {
                    actual.Append(qr.IsDark(x, y) ? '1' : '0');
                }

                Assert.True(bits.Substring(0, qr.Size) == actual.ToString(), "row " + y + " differs for '" + c["text"] + "' v" + version + "-" + c["error"]);
            }
        }

        /// <summary>
        /// Automatic choice: the version must equal the reference encoders' choice, and the mask
        /// must be the one with the lowest ISO 18004 §7.8.3 penalty. (Reference libraries do not
        /// agree with each other on masks — segno and qrcode pick the same mask in only a few
        /// cases — so their mask choice is not used as an oracle; every mask decodes.)
        /// </summary>
        [Theory]
        [MemberData(nameof(AutoCases))]
        public void AutomaticVersionMatchesReference_AndMaskHasLowestPenalty(int index)
        {
            var c = Cases("auto").ElementAt(index);
            var text = (string)c["text"]!;
            var level = Level((string)c["error"]!);
            var qr = QrCode.Encode(text, level);

            Assert.Equal((int)(decimal)c["version"]!, qr.Version);
            var penalties = Enumerable.Range(0, 8).Select(m => QrEncoder.Penalty(QrCode.Encode(text, level, qr.Version, qr.Version, m).Matrix)).ToList();
            Assert.Equal(penalties.IndexOf(penalties.Min()), qr.Mask);
        }

        [Fact]
        public void ModeSelection()
        {
            Assert.Equal(QrEncoder.Mode.Numeric, QrEncoder.ChooseMode("0123456789"));
            Assert.Equal(QrEncoder.Mode.Alphanumeric, QrEncoder.ChooseMode("HELLO WORLD-42"));
            Assert.Equal(QrEncoder.Mode.Byte, QrEncoder.ChooseMode("hello"));
            Assert.Equal(QrEncoder.Mode.Byte, QrEncoder.ChooseMode(string.Empty));
        }

        [Fact]
        public void Capacity_LimitsAreEnforced()
        {
            // Version 40-L holds 7089 digits; one more must not fit.
            Assert.Equal(40, QrCode.Encode(new string('1', 7089), QrErrorCorrection.Low).Version);
            var ex = Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('1', 7090), QrErrorCorrection.Low));
            Assert.Contains("does not fit", ex.Message);
            Assert.Throws<ArgumentException>(() => QrCode.Encode("too long for version 1", maxVersion: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => QrCode.Encode("x", mask: 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => QrCode.Encode("x", minVersion: 5, maxVersion: 3));
        }

        [Fact]
        public void EmptyText_IsVersion1()
        {
            var qr = QrCode.Encode(string.Empty);
            Assert.Equal(1, qr.Version);
            Assert.Equal(21, qr.Size);
        }

        [Fact]
        public void Version40_IsFast()
        {
            var text = new string('a', 2331); // fills 40-M in byte mode
            QrCode.Encode(text);
            var sw = Stopwatch.StartNew();
            var qr = QrCode.Encode(text);
            sw.Stop();

            Assert.Equal(40, qr.Version);
            Assert.True(sw.ElapsedMilliseconds < 500, "took " + sw.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void Png_HasExpectedSizeAndDecodes()
        {
            var qr = QrCode.Encode("HELLO", QrErrorCorrection.Medium, 1, 1, 0);
            var png = qr.ToPng(moduleSize: 3, quietZone: 4);
            var image = Images.PdfImage.FromBytes(png);

            Assert.Equal((21 + 8) * 3, image.PixelWidth);
            Assert.Equal((21 + 8) * 3, image.PixelHeight);
            Assert.False(image.HasAlpha);
        }

        [Fact]
        public void Svg_IsOnePath()
        {
            var svg = QrCode.Encode("HELLO").ToSvg(moduleSize: 2);
            Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg);
            Assert.Equal(1, svg.Split(new[] { "<path" }, StringSplitOptions.None).Length - 1);
        }

        [Fact]
        public void DrawQrCode_WritesOneFilledPath_AndChecksLogoRules()
        {
            var doc = new PdfDocument();
            var g = doc.AddPage().Graphics;
            g.DrawQrCode(QrCode.Encode("https://lifeos.app"), 50, 50, 120);
            var content = PdfStructure.Verify(doc.ToArray()).PageContents()[0];

            Assert.Equal(2, content.Split('\n').Count(l => l == "f" || l.EndsWith(" re f", StringComparison.Ordinal)));

            var logo = Images.PdfImage.FromFile(Path.Combine(AppContext.BaseDirectory, "TestAssets", "rgb.jpg"));
            Assert.Throws<ArgumentException>(() => g.DrawQrCode(QrCode.Encode("x"), 0, 0, 100, logo: logo));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.DrawQrCode(QrCode.Encode("x", QrErrorCorrection.High), 0, 0, 100, logo: logo, logoScale: 0.4));
            g.DrawQrCode(QrCode.Encode("x", QrErrorCorrection.High), 0, 0, 100, logo: logo);
        }
    }
}
