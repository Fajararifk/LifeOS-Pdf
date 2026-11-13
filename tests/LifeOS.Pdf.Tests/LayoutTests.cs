using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Layout.Engine;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class LayoutTests
    {
        private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

        /// <summary>Hex string of text as written for Helvetica (WinAnsi), to find text in content streams.</summary>
        private static string Hex(string text) => "<" + string.Concat(WinAnsiEncoding.Encode(text).Select(b => b.ToString("X2"))) + ">";

        private static (FlowDocument Doc, Section Section) NewDoc(Action<PageSetup>? setup = null)
        {
            var doc = new FlowDocument();
            doc.Info.CreationDate = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
            var page = new PageSetup { Margins = new Margins(50) };
            setup?.Invoke(page);
            return (doc, doc.AddSection(page));
        }

        private static List<string> Pages(FlowDocument doc) => PdfStructure.Verify(doc.Render().ToArray()).PageContents();

        // ------------------------------------------------------------- basics

        [Fact]
        public void SimpleParagraph_OnePage()
        {
            var (doc, s) = NewDoc();
            s.AddParagraph("Halo dunia");

            var pages = Pages(doc);
            Assert.Single(pages);
            Assert.Contains(Hex("Halo dunia") + " Tj", pages[0]);
        }

        [Fact]
        public void NoSections_Throws_EmptySection_GivesOnePage()
        {
            Assert.Throws<InvalidOperationException>(() => new FlowDocument().Render());

            var (doc, _) = NewDoc();
            Assert.Single(doc.Render().Pages);
        }

        [Fact]
        public void LongContent_Paginates_WithPageNumbersInFooter()
        {
            var (doc, s) = NewDoc();
            s.Footer.AddParagraph().AddText("Halaman ").AddPageNumber().AddText(" dari ").AddPageCount();
            for (var i = 0; i < 150; i++)
            {
                s.AddParagraph("Paragraf nomor " + i + " dengan beberapa kata supaya cukup panjang.");
            }

            var pages = Pages(doc);
            Assert.True(pages.Count > 2);
            for (var p = 0; p < pages.Count; p++)
            {
                Assert.Contains(Hex("Halaman " + (p + 1) + " dari " + pages.Count), pages[p]);
            }
        }

        [Fact]
        public void PageCount_IsCorrectWhenItGainsADigit()
        {
            // Exactly ten pages via page breaks: the count field must say 10 everywhere.
            var (doc, s) = NewDoc();
            s.Header.AddParagraph().AddPageNumber().AddText("/").AddPageCount();
            for (var i = 0; i < 10; i++)
            {
                s.AddParagraph("Bab " + i);
                if (i < 9)
                {
                    s.AddPageBreak();
                }
            }

            var pages = Pages(doc);
            Assert.Equal(10, pages.Count);
            Assert.Contains(Hex("1/10"), pages[0]);
            Assert.Contains(Hex("10/10"), pages[9]);
        }

        [Fact]
        public void PageNumberField_InBody_UsesTheActualPage()
        {
            var (doc, s) = NewDoc();
            s.AddParagraph("pertama");
            s.AddPageBreak();
            s.AddParagraph().AddText("Ini halaman ").AddPageNumber();

            Assert.Contains(Hex("Ini halaman 2"), Pages(doc)[1]);
        }

        [Fact]
        public void Output_IsDeterministic()
        {
            byte[] Build()
            {
                var (doc, s) = NewDoc();
                s.AddHeading("Judul");
                s.AddParagraph("Isi " + new string('x', 50));
                var t = s.AddTable();
                t.AddColumn(ColumnWidth.Auto);
                t.AddColumn(ColumnWidth.Relative());
                t.AddRow("a", "b");
                return doc.Render().ToArray();
            }

            Assert.Equal(Build(), Build());
        }

        // ------------------------------------------------------------- line breaking

        private static List<TextLine> Break(string text, double width, FontFamily? family = null)
        {
            var ctx = new LayoutContext(new DocumentStyles());
            var style = new ResolvedStyle(ctx.BaseStyle.Merge(new TextStyle { Family = family ?? FontFamily.Helvetica, Size = 10 }));
            var builder = new AtomBuilder(ctx);
            builder.Add(text, style);
            return LineBreaker.Break(builder.Finish(), width, width, ctx);
        }

        private static string LineText(TextLine line) => string.Concat(line.Pieces.Select(p => p.Text));

        [Fact]
        public void Break_AtSpaces_TrailingSpacesNotCounted()
        {
            var lines = Break("satu dua tiga empat lima", 60);
            Assert.True(lines.Count > 1);
            foreach (var line in lines)
            {
                Assert.True(line.Width <= 60.001, "too wide: " + LineText(line));
                Assert.False(LineText(line).EndsWith(" ", StringComparison.Ordinal));
            }

            Assert.Equal("satu dua tiga empat lima", string.Join(" ", lines.Select(LineText)));
        }

        [Fact]
        public void Break_SoftHyphenShowsDashOnlyWhenUsed()
        {
            var narrow = Break("Selamat­pagi", 45);
            Assert.Equal(new[] { "Selamat-", "pagi" }, narrow.Select(LineText).ToArray());

            var wide = Break("Selamat­pagi", 200);
            Assert.Equal(new[] { "Selamatpagi" }, wide.Select(LineText).ToArray());
        }

        [Fact]
        public void Break_AfterHyphen_NotAtNoBreakSpace()
        {
            Assert.Equal(new[] { "e-", "mail" }, Break("e-mail", 20).Select(LineText).ToArray());
            // "harga Rp" would fit in 50 pt, but the no-break space keeps "Rp 1.000" together.
            Assert.Equal(new[] { "harga", "Rp 1.000" }, Break("harga Rp 1.000", 50).Select(LineText).ToArray());
        }

        [Fact]
        public void Break_BetweenCjkCharacters_ButNotBeforeClosingPunctuation()
        {
            var lines = Break("日本語。テキスト", 12);
            Assert.True(lines.Count >= 4);
            Assert.DoesNotContain(lines, l => LineText(l).StartsWith("。", StringComparison.Ordinal));
        }

        [Fact]
        public void Break_OverlongWordIsSplitByCharacters()
        {
            var lines = Break("Supercalifragilistik", 30);
            Assert.True(lines.Count > 1);
            Assert.All(lines, l => Assert.True(l.Width <= 30.001));
            Assert.Equal("Supercalifragilistik", string.Concat(lines.Select(LineText)));
        }

        [Fact]
        public void Break_LineBreaksAndEmptyLines()
        {
            var lines = Break("satu\n\ndua", 200);
            Assert.Equal(new[] { "satu", string.Empty, "dua" }, lines.Select(LineText).ToArray());
            Assert.All(lines, l => Assert.NotEmpty(l.Pieces)); // empty lines still carry font metrics
        }

        // ------------------------------------------------------------- paragraphs

        [Fact]
        public void Justify_UsesTJ_LastLineIsNotStretched()
        {
            var (doc, s) = NewDoc();
            var p = s.AddParagraph(string.Join(" ", Enumerable.Repeat("kata", 60)));
            p.Format.Alignment = ParagraphAlignment.Justify;

            var content = Pages(doc)[0];
            Assert.Contains("] TJ", content);
            var lines = content.Split('\n').Where(l => l.EndsWith(" TJ", StringComparison.Ordinal) || l.EndsWith(" Tj", StringComparison.Ordinal)).ToList();
            Assert.EndsWith(" Tj", lines.Last()); // last line keeps natural spacing
        }

        [Fact]
        public void MixedStyles_InOneParagraph()
        {
            var (doc, s) = NewDoc();
            s.AddParagraph()
                .AddText("Normal ")
                .AddText("tebal", new TextStyle { Bold = true })
                .AddText(" merah", new TextStyle { Color = PdfColor.Red, Underline = true });

            var pdf = PdfStructure.Verify(doc.Render().ToArray());
            Assert.Contains("/BaseFont /Helvetica ", pdf.Text);
            Assert.Contains("/BaseFont /Helvetica-Bold ", pdf.Text);
            Assert.Contains("0.8627 0.149 0.149 rg", pdf.PageContents()[0]); // red text
            Assert.Contains(" l S\n", pdf.PageContents()[0]);               // underline stroke
        }

        [Fact]
        public void FontFallback_UsesSecondFamilyForMissingCharacters()
        {
            var (doc, s) = NewDoc();
            var noto = FontFamily.FromFiles(Path.Combine(AppContext.BaseDirectory, "TestAssets", "fonts", "NotoSans-subset.ttf"));
            doc.Styles.Normal = new TextStyle { Family = FontFamily.Helvetica, Fallbacks = { noto } };
            s.AddParagraph("Tiếng Việt");

            var pdf = PdfStructure.Verify(doc.Render().ToArray());
            Assert.Contains("/BaseFont /Helvetica ", pdf.Text);   // Latin letters stay in Helvetica
            Assert.Contains("+NotoSans-Regular", pdf.Text);        // ế, ệ come from Noto
        }

        [Fact]
        public void Indents_ShiftText()
        {
            var (doc, s) = NewDoc();
            var p = s.AddParagraph("Menjorok");
            p.Format.LeftIndent = 30;
            p.Format.FirstLineIndent = 20;

            Assert.Contains("1 0 0 1 100 ", Pages(doc)[0]); // 50 margin + 30 + 20
        }

        [Fact]
        public void Lists_DrawBulletsAndNumbers()
        {
            var (doc, s) = NewDoc();
            var bullets = s.AddList();
            bullets.AddItem("Makan");
            var numbers = bullets.AddList(ListKind.Numbered);
            numbers.AddItem("Sarapan");
            numbers.AddItem("Makan siang");

            var content = Pages(doc)[0];
            Assert.Contains("<95> Tj", content); // • in WinAnsi
            Assert.Contains(Hex("1."), content);
            Assert.Contains(Hex("2."), content);
            Assert.Contains("1 0 0 1 86 ", content); // nested text: 50 + 2 × 18
        }

        // ------------------------------------------------------------- pagination

        private static double BodyHeight => PageSize.A4.Height - 100;

        [Fact]
        public void KeepWithNext_MovesHeadingToNextPage()
        {
            var (doc, s) = NewDoc();
            s.AddSpacer(BodyHeight - 30);
            s.AddHeading("Bab Baru");
            s.AddParagraph("Isi bab baru.");

            var pages = Pages(doc);
            Assert.Equal(2, pages.Count);
            Assert.DoesNotContain(Hex("Bab Baru"), pages[0]);
            Assert.Contains(Hex("Bab Baru"), pages[1]);
        }

        [Fact]
        public void KeepTogether_MovesWholeParagraph_OtherwiseSplits()
        {
            string Text() => string.Join(" ", Enumerable.Repeat("baris", 120));

            var (doc, s) = NewDoc();
            s.AddSpacer(BodyHeight - 40);
            s.AddParagraph(Text()).Format.KeepTogether = true;
            var pages = Pages(doc);
            Assert.DoesNotContain(" Tj", pages[0]);

            var (doc2, s2) = NewDoc();
            s2.AddSpacer(BodyHeight - 40);
            s2.AddParagraph(Text());
            Assert.Contains(" Tj", Pages(doc2)[0]); // split: first lines stay on page 1
        }

        [Fact]
        public void HugeKeepTogetherParagraph_StillSplits_NoInfiniteLoop()
        {
            var (doc, s) = NewDoc();
            s.AddParagraph(string.Join(" ", Enumerable.Repeat("kata", 3000))).Format.KeepTogether = true;

            Assert.True(Pages(doc).Count > 1);
        }

        [Fact]
        public void PageBreakAndPageBreakBefore()
        {
            var (doc, s) = NewDoc();
            s.AddParagraph("satu");
            s.AddPageBreak();
            s.AddParagraph("dua");
            s.AddParagraph("tiga").Format.PageBreakBefore = true;

            Assert.Equal(3, Pages(doc).Count);
        }

        [Fact]
        public void TwoColumns_FillLeftThenRight()
        {
            var (doc, s) = NewDoc(p => p.Columns = 2);
            for (var i = 0; i < 80; i++)
            {
                s.AddParagraph("Kolom teks " + i);
            }

            var page = Pages(doc)[0];
            var columnWidth = (PageSize.A4.Width - 100 - 18) / 2;
            var secondColumnX = Math.Round(50 + columnWidth + 18, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("1 0 0 1 50 ", page);
            Assert.Contains("1 0 0 1 " + secondColumnX + " ", page);
        }

        [Fact]
        public void Sections_StartNewPagesWithOwnSize()
        {
            var doc = new FlowDocument();
            doc.AddSection().AddParagraph("A4");
            doc.AddSection(new PageSetup { PageSize = PageSize.A5.Landscape }).AddParagraph("A5");

            var pdf = PdfStructure.Verify(doc.Render().ToArray());
            Assert.Contains("/MediaBox [0 0 595.2756 841.8898]", pdf.Text);
            Assert.Contains("/MediaBox [0 0 595.2756 419.5276]", pdf.Text);
        }

        [Fact]
        public void DifferentFirstPageHeader()
        {
            var (doc, s) = NewDoc(p => p.DifferentFirstPage = true);
            s.FirstPageHeader.AddParagraph("SAMPUL");
            s.Header.AddParagraph("Lanjutan");
            s.AddParagraph("a");
            s.AddPageBreak();
            s.AddParagraph("b");

            var pages = Pages(doc);
            Assert.Contains(Hex("SAMPUL"), pages[0]);
            Assert.DoesNotContain(Hex("Lanjutan"), pages[0]);
            Assert.Contains(Hex("Lanjutan"), pages[1]);
        }

        [Fact]
        public void MarginsLargerThanPage_ThrowLayoutException()
        {
            var (doc, s) = NewDoc(p => p.Margins = new Margins(400));
            s.AddParagraph("x");

            Assert.Contains("margins", Assert.Throws<LayoutException>(() => doc.Render()).Message);
        }

        [Fact]
        public void Image_ShrinksToColumnAndPage()
        {
            var (doc, s) = NewDoc();
            s.AddImage(PdfImage.FromFile(Asset("rgb.jpg")), width: 2000);

            var content = Pages(doc)[0];
            var columnWidth = PageSize.A4.Width - 100;
            Assert.Contains(Math.Round(columnWidth, 4).ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 0 ", content);
        }

        // ------------------------------------------------------------- tables

        private static Table NewTable(Section s, params ColumnWidth[] widths)
        {
            var t = s.AddTable();
            foreach (var w in widths)
            {
                t.AddColumn(w);
            }

            return t;
        }

        [Fact]
        public void Table_HeaderRepeatsOnEveryPage_ZebraRows()
        {
            var (doc, s) = NewDoc();
            var t = NewTable(s, ColumnWidth.Auto, ColumnWidth.Relative());
            t.HeaderRows = 1;
            t.AlternateRowBackground = PdfColor.FromHex("#ABCDEF");
            t.AddRow("No", "Keterangan");
            for (var i = 1; i <= 150; i++)
            {
                t.AddRow(i.ToString(), "Baris ke-" + i);
            }

            var pages = Pages(doc);
            Assert.True(pages.Count >= 3);
            Assert.All(pages, p => Assert.Contains(Hex("Keterangan"), p));
            Assert.Contains("0.6706 0.8039 0.9373 rg", pages[0]); // zebra color
            Assert.Contains(Hex("150"), pages.Last());
        }

        [Fact]
        public void Table_ColumnWidths_FixedThenRelative()
        {
            var (doc, s) = NewDoc();
            var t = NewTable(s, ColumnWidth.Fixed(100), ColumnWidth.Relative());
            t.AddRow("kiri", "kanan");

            var content = Pages(doc)[0];
            Assert.Contains("1 0 0 1 55 ", content);  // 50 margin + 5 padding
            Assert.Contains("1 0 0 1 155 ", content); // + 100 fixed column
        }

        [Fact]
        public void Table_PercentAndAutoColumns()
        {
            var (doc, s) = NewDoc();
            var t = NewTable(s, ColumnWidth.Percent(50), ColumnWidth.Auto, ColumnWidth.Auto);
            t.AddRow("a", "pendek", "teks yang lebih panjang");

            var content = Pages(doc)[0];
            var half = Math.Round(50 + (PageSize.A4.Width - 100) / 2 + 5, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("1 0 0 1 " + half + " ", content);
        }

        [Fact]
        public void Table_RightAlignedColumn()
        {
            var (doc, s) = NewDoc();
            var t = s.AddTable();
            t.AddColumn(ColumnWidth.Relative());
            t.AddColumn(ColumnWidth.Fixed(100), ParagraphAlignment.Right);
            t.AddRow("Makan", "Rp450.000");

            var font = new PdfFont(StandardFont.Helvetica, 10);
            var x = Math.Round(PageSize.A4.Width - 50 - 5 - font.MeasureWidth("Rp450.000"), 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("1 0 0 1 " + x + " ", Pages(doc)[0]);
        }

        [Fact]
        public void Table_RowSpanGroupMovesTogether()
        {
            var (doc, s) = NewDoc();
            var t = NewTable(s, ColumnWidth.Fixed(80), ColumnWidth.Relative());
            for (var i = 0; i < 300; i++)
            {
                var row = t.AddRow();
                if (i % 3 == 0)
                {
                    row.Cells[0].AddParagraph("Grup " + i / 3).Format.SpaceAfter = 0;
                    row.Cells[0].RowSpan = 3;
                }

                row.Cells[1].AddParagraph("isi " + i).Format.SpaceAfter = 0;
            }

            var pages = Pages(doc);
            Assert.True(pages.Count > 1);
            for (var g = 0; g < 100; g++)
            {
                // All three rows of a group are on the page that has the group label.
                var page = pages.First(p => p.Contains(Hex("Grup " + g) + " Tj"));
                for (var r = 0; r < 3; r++)
                {
                    Assert.Contains(Hex("isi " + (g * 3 + r)) + " Tj", page);
                }
            }
        }

        [Fact]
        public void Table_ColSpanCoversCells()
        {
            var (doc, s) = NewDoc();
            var t = NewTable(s, ColumnWidth.Relative(), ColumnWidth.Relative(), ColumnWidth.Relative());
            var row = t.AddRow("Total");
            row.Cells[0].ColSpan = 2;
            row.Cells[2].AddParagraph("Rp1.000");
            t.AddRow("a", "b", "c");

            Pages(doc); // valid layout

            var bad = NewDoc();
            var t2 = NewTable(bad.Section, ColumnWidth.Relative(), ColumnWidth.Relative());
            var r2 = t2.AddRow("x", "tertutup");
            r2.Cells[0].ColSpan = 2;
            Assert.Contains("covered", Assert.Throws<LayoutException>(() => bad.Doc.Render()).Message);
        }

        [Fact]
        public void Table_RowTallerThanPage_ThrowsLayoutException()
        {
            var (doc, s) = NewDoc();
            var t = NewTable(s, ColumnWidth.Relative());
            t.AddRow(string.Join("\n", Enumerable.Repeat("baris", 120)));

            Assert.Contains("never split", Assert.Throws<LayoutException>(() => doc.Render()).Message);
        }

        [Fact]
        public void Table_FixedColumnsWiderThanTable_Throw()
        {
            var (doc, s) = NewDoc();
            NewTable(s, ColumnWidth.Fixed(400), ColumnWidth.Fixed(400)).AddRow("a", "b");

            Assert.Throws<LayoutException>(() => doc.Render());
        }

        [Fact]
        public void Table_CellBackgroundVerticalAlignAndNestedTable()
        {
            var (doc, s) = NewDoc();
            var t = NewTable(s, ColumnWidth.Relative(), ColumnWidth.Relative());
            var row = t.AddRow();
            row.Cells[0].AddParagraph(string.Join("\n", Enumerable.Repeat("tinggi", 5)));
            row.Cells[1].Background = PdfColor.FromHex("#FF0000");
            row.Cells[1].VerticalAlignment = VerticalAlignment.Bottom;
            var inner = row.Cells[1].AddTable();
            inner.AddColumn(ColumnWidth.Auto);
            inner.AddRow("dalam");

            var content = Pages(doc)[0];
            Assert.Contains("1 0 0 rg", content);
            Assert.Contains(Hex("dalam"), content);
        }

        [Fact]
        public void Table_ManyRows_IsFast()
        {
            var (doc, s) = NewDoc();
            s.Footer.AddParagraph().AddText("Halaman ").AddPageNumber().AddText(" dari ").AddPageCount();
            var t = NewTable(s, ColumnWidth.Auto, ColumnWidth.Relative(), ColumnWidth.Fixed(90));
            t.HeaderRows = 1;
            t.AddRow("Tanggal", "Catatan", "Nominal");
            for (var i = 0; i < 1500; i++)
            {
                t.AddRow("2026-11-" + (i % 28 + 1).ToString("00"), "Transaksi nomor " + i, "Rp" + (i * 1250).ToString("N0"));
            }

            var sw = Stopwatch.StartNew();
            var pdf = doc.Render();
            pdf.ToArray();
            sw.Stop();

            Assert.True(pdf.Pages.Count >= 30);
            Assert.True(sw.ElapsedMilliseconds < 5000, "took " + sw.ElapsedMilliseconds + " ms");
        }
    }
}
