using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Extraction;
using LifeOS.Pdf.Extraction.Engine;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Layout;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>Fase 7: text, search, images, tables, conversion and redaction.</summary>
    public class ExtractionTests
    {
        private static readonly PdfFont Helvetica = new PdfFont(StandardFont.Helvetica, 12);

        private static PdfDocument Open(string pdf) => PdfDocument.Open(RawPdf.Bytes(pdf));

        private static string Noto => Path.Combine(AppContext.BaseDirectory, "TestAssets", "fonts", "NotoSans-subset.ttf");

        // ------------------------------------------------------------------ text

        [Fact]
        public void OwnDocument_TextPositionsAndWords()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage(new PageSize(400, 300));
            page.Graphics.DrawText("Halo dunia", Helvetica, 50, 40);
            page.Graphics.DrawText("Rp1.924.500", new PdfFont(StandardFont.HelveticaBold, 20), 50, 100);
            var words = page.GetWords();
            Assert.Equal(new[] { "Halo", "dunia", "Rp1.924.500" }, words.Select(w => w.Text).ToArray());
            Assert.InRange(words[0].Bounds.X, 49.9, 50.1);
            Assert.InRange(words[0].Bounds.Y, 38, 42);             // top of the box ≈ the y given to DrawText
            Assert.Equal("Helvetica", words[0].FontName);
            Assert.Equal(12, words[0].FontSize);
            Assert.True(words[2].Bold);
            Assert.Equal("Halo dunia\n\nRp1.924.500", page.ExtractText());
        }

        [Fact]
        public void TrueTypeUnicode_AndLayoutEngine_RoundTrip()
        {
            var flow = new FlowDocument();
            flow.Styles.Normal.Family = FontFamily.FromFiles(Noto);
            var s = flow.AddSection();
            s.AddHeading("Ringkasan");
            s.AddParagraph("Tiếng Việt, Ελληνικά, Русский — Rp7.482.500.").Format.Alignment = ParagraphAlignment.Justify;
            var text = flow.Render().ExtractText();
            Assert.Contains("Ringkasan", text);
            Assert.Contains("Tiếng Việt, Ελληνικά, Русский — Rp7.482.500.", text);
        }

        [Fact]
        public void SimpleFont_Differences_GlyphNames_AndWidths()
        {
            // Codes 1–4 named by /Differences; "fi" is a ligature glyph name.
            var font = "<< /Type /Font /Subtype /Type1 /BaseFont /Custom /FirstChar 1 /LastChar 4 /Widths [600 600 500 250] " +
                       "/Encoding << /Differences [1 /A /fi /uni00E9 /space] >> >>";
            var pdf = RawPdf.OnePage("/Resources << /Font << /F1 4 0 R >> >>",
                "BT /F1 10 Tf 20 100 Td <01020304010203> Tj ET", font);
            var page = Open(pdf).Pages[0];
            Assert.Equal("Aﬁé Aﬁé", page.ExtractText());
            var glyphs = page.GetGlyphs();
            Assert.InRange(glyphs[1].Bounds.X - glyphs[0].Bounds.X, 5.99, 6.01); // width 600 at 10 pt
        }

        [Fact]
        public void Type0_IdentityH_ToUnicodeRanges_AndCidWidths()
        {
            var cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange " +
                       "1 beginbfrange <0010> <0012> <0041> endbfrange 1 beginbfrange <0020> <0021> [<0048> <D83DDE00>] endbfrange " +
                       "1 beginbfchar <0030> <00660069> endbfchar endcmap end end";
            var pdf = RawPdf.OnePage("/Resources << /Font << /F1 4 0 R >> >>",
                "BT /F1 10 Tf 20 100 Td <0010001100120020002100300030> Tj ET",
                "<< /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+Noto /Encoding /Identity-H /DescendantFonts [5 0 R] /ToUnicode 6 0 R >>",
                "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /ABCDEF+Noto /DW 500 /W [16 [700 800] 32 33 300] /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> >>",
                "<< /Length " + cmap.Length + " >>\nstream\n" + cmap + "\nendstream");
            var glyphs = Open(pdf).Pages[0].GetGlyphs();
            Assert.Equal("ABCH\U0001F600fifi", string.Concat(glyphs.Select(g => g.Text)));
            Assert.Equal("Noto", glyphs[0].FontName);
            Assert.InRange(glyphs[1].Bounds.X - glyphs[0].Bounds.X, 6.99, 7.01); // CID 16 width 700
            Assert.InRange(glyphs[2].Bounds.X - glyphs[1].Bounds.X, 7.99, 8.01); // CID 17 width 800
            Assert.InRange(glyphs[3].Bounds.X - glyphs[2].Bounds.X, 4.99, 5.01); // CID 18 default width 500
        }

        [Fact]
        public void Spacing_FromPositions_KerningAndScaling()
        {
            // No space characters: words come from the gaps (TJ displacement, Tw is not used).
            var pdf = RawPdf.OnePage("", "BT /F1 12 Tf 20 100 Td 110 Tz [(Halo) -300 (dunia) 20 (!)] TJ 0 -20 Td (Rp) Tj 3 Tc (500) Tj ET");
            var page = Open(pdf).Pages[0];
            Assert.Equal("Halo dunia!\nRp500", page.ExtractText());
        }

        [Fact]
        public void RotatedPage_ReadsInDisplayOrientation()
        {
            var pdf = RawPdf.OnePage("/Rotate 90", "BT /F1 12 Tf 20 380 Td (Atas) Tj 0 -20 Td (Bawah) Tj ET");
            var page = Open(pdf).Pages[0];
            // Text runs up the page in user space, left to right on screen after the 90° turn.
            var lines = page.GetTextLines();
            Assert.Equal(new[] { "Atas", "Bawah" }, lines.Select(l => l.Text).ToArray());
        }

        [Fact]
        public void Area_AndInvisibleText_Filters()
        {
            var pdf = RawPdf.OnePage("", "BT /F1 12 Tf 20 380 Td (Kiri) Tj 200 0 Td (Kanan) Tj 3 Tr -200 -40 Td (Ocr) Tj ET");
            var page = Open(pdf).Pages[0];
            Assert.Equal("Kanan", page.ExtractText(new TextExtractionOptions { Area = new PdfRect(150, 0, 150, 40) }));
            Assert.DoesNotContain("Ocr", page.ExtractText(new TextExtractionOptions { IncludeInvisibleText = false }));
            Assert.Contains("Ocr", page.ExtractText());
        }

        [Fact]
        public void FormXObjects_AreFollowed_AndCyclesStop()
        {
            var form = "BT /F1 10 Tf 0 0 Td (DalamForm) Tj ET /X1 Do";
            var pdf = RawPdf.OnePage("/Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> /XObject << /X1 4 0 R >> >>",
                "q 1 0 0 1 50 200 cm /X1 Do Q",
                "<< /Type /XObject /Subtype /Form /BBox [0 0 100 20] /Length " + form.Length + " >>\nstream\n" + form + "\nendstream");
            var text = Open(pdf).Pages[0].ExtractText();
            Assert.StartsWith("DalamForm", text); // the form draws itself again: stopped by the cycle guard
        }

        [Fact]
        public void Search_AcrossWordsAndLines()
        {
            var doc = new PdfDocument();
            var g = doc.AddPage().Graphics;
            g.DrawText("Nomor rekening 123", Helvetica, 50, 50);
            g.DrawText("456 7890 atas nama", Helvetica, 50, 66);
            doc.AddPage().Graphics.DrawText("REKENING lain", Helvetica, 50, 50);
            var hits = doc.Search("rekening 123 456");
            Assert.Single(hits);
            Assert.Equal(2, hits[0].Rects.Count);
            Assert.Equal("rekening 123 456", hits[0].Text);
            Assert.Equal(2, doc.Search("REKENING").Count);
            Assert.Single(doc.Search("REKENING", ignoreCase: false));
        }

        // ------------------------------------------------------------------ images

        private static byte[] PngPixels(byte[] png, out int channels)
        {
            var color = png[25];
            channels = color == 0 ? 1 : color == 4 ? 2 : color == 2 ? 3 : 4;
            var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            var idat = new MemoryStream();
            for (var i = 8; i < png.Length;)
            {
                var length = (png[i] << 24) | (png[i + 1] << 16) | (png[i + 2] << 8) | png[i + 3];
                if (Encoding.ASCII.GetString(png, i + 4, 4) == "IDAT")
                {
                    idat.Write(png, i + 8, length);
                }

                i += 12 + length;
            }

            var raw = Internal.Zlib.Decompress(idat.ToArray());
            var row = width * channels;
            var result = new MemoryStream();
            for (var p = 0; p < raw.Length; p += row + 1)
            {
                result.Write(raw, p + 1, row); // filter byte 0 (None) is what PngWriter writes
            }

            return result.ToArray();
        }

        [Fact]
        public void Images_InlineIndexedAndJpeg()
        {
            var image = "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 8 /ColorSpace [/Indexed /DeviceRGB 1 <FF0000 0000FF>] " +
                        "/SMask 5 0 R /Length 2 >>\nstream\n\u0000\u0001\nendstream";
            var smask = "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Length 2 >>\nstream\nÿ\u0080\nendstream";
            var pdf = RawPdf.OnePage("/Resources << /XObject << /Im9 4 0 R >> >>",
                "q 100 0 0 50 10 300 cm /Im9 Do Q q 20 0 0 10 10 10 cm BI /W 2 /H 2 /BPC 1 /CS /G /D [1 0] ID @\u0080 EI Q",
                image, smask);
            var images = Open(pdf).Pages[0].GetImages();
            Assert.Equal(2, images.Count);
            Assert.Equal("Im9", images[0].Name);
            Assert.Equal(ExtractedImageFormat.Png, images[0].Format);
            Assert.InRange(images[0].Bounds.Y, 49.9, 50.1);         // 400 − 300 − 50
            Assert.Equal(100, images[0].Bounds.Width);
            Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 128 }, PngPixels(images[0].GetBytes(), out var ch));
            Assert.Equal(4, ch);
            Assert.True(images[1].IsInline);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngPixels(images[1].GetBytes(), out _)); // decode [1 0] inverts bits

            var doc = new PdfDocument();
            var jpeg = Path.Combine(AppContext.BaseDirectory, "TestAssets", "rgb.jpg");
            doc.AddPage().Graphics.DrawImage(Images.PdfImage.FromFile(jpeg), 10, 10, 100);
            var extracted = doc.Pages[0].GetImages().Single();
            Assert.Equal(ExtractedImageFormat.Jpeg, extracted.Format);
            Assert.Equal(File.ReadAllBytes(jpeg), extracted.GetBytes()); // JPEG data unchanged
        }

        // ------------------------------------------------------------------ tables

        private static PdfDocument Invoice()
        {
            var flow = new FlowDocument();
            var s = flow.AddSection();
            s.AddParagraph("Mutasi rekening");
            var t = s.AddTable();
            t.AddColumn(ColumnWidth.Auto);
            t.AddColumn(ColumnWidth.Relative(1));
            t.AddColumn(ColumnWidth.Auto, ParagraphAlignment.Right);
            t.HeaderRows = 1;
            t.AddRow("Tanggal", "Keterangan", "Jumlah");
            t.AddRow("01/11", "Transfer masuk, gaji", "Rp18.500.000");
            t.AddRow("02/11", "Belanja \"Indomaret\"", "Rp120.000");
            return flow.Render();
        }

        [Fact]
        public void Tables_LatticeToCsvAndJson()
        {
            var table = Invoice().Pages[0].ExtractTables().Single();
            Assert.Equal(TableDetection.Lattice, table.Method);
            Assert.Equal(3, table.RowCount);
            Assert.Equal("Rp18.500.000", table[1, 2]);
            Assert.Equal("Tanggal,Keterangan,Jumlah\r\n01/11,\"Transfer masuk, gaji\",Rp18.500.000\r\n02/11,\"Belanja \"\"Indomaret\"\"\",Rp120.000\r\n", table.ToCsv());
            Assert.Contains("{\"Tanggal\": \"02/11\", \"Keterangan\": \"Belanja \\\"Indomaret\\\"\", \"Jumlah\": \"Rp120.000\"}", table.ToJson());
        }

        [Fact]
        public void Tables_StreamWithoutLines_AndContinuationRows()
        {
            var doc = new PdfDocument();
            var g = doc.AddPage().Graphics;
            var y = 60.0;
            foreach (var row in new[] { ("TGL", "KETERANGAN", "MUTASI"), ("01/11", "TRSF E-BANKING", "500.000"), ("", "ke Fajar", ""), ("03/11", "BIAYA ADM", "15.000") })
            {
                g.DrawText(row.Item1, Helvetica, 50, y);
                g.DrawText(row.Item2, Helvetica, 150, y);
                g.DrawText(row.Item3, Helvetica, 450, y, align: TextAlign.Right);
                y += 16;
            }

            g.DrawText("• bukan tabel", Helvetica, 50, 300);
            g.DrawText("• juga bukan", Helvetica, 50, 316);
            var tables = doc.Pages[0].ExtractTables();
            var table = Assert.Single(tables);
            Assert.Equal(TableDetection.Stream, table.Method);
            Assert.Equal(3, table.RowCount);
            Assert.Equal("TRSF E-BANKING ke Fajar", table[1, 1]);
            Assert.Equal("15.000", table[2, 2]);
        }

        // ------------------------------------------------------------------ conversion

        [Fact]
        public void Html_HasHeadingsParagraphsAndTables()
        {
            var flow = new FlowDocument();
            var s = flow.AddSection();
            s.AddHeading("Laporan <Bulanan>");
            s.AddParagraph("Isi paragraf & catatan.");
            var html = PdfTextConverter.ToHtml(flow.Render());
            Assert.Contains("<h1>Laporan &lt;Bulanan&gt;</h1>", html);
            Assert.Contains("<p>Isi paragraf &amp; catatan.</p>", html);
            Assert.Contains("<table>", PdfTextConverter.ToHtml(Invoice()));
            Assert.Contains("\f", PdfTextConverter.ToText(Merge2()));
        }

        private static PdfDocument Merge2()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.DrawText("satu", Helvetica, 50, 50);
            doc.AddPage().Graphics.DrawText("dua", Helvetica, 50, 50);
            return doc;
        }

        // ------------------------------------------------------------------ redaction

        [Fact]
        public void Redact_RemovesGlyphsKeepsNeighbours()
        {
            var pdf = RawPdf.OnePage("", "BT /F1 12 Tf 20 300 Td (NIK 3174012345678901 aktif) Tj 0 -20 Td (Nama Fajar) ' ET");
            var doc = Open(pdf);
            var before = doc.Pages[0].GetWords().Single(w => w.Text == "aktif").Bounds;
            Assert.Equal(1, PdfRedactor.RedactText(doc, "3174012345678901"));
            var saved = PdfDocument.Open(doc.ToArray());
            var page = saved.Pages[0];
            Assert.DoesNotContain("3174", page.ExtractText());
            var after = page.GetWords().Single(w => w.Text == "aktif").Bounds;
            Assert.InRange(after.X - before.X, -0.01, 0.01);              // the rest of the line did not move
            Assert.Contains("Nama Fajar", page.ExtractText());
            Assert.DoesNotContain("3174", Encoding.ASCII.GetString(saved.ToArray(new PdfSaveOptions { CompressContent = false })));
        }

        [Fact]
        public void Redact_FormsImagesVectorsAndAnnotations()
        {
            var form = "BT /F1 10 Tf 0 5 Td (Rahasia) Tj ET";
            var pdf = RawPdf.OnePage(
                "/Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> /XObject << /Fm 4 0 R /Im 5 0 R >> >> /Annots [6 0 R]",
                "q 1 0 0 1 50 200 cm /Fm Do Q q 40 0 0 40 200 200 cm /Im Do Q 0 0 1 rg 50 100 10 10 re 280 100 10 10 re f BT /F1 10 Tf 150 300 Td (Publik) Tj ET",
                "<< /Type /XObject /Subtype /Form /BBox [0 0 100 20] /Length " + form.Length + " >>\nstream\n" + form + "\nendstream",
                "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Length 1 >>\nstream\n\u0000\nendstream",
                "<< /Type /Annot /Subtype /Text /Rect [200 200 220 220] /Contents (catatan) >>");
            var doc = Open(pdf);
            // Area covers the form text, the image, the annotation and the first blue square.
            var removed = PdfRedactor.Redact(doc.Pages[0], new PdfRect(40, 140, 200, 170));
            Assert.True(removed >= 4, "removed " + removed);
            var saved = PdfStructure.Verify(doc.ToArray(new PdfSaveOptions { CompressContent = false }));
            var reopened = PdfDocument.Open(doc.ToArray());
            Assert.DoesNotContain("Rahasia", reopened.ExtractText());
            Assert.Contains("Publik", reopened.ExtractText());
            Assert.Empty(reopened.Pages[0].GetImages());
            Assert.DoesNotContain("/Subtype /Image", saved.Text);
            Assert.DoesNotContain("(catatan)", saved.Text);
            var content = Encoding.ASCII.GetString(PageAnalysis.ContentBytes(reopened.Pages[0].Source!));
            Assert.DoesNotContain("50 100 10 10 re", content);
            Assert.Contains("280 100 10 10 re", content);
        }

        [Fact]
        public void Redact_PageDrawnInMemory_AndMetadata()
        {
            var doc = new PdfDocument();
            doc.Info.Subject = "Slip gaji Fajar Arif";
            doc.AddPage().Graphics.DrawText("Fajar Arif Rp20.414.577", Helvetica, 50, 50);
            doc.Outline.Add("Fajar Arif", Navigation.PdfDestination.ToPage(doc.Pages[0]));
            PdfRedactor.RedactText(doc, "fajar arif");
            Assert.Equal("Slip gaji [redacted]", doc.Info.Subject);
            Assert.Equal("[redacted]", doc.Outline.Items[0].Title);
            var bytes = doc.ToArray(new PdfSaveOptions { CompressContent = false });
            Assert.DoesNotContain("Fajar", Encoding.ASCII.GetString(bytes));
            Assert.Contains("Rp20.414.577", PdfDocument.Open(bytes).ExtractText());
        }

        [Fact]
        public void ContentReader_RoundTripsOperators()
        {
            var content = "q 1 0 0 1 10 20 cm BT /F1 12 Tf [(A) -120.5 (B)] TJ ET BI /W 1 /H 1 /CS /G /BPC 8 ID ÿ EI Q";
            var ops = ContentReader.Read(RawPdf.Bytes(content));
            Assert.Equal(new[] { "q", "cm", "BT", "Tf", "TJ", "ET", "BI", "Q" }, ops.Select(o => o.Op).ToArray());
            var again = ContentReader.Read(ContentReader.Write(ops));
            Assert.Equal(ops.Select(o => o.Op), again.Select(o => o.Op));
            Assert.Equal(new byte[] { 0xFF }, again[6].InlineData);
        }
    }
}
