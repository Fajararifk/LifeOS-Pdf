using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>Fase 6: lexer, parser, cross-reference variants, recovery, filters and limits.</summary>
    public class ParserTests
    {
        private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", "pdf", name);

        private static PdfDocument OpenText(string pdf, PdfReadOptions? options = null) => PdfDocument.Open(RawPdf.Bytes(pdf), options);

        // ------------------------------------------------------------------ lexer

        [Fact]
        public void Lexer_ReadsAllTokenKinds()
        {
            var lexer = new Lexer(RawPdf.Bytes("123 -4.5 .5 +7 --3 /A#20B /Type (a\\(b\\)\\101\\\nc(d)) <48656C6C6F7> [ ] << >> true % comment\nobj"));
            var t = lexer.Next();
            Assert.Equal((TokenKind.Integer, 123L), (t.Kind, t.Integer));
            t = lexer.Next();
            Assert.Equal((TokenKind.Real, -4.5, "-4.5"), (t.Kind, t.Real, t.Text));
            Assert.Equal(0.5, lexer.Next().Real);
            Assert.Equal(7, lexer.Next().Integer);
            Assert.Equal(-3, lexer.Next().Real); // lenient: "--3" is read as -3 like other readers
            Assert.Equal("A B", lexer.Next().Text);
            Assert.Equal("Type", lexer.Next().Text);
            t = lexer.Next();
            Assert.Equal(TokenKind.String, t.Kind);
            Assert.Equal("a(b)Ac(d)", Encoding.ASCII.GetString(t.Bytes!)); // escapes, octal, line continuation, nesting
            t = lexer.Next();
            Assert.Equal(TokenKind.HexString, t.Kind);
            Assert.Equal(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x70 }, t.Bytes); // odd digit count → trailing 0
            Assert.Equal(TokenKind.ArrayStart, lexer.Next().Kind);
            Assert.Equal(TokenKind.ArrayEnd, lexer.Next().Kind);
            Assert.Equal(TokenKind.DictStart, lexer.Next().Kind);
            Assert.Equal(TokenKind.DictEnd, lexer.Next().Kind);
            Assert.True(lexer.Next().IsKeyword("true"));
            Assert.True(lexer.Next().IsKeyword("obj"));
            Assert.Equal(TokenKind.Eof, lexer.Next().Kind);
        }

        [Fact]
        public void Lexer_UnterminatedString_Throws() =>
            Assert.Throws<PdfReadException>(() => new Lexer(RawPdf.Bytes("(never closed")).Next());

        // ------------------------------------------------------------------ opening

        [Fact]
        public void Open_HandWrittenFile()
        {
            var doc = OpenText(RawPdf.OnePage("/Rotate 90"));
            Assert.Single(doc.Pages);
            Assert.Equal(300, doc.Pages[0].Width);
            Assert.Equal(400, doc.Pages[0].Height);
            Assert.Equal(90, doc.Pages[0].Rotation);
            Assert.True(doc.Pages[0].IsImported);
            Assert.Equal("1.7", doc.PdfVersion);
            var saved = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/Rotate 90", saved.Text);
            Assert.Contains("(Halo) Tj", string.Join("\n", saved.PageContents()));
        }

        [Fact]
        public void Open_InheritedAttributes_FromPageTree()
        {
            var pdf = RawPdf.Build(new[]
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 2 /MediaBox [0 0 500 700] /Rotate 180 /Resources << /Font << /F9 5 0 R >> >> >>",
                "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 4 0 R] /Count 2 /CropBox [10 10 490 690] >>",
                "<< /Type /Page /Parent 3 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
            });
            var doc = OpenText(pdf);
            Assert.Single(doc.Pages); // the same page object listed twice is used once
            Assert.Equal(500, doc.Pages[0].Width);
            Assert.Equal(180, doc.Pages[0].Rotation);
            var saved = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/CropBox [10 10 490 690]", saved.Text);
            Assert.Contains("/BaseFont /Courier", saved.Text); // inherited resources were copied
        }

        [Fact]
        public void Open_InfoWithUnicodeAndDates()
        {
            var pdf = RawPdf.Build(new[]
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
                "<< /Title <FEFF004C00610070> /Author (Fajar\\222 \\223) /CreationDate (D:20261113101500+07'00') >>",
            }, "/Info 4 0 R");
            var doc = OpenText(pdf);
            Assert.Equal("Lap", doc.Info.Title);
            Assert.Equal("Fajar™ ﬁ", doc.Info.Author); // PDFDocEncoding: 0x92 = ™, 0x93 = ﬁ
            Assert.Equal(new DateTimeOffset(2026, 11, 13, 10, 15, 0, TimeSpan.FromHours(7)), doc.Info.CreationDate);
        }

        [Fact]
        public void Open_OwnObjectStreams_RoundTrip()
        {
            var source = new PdfDocument();
            source.Info.Title = "Kompak";
            for (var i = 0; i < 150; i++)
            {
                source.AddPage().Graphics.DrawText("Halaman " + i, new PdfFont(StandardFont.Helvetica, 12), 72, 72);
            }

            var compact = source.ToArray(new PdfSaveOptions { UseObjectStreams = true });
            var classic = source.ToArray();
            Assert.True(compact.Length < classic.Length, compact.Length + " vs " + classic.Length);
            Assert.Contains("/Type /ObjStm", Encoding.ASCII.GetString(compact));
            var reopened = PdfDocument.Open(compact);
            Assert.Equal(150, reopened.Pages.Count);
            Assert.Equal("Kompak", reopened.Info.Title);
            var text = string.Join("\n", PdfStructure.Verify(reopened.ToArray()).PageContents());
            Assert.Contains("Tj", text);
        }

        [Theory]
        [InlineData("qpdf-objstm.pdf")]
        [InlineData("qpdf-linearized.pdf")]
        [InlineData("qpdf-plain.pdf")]
        public void Open_FilesWrittenByQpdf(string name)
        {
            var doc = PdfDocument.Open(Asset(name));
            Assert.Equal(5, doc.Pages.Count);
            Assert.Equal("LifeOS.Pdf — Navigasi & Interaktif", doc.Info.Title);
            Assert.Contains(doc.Outline.Items, i => i.Title == "1. Pendahuluan");
            var saved = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/Subtype /Link", saved.Text);
            Assert.Equal(5, saved.PageContents().Count);
        }

        [Fact]
        public void Open_IncrementalUpdate_NewestObjectWins()
        {
            var original = RawPdf.OnePage("", "BT ET", "<< /Title (Lama) >>");
            var start = original.LastIndexOf("startxref\n", StringComparison.Ordinal);
            var prev = original.Substring(start + 10, original.IndexOf('\n', start + 10) - start - 10);
            var sb = new StringBuilder(original);
            var objOffset = sb.Length;
            sb.Append("4 0 obj\n<< /Title (Baru) >>\nendobj\n");
            var xref = sb.Length;
            sb.Append("xref\n4 1\n").Append(objOffset.ToString("D10")).Append(" 00000 n \n")
              .Append("trailer\n<< /Size 6 /Root 1 0 R /Info 4 0 R /Prev ").Append(prev).Append(" >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            Assert.Equal("Baru", OpenText(sb.ToString()).Info.Title);
        }

        [Fact]
        public void Open_DamagedCrossReference_IsRebuilt()
        {
            var good = RawPdf.OnePage();
            var wrongStart = good.Substring(0, good.LastIndexOf("startxref", StringComparison.Ordinal)) + "startxref\n999999\n%%EOF\n";
            Assert.Single(OpenText(wrongStart).Pages);

            var noXref = good.Substring(0, good.IndexOf("xref\n", StringComparison.Ordinal)) + "trailer\n<< /Root 1 0 R >>\n%%EOF\n";
            Assert.Single(OpenText(noXref).Pages);

            var shifted = "garbage before the header\n" + good; // offsets now wrong by the prefix length
            Assert.Single(OpenText(shifted).Pages);
        }

        [Fact]
        public void Open_WrongStreamLength_FindsEndstream()
        {
            var pdf = RawPdf.OnePage().Replace("/Length 35", "/Length 9999");
            var doc = OpenText(pdf);
            Assert.Contains("(Halo) Tj", string.Join("\n", PdfStructure.Verify(doc.ToArray()).PageContents()));
        }

        [Fact]
        public void Open_Encrypted_ThrowsWithHint()
        {
            var pdf = RawPdf.Build(new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 0 >>", "<< /Filter /Standard /V 1 /R 2 >>" }, "/Encrypt 3 0 R");
            var ex = Assert.Throws<PdfEncryptedException>(() => OpenText(pdf));
            Assert.Contains("qpdf --decrypt", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("hello world")]
        [InlineData("%PDF-1.4\n%%EOF")]
        public void Open_NotAPdfOrEmpty_Throws(string text) =>
            Assert.ThrowsAny<PdfReadException>(() => OpenText(text));

        // ------------------------------------------------------------------ untrusted input

        [Fact]
        public void Limits_DeepNesting_FailsFast()
        {
            var deep = new string('[', 20000) + new string(']', 20000);
            var pdf = RawPdf.OnePage("/X " + deep);
            var sw = Stopwatch.StartNew();
            Assert.ThrowsAny<PdfReadException>(() => OpenText(pdf).ToArray());
            Assert.True(sw.ElapsedMilliseconds < 5000);
        }

        [Fact]
        public void Limits_DecompressionBomb_IsStopped()
        {
            var bomb = new PdfStream(new PdfDictionary().Set("Filter", "FlateDecode"), Internal.Zlib.Compress(new byte[50 * 1024 * 1024]));
            Assert.Throws<PdfLimitException>(() => StreamDecoder.Decode(bomb, null, 1024 * 1024));
        }

        [Fact]
        public void Limits_ReferenceCycles_AndLoops_Terminate()
        {
            // Page tree whose kid points back to the root, a /Prev pointing to itself and a 5→6→5 chain.
            var pdf = RawPdf.Build(new[]
            {
                "<< /Type /Catalog /Pages 2 0 R /Outlines 7 0 R >>",
                "<< /Type /Pages /Kids [2 0 R 3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /X 5 0 R >>",
                "<< >>",
                "6 0 R",
                "5 0 R",
                "<< /First 8 0 R >>",
                "<< /Title (Loop) /Next 8 0 R /Dest [3 0 R /Fit] >>",
            });
            var sw = Stopwatch.StartNew();
            try
            {
                var doc = OpenText(pdf);
                Assert.Single(doc.Pages);
                Assert.Single(doc.Outline.Items); // /Next cycle stops
                doc.ToArray();
            }
            catch (PdfReadException)
            {
                // A reference cycle may also be reported as damage; it must not hang.
            }

            Assert.True(sw.ElapsedMilliseconds < 5000);
        }

        [Fact]
        public void Fuzz_RandomDamage_NeverHangsOrCrashes()
        {
            var valid = PdfDocument.Open(Asset("qpdf-objstm.pdf")).ToArray();
            var random = new Random(1234);
            var sw = Stopwatch.StartNew();
            for (var iteration = 0; iteration < 300; iteration++)
            {
                var data = (byte[])valid.Clone();
                var kind = iteration % 3;
                if (kind == 0)
                {
                    for (var k = 0; k < 20; k++)
                    {
                        data[random.Next(data.Length)] = (byte)random.Next(256);
                    }
                }
                else if (kind == 1)
                {
                    Array.Resize(ref data, random.Next(data.Length));
                }
                else
                {
                    var at = random.Next(data.Length - 200);
                    Array.Clear(data, at, random.Next(1, 200));
                }

                try
                {
                    var doc = PdfDocument.Open(data, new PdfReadOptions { MaxStreamSize = 8 * 1024 * 1024 });
                    doc.ToArray();
                }
                catch (PdfReadException)
                {
                }
            }

            Assert.True(sw.Elapsed.TotalSeconds < 60, "fuzzing took " + sw.Elapsed);
        }

        // ------------------------------------------------------------------ filters

        private static byte[] Decode(string filter, byte[] data, PdfDictionary? parms = null)
        {
            var dict = new PdfDictionary().Set("Filter", filter);
            if (parms != null)
            {
                dict.Set("DecodeParms", parms);
            }

            return StreamDecoder.Decode(new PdfStream(dict, data), null, 1 << 20);
        }

        [Fact]
        public void Filters_DecodeKnownVectors()
        {
            // ISO 32000-1 §7.4.4.2 example: "-----A---B".
            Assert.Equal("-----A---B", Encoding.ASCII.GetString(Decode("LZWDecode", new byte[] { 0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01 })));
            Assert.Equal("Hello, LifeOS!", Encoding.ASCII.GetString(Decode("ASCII85Decode", Encoding.ASCII.GetBytes("87cURD_*#)BkCpP;]u~>"))));
            Assert.Equal("Hi!", Encoding.ASCII.GetString(Decode("ASCIIHexDecode", Encoding.ASCII.GetBytes("48 69 21>"))));
            Assert.Equal("abcccc", Encoding.ASCII.GetString(Decode("RunLengthDecode", new byte[] { 1, (byte)'a', (byte)'b', 253, (byte)'c', 128 })));
        }

        [Fact]
        public void Filters_FlateWithPngPredictor()
        {
            // Two rows of 3 bytes, filter Up on the second row.
            var raw = new byte[] { 0, 10, 20, 30, 2, 1, 1, 1 };
            var parms = new PdfDictionary().Set("Predictor", 12).Set("Columns", 3);
            Assert.Equal(new byte[] { 10, 20, 30, 11, 21, 31 }, Decode("FlateDecode", Internal.Zlib.Compress(raw), parms));
        }

        [Fact]
        public void Names_RoundTripNonAsciiBytes()
        {
            var doc = OpenText(RawPdf.OnePage("/Group << /S /Transparency /CS /Caf#E9 >>"));
            Assert.Contains("/Caf#E9", Encoding.GetEncoding("ISO-8859-1").GetString(doc.ToArray(new PdfSaveOptions { CompressContent = false })));
        }
    }
}
