using System;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class TextTests
    {
        [Fact]
        public void MeasureWidth_UsesAfmMetrics()
        {
            // Helvetica: H=722 e=556 l=222 l=222 o=556 → 2278 units.
            Assert.Equal(22.78, new PdfFont(StandardFont.Helvetica, 10).MeasureWidth("Hello"), 6);
            // Courier is monospaced: 600 units per glyph.
            Assert.Equal(36, new PdfFont(StandardFont.Courier, 12).MeasureWidth("abcde"), 6);
            // Accented letters use the width of their base letter.
            var helv = new PdfFont(StandardFont.Helvetica, 10);
            Assert.Equal(helv.MeasureWidth("e"), helv.MeasureWidth("é"), 6);
        }

        [Fact]
        public void WinAnsi_EncodesLatinAndSpecials_ReplacesUnsupported()
        {
            Assert.Equal(new byte[] { 0x80, 0xE9, 0x96, (byte)'?' }, WinAnsiEncoding.Encode("€é–中"));
            Assert.Equal(new byte[] { (byte)'a', (byte)' ', (byte)'b' }, WinAnsiEncoding.Encode("a\tb\u0001"));
        }

        [Fact]
        public void DrawText_WritesFontResourceAndHexString()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("Rp1.350.000", new PdfFont(StandardFont.HelveticaBold, 14), 72, 72, PdfColor.Blue);

            var pdf = PdfStructure.Verify(doc.ToArray());
            var content = pdf.PageContents()[0];

            Assert.Contains("/BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding", pdf.Text);
            Assert.Contains("/Font << /F1 3 0 R >>", pdf.Text);
            Assert.Contains("/F1 14 Tf", content);
            Assert.Contains("<5270312E3335302E303030> Tj", content);
            Assert.Contains("0.1451 0.3882 0.9216 rg", content);
        }

        [Fact]
        public void DrawText_YIsTopOfLine()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage(new PageSize(200, 200));
            var font = new PdfFont(StandardFont.Helvetica, 10);
            page.Graphics.DrawText("A", font, 10, 20);

            var content = PdfStructure.Verify(doc.ToArray()).PageContents()[0];

            // baseline = top + ascent (7.18) → PDF y = 200 - 27.18 = 172.82
            Assert.Contains("1 0 0 1 10 172.82 Tm", content);
        }

        [Theory]
        [InlineData(TextAlign.Left, "1 0 0 1 100 ")]
        [InlineData(TextAlign.Center, "1 0 0 1 88.61 ")]
        [InlineData(TextAlign.Right, "1 0 0 1 77.22 ")]
        public void DrawText_Alignment(TextAlign align, string expected)
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("Hello", new PdfFont(StandardFont.Helvetica, 10), 100, 50, null, align);

            Assert.Contains(expected, PdfStructure.Verify(doc.ToArray()).PageContents()[0]);
        }

        [Fact]
        public void FontsAreSharedAcrossPages()
        {
            var doc = new PdfDocument();
            var font = new PdfFont(StandardFont.TimesRoman, 12);
            for (var i = 0; i < 5; i++)
            {
                doc.AddPage().Graphics.DrawText("Halaman " + (i + 1), font, 72, 72);
            }

            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Equal(1, pdf.CountObjects("/BaseFont /Times-Roman"));
        }

        [Fact]
        public void SymbolFonts_HaveNoWinAnsiEncoding()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("abc", new PdfFont(StandardFont.ZapfDingbats, 12), 72, 72);

            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Contains("/BaseFont /ZapfDingbats >>", pdf.Text);
        }

        [Fact]
        public void Wrap_BreaksAtSpacesWithinWidth()
        {
            var font = new PdfFont(StandardFont.Helvetica, 10);
            var lines = PdfGraphics.WrapText("Belajar membuat library PDF sendiri dari nol", font, 100);

            Assert.True(lines.Count > 1);
            foreach (var line in lines)
            {
                Assert.True(font.MeasureWidth(line) <= 100, "Line too wide: " + line);
                Assert.Equal(line.Trim(), line);
            }

            Assert.Equal("Belajar membuat library PDF sendiri dari nol", string.Join(" ", lines));
        }

        [Fact]
        public void Wrap_SplitsWordsLongerThanTheBox()
        {
            var font = new PdfFont(StandardFont.Courier, 10); // 6pt per char
            var lines = PdfGraphics.WrapText("abcdefghijklmnop", font, 30);

            Assert.Equal(new[] { "abcde", "fghij", "klmno", "p" }, lines);
        }

        [Fact]
        public void Wrap_KeepsParagraphsAndBlankLines()
        {
            var font = new PdfFont(StandardFont.Helvetica, 10);
            var lines = PdfGraphics.WrapText("Satu\n\nDua", font, 200);

            Assert.Equal(new[] { "Satu", string.Empty, "Dua" }, lines);
        }

        [Fact]
        public void DrawTextBox_ReturnsHeightUsed()
        {
            var doc = new PdfDocument();
            var font = new PdfFont(StandardFont.Helvetica, 10);
            var height = doc.AddPage().Graphics.DrawTextBox("Satu\nDua\nTiga", font, 72, 72, 300);

            Assert.Equal(36, height, 6); // 3 lines × 10pt × 1.2
        }

        [Fact]
        public void InvalidFontSize_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfFont(StandardFont.Helvetica, 0));
            Assert.Throws<ArgumentException>(() => new PdfFont(StandardFont.Helvetica, double.NaN));
        }
    }
}
