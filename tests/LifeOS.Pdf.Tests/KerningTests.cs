using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Fonts.TrueType;
using LifeOS.Pdf.Layout;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class KerningTests
    {
        // Reference values read with fontTools from the test fonts (tools/make_test_assets.py).
        private const int GlyphA = 34, GlyphV = 55;

        private static string FontPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", "fonts", name);

        [Theory]
        [InlineData("AV", -40)]
        [InlineData("To", -70)]
        [InlineData("Ta", -80)]
        [InlineData("oo", 0)]
        public void Gpos_PairKerningMatchesFontTools(string pair, int expected)
        {
            var face = PdfFontFace.FromFile(FontPath("NotoSans-kern.ttf"));
            var run = face.Program.Map(pair, kerning: true);

            Assert.Equal(expected, run.Adjustments[0]);
            Assert.Equal(face.Program.Map(pair).Width + expected, run.Width);
        }

        [Fact]
        public void LegacyKernTable_IsRead()
        {
            var face = PdfFontFace.FromFile(FontPath("NotoSans-kerntable.ttf"));

            Assert.Equal(-80, face.Program.Map("AV", kerning: true).Adjustments[0]);
            Assert.Equal(-60, face.Program.Map("To", kerning: true).Adjustments[0]);
        }

        [Fact]
        public void KerningOff_ByDefaultInMapAndPdfGraphics()
        {
            var face = PdfFontFace.FromFile(FontPath("NotoSans-kern.ttf"));
            Assert.Equal(0, face.Program.Map("AV").Adjustments[0]);

            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("AVATAR", new PdfFont(face, 12), 72, 72);
            Assert.DoesNotContain(" TJ", PdfStructure.Verify(doc.ToArray()).PageContents()[0]);
        }

        [Fact]
        public void LayoutEngine_WritesKerningWithTJ()
        {
            var doc = new FlowDocument();
            doc.Styles.Normal = new TextStyle { Family = FontFamily.FromFiles(FontPath("NotoSans-kern.ttf")) };
            doc.AddSection().AddParagraph("AV");

            var content = PdfStructure.Verify(doc.Render().ToArray()).PageContents()[0];
            Assert.Contains("[<" + GlyphA.ToString("X4") + "> 40 <" + GlyphV.ToString("X4") + ">] TJ", content);
        }

        [Fact]
        public void KerningCanBeDisabledPerStyle()
        {
            var doc = new FlowDocument();
            doc.Styles.Normal = new TextStyle { Family = FontFamily.FromFiles(FontPath("NotoSans-kern.ttf")), Kerning = false };
            doc.AddSection().AddParagraph("AV");

            Assert.DoesNotContain(" TJ", PdfStructure.Verify(doc.Render().ToArray()).PageContents()[0]);
        }

        [Fact]
        public void CorruptGpos_DisablesKerningInsteadOfFailing()
        {
            var data = File.ReadAllBytes(FontPath("NotoSans-kern.ttf"));
            var gpos = TrueTypeFont.Parse(data, 0).Table("GPOS");
            // Point the FeatureList offset far outside the table.
            BigEndian.Write16(data, (int)gpos.Offset + 6, 0xFFF0);

            var face = PdfFontFace.FromBytes(data);
            var run = face.Program.Map("AV", kerning: true);
            Assert.Equal(0, run.Adjustments[0]);
        }

        [Fact]
        public void NotdefGlyph_HasNoToUnicodeEntry()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("中A", new PdfFont(PdfFontFace.FromFile(FontPath("NotoSans-subset.ttf")), 12), 72, 72);
            var pdf = PdfStructure.Verify(doc.ToArray());
            var number = int.Parse(Regex.Match(pdf.Text, @"/ToUnicode (\d+) 0 R").Groups[1].Value);
            var cmap = Encoding.ASCII.GetString(pdf.StreamData(number));

            Assert.DoesNotContain("<0000> <4E2D>", cmap); // 中 drew .notdef: no mapping for glyph 0
            Assert.Contains("<0022> <0041>", cmap);
        }
    }
}
