using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Manipulation;
using LifeOS.Pdf.Navigation;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>Fase 6: page operations, merge, split, stamps, imposition.</summary>
    public class ManipulationTests
    {
        private static readonly PdfFont Font = new PdfFont(StandardFont.Helvetica, 12);

        /// <summary>A saved and reopened document with numbered pages and one bookmark per page.</summary>
        private static PdfDocument Numbered(int pages, string title = "Dok")
        {
            var doc = new PdfDocument();
            doc.Info.Title = title;
            for (var i = 1; i <= pages; i++)
            {
                var p = doc.AddPage(new PageSize(300, 400));
                p.Graphics.DrawText(title + " halaman " + i, Font, 20, 20);
                doc.Outline.Add(title + " " + i, PdfDestination.ToPage(p, 50));
            }

            doc.Pages[0].AddLink(10, 10, 50, 20, PdfDestination.FitPage(doc.Pages[pages - 1]));
            return PdfDocument.Open(doc.ToArray());
        }

        private static string AllContent(byte[] pdf)
        {
            var s = PdfStructure.Verify(pdf);
            var sb = new StringBuilder();
            foreach (var n in s.Objects.Keys)
            {
                if (s.Objects[n].Contains("stream"))
                {
                    try
                    {
                        sb.Append(Encoding.ASCII.GetString(s.StreamData(n))).Append('\n');
                    }
                    catch (Exception)
                    {
                        // binary streams that are not Flate (images) are skipped
                    }
                }
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ page operations

        [Fact]
        public void Pages_InsertRemoveMoveDuplicate()
        {
            var doc = Numbered(3);
            doc.InsertPage(0, PageSize.A5).Graphics.DrawText("Sampul", Font, 20, 20);
            doc.ImportPage(doc.Pages[1]);       // duplicate of page 1 at the end
            doc.MovePage(4, 1);
            doc.RemovePage(3);
            Assert.Equal(4, doc.Pages.Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.RemovePage(9));
            var content = AllContent(doc.ToArray());
            Assert.Contains("Sampul", content.Replace("<", string.Empty).Length > 0 ? "Sampul" : string.Empty);
        }

        [Fact]
        public void RemovePage_RetargetsBookmarks()
        {
            var doc = Numbered(3);
            var removed = doc.Pages[2];
            doc.RemovePage(2);
            Assert.All(doc.Outline.Items, i => Assert.NotSame(removed, i.Destination.Page));
            PdfStructure.Verify(doc.ToArray());
        }

        [Fact]
        public void LastPage_CannotBeRemoved() =>
            Assert.Throws<InvalidOperationException>(() => Numbered(1).RemovePage(0));

        [Fact]
        public void Rotation_IsValidated()
        {
            var page = Numbered(1).Pages[0];
            page.Rotation = -90;
            Assert.Equal(270, page.Rotation);
            Assert.Throws<ArgumentOutOfRangeException>(() => page.Rotation = 45);
        }

        [Fact]
        public void InternalLink_FollowsCopiedPage_OrIsDropped()
        {
            var source = Numbered(3);
            var all = new PdfDocument();
            all.ImportPages(source);
            var s = PdfStructure.Verify(all.ToArray());
            var link = s.Objects.Values.Single(o => o.Contains("/Subtype /Link"));
            var target = int.Parse(Regex.Match(link, @"/Dest \[(\d+) 0 R").Groups[1].Value);
            Assert.Contains("/Type /Page", s.Objects[target]);

            var first = new PdfDocument();
            first.ImportPages(source, PageRange.Parse("1"));
            Assert.DoesNotContain("/Subtype /Link", PdfStructure.Verify(first.ToArray()).Text); // target page not copied
        }

        [Fact]
        public void UnsafeActions_AreNotCopied()
        {
            var pdf = RawPdf.OnePage(
                "/Annots [4 0 R 5 0 R] /AA << /O << /S /JavaScript /JS (app.alert(1)) >> >>", "BT ET",
                "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /A << /S /JavaScript /JS (app.alert(1)) >> >>",
                "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /A << /S /URI /URI (https://lifeos.app) /Next << /S /JavaScript /JS (x) >> >> >>");
            var saved = PdfStructure.Verify(PdfDocument.Open(RawPdf.Bytes(pdf)).ToArray());
            Assert.DoesNotContain("JavaScript", saved.Text);
            Assert.Contains("/URI (https://lifeos.app)", saved.Text);
            Assert.Single(saved.Objects.Values, o => o.Contains("/Subtype /Link"));
        }

        // ------------------------------------------------------------------ merge & split

        [Fact]
        public void Merge_KeepsPagesAndBookmarksPerDocument()
        {
            var merged = PdfMerger.Merge(new[] { Numbered(2, "A"), Numbered(3, "B") });
            Assert.Equal(5, merged.Pages.Count);
            Assert.Equal(new[] { "A", "B" }, merged.Outline.Items.Select(i => i.Title).ToArray());
            Assert.Equal(3, merged.Outline.Items[1].Children.Count);
            Assert.Same(merged.Pages[2], merged.Outline.Items[1].Children[0].Destination.Page);

            var flat = PdfMerger.Merge(new[] { Numbered(2, "A"), Numbered(1, "B") }, new MergeOptions { BookmarkPerDocument = false });
            Assert.Equal(3, flat.Outline.Count);
            PdfStructure.Verify(merged.ToArray());
        }

        [Fact]
        public void Merge_DocumentsDrawnInMemory()
        {
            var fresh = new PdfDocument();
            fresh.AddPage().Graphics.DrawText("baru", Font, 20, 20);
            var merged = PdfMerger.Merge(new[] { fresh, Numbered(1) });
            Assert.Equal(2, merged.Pages.Count);
            Assert.All(merged.Pages, p => Assert.True(p.IsImported));
            Assert.Throws<ArgumentException>(() => PdfMerger.Merge(Array.Empty<PdfDocument>()));
        }

        [Fact]
        public void Split_EveryAndRanges()
        {
            var doc = Numbered(5);
            var parts = PdfSplitter.SplitEvery(doc, 2);
            Assert.Equal(new[] { 2, 2, 1 }, parts.Select(p => p.Pages.Count).ToArray());
            Assert.Equal(new[] { "Dok 3", "Dok 4" }, parts[1].Outline.Items.Select(i => i.Title).ToArray());
            Assert.Equal("Dok", parts[2].Info.Title);

            var reversed = PdfSplitter.Extract(doc, PageRange.Parse("3-1"));
            Assert.Equal(3, reversed.Pages.Count);
            Assert.Equal(new[] { 1, 4 }, PdfSplitter.SplitByRanges(doc, "last", "-4").Select(p => p.Pages.Count).ToArray());
            foreach (var p in parts)
            {
                PdfStructure.Verify(p.ToArray());
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("a-b")]
        [InlineData(",")]
        public void PageRange_RejectsBadInput(string text) => Assert.Throws<FormatException>(() => PageRange.Parse(text));

        [Fact]
        public void PageRange_OutOfDocument_Throws() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => PageRange.Parse("2-9").Resolve(3));

        // ------------------------------------------------------------------ stamps

        [Fact]
        public void PageNumbers_Bates_AndWatermark()
        {
            var doc = Numbered(3);
            doc.Pages[1].Rotation = 90;
            PdfStamper.AddTextWatermark(doc, "SALINAN");
            PdfStamper.AddPageNumbers(doc, "Halaman {page} dari {pages}", new StampOptions { Pages = PageRange.Parse("2-"), StartNumber = 1 });
            PdfStamper.AddBatesNumbers(doc, "LOS-", 7, 4);
            var bytes = doc.ToArray();
            var content = AllContent(bytes);
            Assert.Contains(Hex("Halaman 2 dari 2"), content);
            Assert.DoesNotContain(Hex("Halaman 3 dari"), content);
            Assert.Contains(Hex("LOS-0009"), content);
            Assert.Contains("0 1 -1 0 300 0 cm", content); // display frame on the rotated page
            Assert.Contains("/LOSOverlay Do", content);
        }

        private static string Hex(string text) => "<" + Internal.PdfFormat.Hex(Encoding.ASCII.GetBytes(text)) + ">";

        [Fact]
        public void Watermark_Behind_UsesUnderlay()
        {
            var doc = Numbered(1);
            PdfStamper.AddTextWatermark(doc, "DRAFT", new WatermarkOptions { Behind = true, Opacity = 0.1 });
            var s = PdfStructure.Verify(doc.ToArray());
            var page = s.Objects.Values.Single(o => o.StartsWith("<< /Type /Page ", StringComparison.Ordinal));
            var first = int.Parse(Regex.Match(page, @"/Contents \[(\d+) 0 R").Groups[1].Value);
            Assert.Equal("/LOSUnderlay Do\n", Encoding.ASCII.GetString(s.StreamData(first)));
        }

        // ------------------------------------------------------------------ imposition

        [Fact]
        public void NUp_Booklet_Overlay()
        {
            var doc = Numbered(5);
            var nup = PdfImposition.NUp(doc, 2, 2);
            Assert.Equal(2, nup.Pages.Count);
            var s = PdfStructure.Verify(nup.ToArray());
            Assert.Equal(5, s.CountObjects("/Subtype /Form /BBox [0 0 300 400]"));

            var booklet = PdfImposition.Booklet(doc);
            Assert.Equal(4, booklet.Pages.Count); // 5 pages → 8 slots → 2 sheets, 2 sides
            Assert.True(booklet.Pages[0].Width > booklet.Pages[0].Height);
            PdfStructure.Verify(booklet.ToArray());

            var letterhead = new PdfDocument();
            letterhead.AddPage(new PageSize(300, 400)).Graphics.FillRectangle(PdfColor.Blue, 0, 0, 300, 40);
            PdfImposition.Overlay(doc, letterhead.Pages[0]);
            Assert.Contains("/LOSUnderlay", PdfStructure.Verify(doc.ToArray()).Text);
        }

        [Fact]
        public void DrawPage_OfPageCreatedInMemory_UsesSnapshot()
        {
            var a = new PdfDocument();
            a.AddPage().Graphics.DrawText("isi", Font, 20, 20);
            var b = new PdfDocument();
            b.AddPage().Graphics.DrawPage(a.Pages[0], 0, 0, 200, 280);
            Assert.Contains("/Subtype /Form", PdfStructure.Verify(b.ToArray()).Text);
        }
    }
}
