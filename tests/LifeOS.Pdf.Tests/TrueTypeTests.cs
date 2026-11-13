using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Fonts.TrueType;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class TrueTypeTests
    {
        // Facts about the test fonts, read with fontTools when the assets were generated.
        private const int GlyphA = 34;          // 'A', advance 639/1000
        private const int GlyphEHook = 550;     // 'ệ' U+1EC7, composite of 70 + 317 + 614
        private const int GlyphMathBoldA = 56;  // U+1D400 in NotoSansMath-astral (cmap format 12)

        private static string FontPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", "fonts", name);

        private static PdfFontFace Noto() => PdfFontFace.FromFile(FontPath("NotoSans-subset.ttf"));

        private static TrueTypeFont NotoFont() => TrueTypeFont.Parse(File.ReadAllBytes(FontPath("NotoSans-subset.ttf")), 0);

        [Fact]
        public void Parse_ReadsNamesAndMetrics()
        {
            var face = Noto();

            Assert.Equal("NotoSans-Regular", face.PostScriptName);
            Assert.Equal("Noto Sans", face.FamilyName);
            Assert.True(face.IsEmbedded);
            Assert.False(face.IsBold);
            Assert.Equal(663, face.GlyphCount);
            Assert.True(face.HasGlyph('A'));
            Assert.True(face.HasGlyph("Tiếng Việt Ελληνικά Русский €"));
            Assert.False(face.HasGlyph("中"));
        }

        [Fact]
        public void Map_UsesCmapAndHmtx()
        {
            var font = new PdfFont(Noto(), 10);

            var run = font.Map("A");
            Assert.Equal(new[] { GlyphA }, run.Glyphs);
            Assert.Equal(new byte[] { 0x00, 0x22 }, run.Codes);
            Assert.Equal(6.39, font.MeasureWidth("A"), 6);
            Assert.Equal(GlyphEHook, font.Map("ệ").Glyphs[0]);
        }

        [Fact]
        public void MissingGlyph_MapsToNotdef_AndLoneSurrogateToReplacement()
        {
            var run = new PdfFont(Noto(), 10).Map("中\uD800");

            Assert.Equal(new[] { 0, 0 }, run.Glyphs);
            Assert.Equal(new[] { 0x4E2D, 0xFFFD }, run.CodePoints);
        }

        [Fact]
        public void Cmap12_SupportsCharactersAboveBmp()
        {
            var face = PdfFontFace.FromFile(FontPath("NotoSansMath-astral.ttf"));
            var run = new PdfFont(face, 12).Map("\U0001D400");

            Assert.Equal(new[] { GlyphMathBoldA }, run.Glyphs);
            Assert.Equal(new[] { 0x1D400 }, run.CodePoints);
        }

        [Fact]
        public void Collection_SelectsFontByIndex()
        {
            Assert.Equal("NotoSans-Regular", PdfFontFace.FromFile(FontPath("test.ttc"), 0).PostScriptName);
            Assert.Equal("NotoSansMath-Regular", PdfFontFace.FromFile(FontPath("test.ttc"), 1).PostScriptName);
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => PdfFontFace.FromFile(FontPath("test.ttc"), 2));
            Assert.Contains("0 to 1", ex.Message);
        }

        [Fact]
        public void Subset_KeepsUsedGlyphsAndCompositeComponents()
        {
            var font = NotoFont();
            var subset = TrueTypeSubsetter.Subset(font, new[] { GlyphEHook });
            var parsed = TrueTypeFont.Parse(subset, 0, requireCmap: false);

            Assert.Equal(font.GlyphCount, parsed.GlyphCount); // glyph IDs are preserved
            foreach (var gid in new[] { 0, GlyphEHook, 70, 317, 614 })
            {
                parsed.GlyphRange(gid, out _, out var length);
                Assert.True(length > 0, "glyph " + gid + " should be kept");
            }

            parsed.GlyphRange(GlyphA, out _, out var unused);
            Assert.Equal(0, unused);
            Assert.True(subset.Length < File.ReadAllBytes(FontPath("NotoSans-subset.ttf")).Length / 2);
            Assert.Equal(0xB1B0AFBAu, TrueTypeSubsetter.Checksum(subset, 0, subset.Length));
        }

        [Fact]
        public void Subset_HasCmapForUsedCharacters_BmpAndAstral()
        {
            var noto = TrueTypeSubsetter.Subset(NotoFont(), new[] { GlyphA, GlyphEHook },
                new System.Collections.Generic.Dictionary<int, int> { [GlyphA] = 'A', [GlyphEHook] = 0x1EC7 });
            var parsed = TrueTypeFont.Parse(noto, 0);
            Assert.Equal(GlyphA, parsed.Cmap.GlyphId('A'));
            Assert.Equal(GlyphEHook, parsed.Cmap.GlyphId(0x1EC7));
            Assert.Equal(0, parsed.Cmap.GlyphId('B'));

            var mathFont = TrueTypeFont.Parse(File.ReadAllBytes(FontPath("NotoSansMath-astral.ttf")), 0);
            var math = TrueTypeSubsetter.Subset(mathFont, new[] { GlyphMathBoldA, 57 },
                new System.Collections.Generic.Dictionary<int, int> { [GlyphMathBoldA] = 0x1D400, [57] = 0x1D401 });
            var parsedMath = TrueTypeFont.Parse(math, 0);
            Assert.Equal(GlyphMathBoldA, parsedMath.Cmap.GlyphId(0x1D400));
            Assert.Equal(57, parsedMath.Cmap.GlyphId(0x1D401));
        }

        [Fact]
        public void Subset_OfCollectionFont_IsStandaloneTrueType()
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(FontPath("test.ttc")), 1);
            var subset = TrueTypeSubsetter.Subset(font, new[] { GlyphMathBoldA });

            Assert.Equal(0x00010000u, BigEndian.U32(subset, 0));
            Assert.Equal("NotoSansMath-Regular", TrueTypeFont.Parse(subset, 0, requireCmap: false).PostScriptName);
        }

        [Fact]
        public void EmbeddedFont_WritesType0CidFontAndToUnicode()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("A ệ", new PdfFont(Noto(), 14), 72, 72);
            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Matches(@"/Subtype /Type0 /BaseFont /[A-Z]{6}\+NotoSans-Regular /Encoding /Identity-H /DescendantFonts \[\d+ 0 R\] /ToUnicode \d+ 0 R", pdf.Text);
            Assert.Contains("/Subtype /CIDFontType2", pdf.Text);
            Assert.Contains("/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >>", pdf.Text);
            Assert.Contains("/CIDToGIDMap /Identity", pdf.Text);
            Assert.Matches(@"/FontFile2 \d+ 0 R", pdf.Text);
            // Space is glyph 1 in this subset font (advance 260).
            Assert.Matches(@"/W \[1 \[260\] 34 \[639\] 550 \[564\]\]", pdf.Text);
            Assert.Contains("<002200010226> Tj", pdf.PageContents()[0]);

            var toUnicode = ToUnicodeOf(pdf);
            Assert.Contains("<0022> <0041>", toUnicode);
            Assert.Contains("<0226> <1EC7>", toUnicode);
        }

        [Fact]
        public void EmbeddedFont_AstralCharactersUseSurrogatePairsInToUnicode()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("\U0001D400", new PdfFont(PdfFontFace.FromFile(FontPath("NotoSansMath-astral.ttf")), 14), 72, 72);

            Assert.Contains("<0038> <D835DC00>", ToUnicodeOf(PdfStructure.Verify(doc.ToArray())));
        }

        [Fact]
        public void EmbeddedFont_IsWrittenOncePerDocument()
        {
            var face = Noto();
            var doc = new PdfDocument();
            for (var i = 0; i < 100; i++)
            {
                doc.AddPage().Graphics.DrawText("Halaman " + i, new PdfFont(face, 8 + i % 5), 72, 72);
            }

            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Equal(1, pdf.CountObjects("/Subtype /Type0"));
            Assert.Equal(1, pdf.CountObjects("/Subtype /CIDFontType2"));
        }

        [Fact]
        public void StandardAndEmbeddedFonts_CanBeMixed()
        {
            var doc = new PdfDocument();
            var g = doc.AddPage().Graphics;
            g.DrawText("Helvetica", new PdfFont(StandardFont.Helvetica, 12), 72, 72);
            g.DrawText("Noto Sans", new PdfFont(Noto(), 12), 72, 100);

            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/Font << /F1 ", pdf.Text);
            Assert.Contains("/F2 ", pdf.Text);
            Assert.Contains("/BaseFont /Helvetica ", pdf.Text);
        }

        [Fact]
        public void EmbeddedOutput_IsDeterministic()
        {
            byte[] Build()
            {
                var doc = new PdfDocument();
                doc.Info.CreationDate = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
                doc.AddPage().Graphics.DrawText("Sama persis", new PdfFont(Noto(), 12), 72, 72);
                return doc.ToArray();
            }

            Assert.Equal(Build(), Build());
        }

        [Fact]
        public void RestrictedLicense_IsRejected_NoSubsetting_EmbedsWholeFont()
        {
            var restricted = WithFsType(0x0002);
            var ex = Assert.Throws<NotSupportedException>(() => PdfFontFace.FromBytes(restricted));
            Assert.Contains("does not allow embedding", ex.Message);

            // Restricted + editable bit: the least restrictive permission wins.
            PdfFontFace.FromBytes(WithFsType(0x000A));

            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("A", new PdfFont(PdfFontFace.FromBytes(WithFsType(0x0100)), 12), 72, 72);
            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/BaseFont /NotoSans-Regular", pdf.Text); // no subset tag
        }

        [Fact]
        public void CffAndWoffFonts_AreRejectedWithClearMessage()
        {
            var otto = new byte[64];
            Encoding.ASCII.GetBytes("OTTO").CopyTo(otto, 0);
            Assert.Contains("CFF", Assert.Throws<NotSupportedException>(() => PdfFontFace.FromBytes(otto)).Message);

            var woff = new byte[64];
            Encoding.ASCII.GetBytes("wOFF").CopyTo(woff, 0);
            Assert.Contains("WOFF", Assert.Throws<NotSupportedException>(() => PdfFontFace.FromBytes(woff)).Message);
        }

        [Fact]
        public void CorruptFonts_OnlyThrowFormatOrNotSupported()
        {
            var original = File.ReadAllBytes(FontPath("NotoSans-subset.ttf"));
            var random = new Random(1234);
            for (var round = 0; round < 300; round++)
            {
                byte[] data;
                if (round < 40)
                {
                    data = new byte[random.Next(0, original.Length)];
                    Array.Copy(original, data, data.Length);
                }
                else
                {
                    data = (byte[])original.Clone();
                    for (var k = 0; k < 1 + round % 8; k++)
                    {
                        // Concentrate damage in the table directory and early tables.
                        var pos = random.Next(0, round % 2 == 0 ? 600 : data.Length);
                        data[pos] = (byte)random.Next(256);
                    }
                }

                try
                {
                    var face = PdfFontFace.FromBytes(data);
                    var doc = new PdfDocument();
                    doc.AddPage().Graphics.DrawText("Aệ€Ω Ж", new PdfFont(face, 12), 10, 10);
                    doc.ToArray();
                }
                catch (FormatException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }

        [Fact]
        public void StandardFontMetrics_ComeFromAfm()
        {
            // Times-Italic 'f' is 278 in its AFM (Times-Roman: 333) — Fase 0 approximated this.
            Assert.Equal(2.78, new PdfFont(StandardFont.TimesItalic, 10).MeasureWidth("f"), 6);
            Assert.Equal(5.56, new PdfFont(StandardFont.Helvetica, 10).MeasureWidth("€"), 6);
            Assert.Equal(3.5, new PdfFont(StandardFont.Helvetica, 10).MeasureWidth("•"), 6);
            Assert.True(PdfFontFace.Standard(StandardFont.Helvetica) == new PdfFont(StandardFont.Helvetica, 9).Face);
            Assert.False(PdfFontFace.Standard(StandardFont.Courier).IsEmbedded);
        }

        private static byte[] WithFsType(ushort fsType)
        {
            var data = File.ReadAllBytes(FontPath("NotoSans-subset.ttf"));
            var os2 = TrueTypeFont.Parse(data, 0).Table("OS/2");
            BigEndian.Write16(data, (int)os2.Offset + 8, fsType);
            return data;
        }

        private static string ToUnicodeOf(PdfStructure pdf)
        {
            var number = int.Parse(Regex.Match(pdf.Text, @"/ToUnicode (\d+) 0 R").Groups[1].Value);
            return Encoding.ASCII.GetString(pdf.StreamData(number));
        }
    }
}
