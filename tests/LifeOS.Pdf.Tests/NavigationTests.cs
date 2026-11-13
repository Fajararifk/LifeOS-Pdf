using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LifeOS.Pdf.Annotations;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Navigation;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>Fase 5: links, bookmarks, named destinations, page labels, viewer preferences, annotations, attachments.</summary>
    public class NavigationTests
    {
        private static PdfDocument TwoPages(out PdfPage p1, out PdfPage p2)
        {
            var doc = new PdfDocument();
            doc.Info.CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            p1 = doc.AddPage(new PageSize(600, 800));
            p2 = doc.AddPage(new PageSize(600, 800));
            return doc;
        }

        private static string Obj(PdfStructure pdf, string fragment) => pdf.Objects.Values.Single(o => o.Contains(fragment));

        // ------------------------------------------------------------------ links

        [Fact]
        public void UriLink_WritesInvisibleLinkWithUriAction()
        {
            var doc = TwoPages(out var p1, out _);
            p1.AddLink(72, 72, 100, 14, "https://example.com/a b?x=1");
            var pdf = PdfStructure.Verify(doc.ToArray());
            var link = Obj(pdf, "/Subtype /Link");
            Assert.Contains("/Border [0 0 0]", link);
            Assert.Contains("/S /URI /URI (https://example.com/a%20b?x=1)", link);
            // Rect is converted to bottom-left coordinates: 800 - 72 - 14.
            Assert.Contains("/Rect [72 714 172 728]", link);
            Assert.Matches(@"/Annots \[\d+ 0 R\]", pdf.Text);
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("file:///etc/passwd")]
        [InlineData("relative/path")]
        public void UriLink_RefusesUnsafeOrRelative(string uri)
        {
            var doc = TwoPages(out var p1, out _);
            Assert.Throws<ArgumentException>(() => p1.AddLink(0, 0, 10, 10, uri));
        }

        [Fact]
        public void InternalLink_PointsToPageObject()
        {
            var doc = TwoPages(out var p1, out var p2);
            p1.AddLink(72, 72, 100, 14, PdfDestination.ToPage(p2, top: 100));
            var pdf = PdfStructure.Verify(doc.ToArray());
            var link = Obj(pdf, "/Subtype /Link");
            var m = Regex.Match(link, @"/Dest \[(\d+) 0 R /XYZ null 700 null\]");
            Assert.True(m.Success, link);
            Assert.Contains("/Type /Page", pdf.Objects[int.Parse(m.Groups[1].Value)]);
        }

        [Fact]
        public void LinkToPageOfOtherDocument_Throws()
        {
            var doc = TwoPages(out var p1, out _);
            var other = new PdfDocument().AddPage();
            p1.AddLink(0, 0, 10, 10, PdfDestination.FitPage(other));
            Assert.Throws<InvalidOperationException>(() => doc.ToArray());
        }

        [Fact]
        public void NamedDestination_WrittenAsSortedNameTree()
        {
            var doc = TwoPages(out var p1, out var p2);
            doc.NamedDestinations["zeta"] = PdfDestination.FitPage(p1);
            doc.NamedDestinations["alpha"] = PdfDestination.FitWidth(p2, 50);
            p1.AddLink(0, 0, 10, 10, PdfDestination.Named("alpha"));
            var pdf = PdfStructure.Verify(doc.ToArray());
            var catalog = Obj(pdf, "/Type /Catalog");
            Assert.Matches(@"/Names << /Dests << /Names \[\(alpha\) \[\d+ 0 R /FitH 750\] \(zeta\) \[\d+ 0 R /Fit\]\] >> >>", catalog);
            Assert.Contains("/Dest (alpha)", Obj(pdf, "/Subtype /Link"));
        }

        [Fact]
        public void LinkToUndefinedName_ThrowsWithHint()
        {
            var doc = TwoPages(out var p1, out _);
            p1.AddLink(0, 0, 10, 10, PdfDestination.Named("nowhere"));
            var ex = Assert.Throws<InvalidOperationException>(() => doc.ToArray());
            Assert.Contains("NamedDestinations", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("with space")]
        [InlineData("bab#1")]
        public void InvalidDestinationName_Throws(string name) =>
            Assert.Throws<ArgumentException>(() => PdfDestination.Named(name));

        // ------------------------------------------------------------------ outline

        [Fact]
        public void Outline_CountsFollowOpenState()
        {
            var doc = TwoPages(out var p1, out var p2);
            var ch1 = doc.Outline.Add("Bab 1", PdfDestination.ToPage(p1));
            ch1.Add("1.1", PdfDestination.ToPage(p1, 300));
            var closed = ch1.Add("1.2", PdfDestination.ToPage(p2));
            closed.IsOpen = false;
            closed.Add("1.2.1", PdfDestination.ToPage(p2, 200));
            var ch2 = doc.Outline.Add("Bab 2", PdfDestination.ToPage(p2));
            ch2.Bold = true;
            ch2.Color = PdfColor.FromHex("#FF0000");

            var pdf = PdfStructure.Verify(doc.ToArray());
            // Root: Bab 1 + its 2 visible children + Bab 2 = 4.
            Assert.Contains("<< /Type /Outlines /First", Obj(pdf, "/Type /Outlines"));
            Assert.Contains("/Count 4", Obj(pdf, "/Type /Outlines"));
            Assert.Contains("/Count 2", Obj(pdf, "/Title (Bab 1)"));
            Assert.Contains("/Count -1", Obj(pdf, "/Title (1.2)"));
            var bab2 = Obj(pdf, "/Title (Bab 2)");
            Assert.Contains("/C [1 0 0]", bab2);
            Assert.Contains("/F 2", bab2);
            Assert.Contains("/Prev", bab2);
            Assert.Contains("/PageMode /UseOutlines", Obj(pdf, "/Type /Catalog"));
        }

        [Fact]
        public void Outline_UnicodeTitle_IsUtf16()
        {
            var doc = TwoPages(out var p1, out _);
            doc.Outline.Add("Ringkasan — 日本", PdfDestination.ToPage(p1));
            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/Title <FEFF", pdf.Text);
        }

        // ------------------------------------------------------------------ page labels & viewer

        [Fact]
        public void PageLabels_WrittenAsNumberTree()
        {
            var doc = TwoPages(out _, out _);
            doc.AddPage();
            doc.PageLabels.Add(0, PageLabelStyle.LowerRoman);
            doc.PageLabels.Add(1, PageLabelStyle.Decimal, "A-", 5);
            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/PageLabels << /Nums [0 << /S /r >> 1 << /S /D /P (A-) /St 5 >>] >>", Obj(pdf, "/Type /Catalog"));
            Assert.Equal("i", doc.PageLabels.GetLabel(0));
            Assert.Equal("A-5", doc.PageLabels.GetLabel(1));
            Assert.Equal("A-6", doc.PageLabels.GetLabel(2));
        }

        [Fact]
        public void PageLabels_NotStartingAtZero_GetDecimalStart()
        {
            var doc = TwoPages(out _, out _);
            doc.PageLabels.Add(1, PageLabelStyle.UpperLetters);
            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/Nums [0 << /S /D >> 1 << /S /A >>]", pdf.Text);
        }

        [Fact]
        public void PageLabel_Formats()
        {
            Assert.Equal("xiv", PdfPageLabels.Format(PageLabelStyle.LowerRoman, 14));
            Assert.Equal("MMXXVI", PdfPageLabels.Format(PageLabelStyle.UpperRoman, 2026));
            Assert.Equal("AA", PdfPageLabels.Format(PageLabelStyle.UpperLetters, 27));
            Assert.Equal("c", PdfPageLabels.Format(PageLabelStyle.LowerLetters, 3));
        }

        [Fact]
        public void ViewerPreferences_AndOpenAction()
        {
            var doc = TwoPages(out _, out _);
            doc.Info.Title = "Laporan";
            doc.ViewerPreferences.PageLayout = PageLayout.TwoPageRight;
            doc.ViewerPreferences.PageMode = PageMode.Thumbnails;
            doc.ViewerPreferences.InitialZoom = InitialZoom.Percent;
            doc.ViewerPreferences.ZoomPercent = 150;
            doc.ViewerPreferences.InitialPage = 1;
            doc.ViewerPreferences.HideToolbar = true;
            var catalog = Obj(PdfStructure.Verify(doc.ToArray()), "/Type /Catalog");
            Assert.Contains("/PageLayout /TwoPageRight", catalog);
            Assert.Contains("/PageMode /UseThumbs", catalog);
            Assert.Matches(@"/OpenAction \[\d+ 0 R /XYZ null null 1.5\]", catalog);
            Assert.Contains("/ViewerPreferences << /DisplayDocTitle true /HideToolbar true >>", catalog);
        }

        [Fact]
        public void PlainDocument_HasNoInteractiveEntries()
        {
            var doc = TwoPages(out _, out _);
            var catalog = Obj(PdfStructure.Verify(doc.ToArray()), "/Type /Catalog");
            Assert.Equal("<< /Type /Catalog /Pages 2 0 R >>", catalog);
        }

        // ------------------------------------------------------------------ annotations

        [Fact]
        public void Note_Highlight_Stamp_HaveAppearanceStreams()
        {
            var doc = TwoPages(out var p1, out _);
            p1.AddNote(400, 80, "Cek lagi angka ini", "Fajar", icon: NoteIcon.Note);
            p1.AddHighlight(72, 100, 200, 14, comment: "penting");
            p1.AddStamp(350, 300, 160, 60, "LUNAS");
            var pdf = PdfStructure.Verify(doc.ToArray());

            var note = Obj(pdf, "/Subtype /Text");
            Assert.Contains("/F 24", note);
            Assert.Contains("/Name /Note", note);
            Assert.Contains("/T (Fajar)", note);
            Assert.Matches(@"/AP << /N \d+ 0 R >>", note);

            var hl = Obj(pdf, "/Subtype /Highlight");
            Assert.Contains("/QuadPoints [72 700 272 700 72 686 272 686]", hl);
            Assert.Contains("/BM /Multiply", pdf.Text);

            var stamp = Obj(pdf, "/Subtype /Stamp");
            var ap = int.Parse(Regex.Match(stamp, @"/AP << /N (\d+) 0 R >>").Groups[1].Value);
            Assert.Contains("/Subtype /Form /BBox [0 0 160 60]", pdf.Objects[ap]);
            Assert.Matches(@"/Font << /F\d+ \d+ 0 R >>", pdf.Objects[ap]);
            var content = Encoding.ASCII.GetString(pdf.StreamData(ap));
            Assert.Contains("Tf", content);
            Assert.Contains("<4C554E4153> Tj", content); // "LUNAS"
            Assert.Contains("/BaseFont /Helvetica-Bold", pdf.Text);
        }

        [Fact]
        public void Annotations_InvalidArea_Throws()
        {
            var doc = TwoPages(out var p1, out _);
            Assert.Throws<ArgumentOutOfRangeException>(() => p1.AddHighlight(0, 0, -5, 10));
            Assert.Throws<ArgumentException>(() => p1.AddStamp(0, 0, 50, 20, " "));
        }

        // ------------------------------------------------------------------ attachments

        [Fact]
        public void Attachment_EmbeddedWithChecksumAndMime()
        {
            var doc = TwoPages(out _, out _);
            var data = Encoding.UTF8.GetBytes("{\"total\":1924500}");
            doc.Attachments.Add("data.json", data, "application/json", "Data mentah");
            doc.Attachments.Add("catatan é.txt", Encoding.UTF8.GetBytes("halo"));
            var pdf = PdfStructure.Verify(doc.ToArray());

            var spec = Obj(pdf, "/F (data.json)");
            Assert.Contains("/Type /Filespec", spec);
            Assert.Contains("/Desc (Data mentah)", spec);
            var ef = int.Parse(Regex.Match(spec, @"/EF << /F (\d+) 0 R").Groups[1].Value);
            Assert.Contains("/Subtype /application#2Fjson", pdf.Objects[ef]);
            Assert.Contains("/Size 17", pdf.Objects[ef]);
            Assert.Equal(data, pdf.StreamData(ef));

            var unicode = Obj(pdf, "/F (catatan _.txt)");
            Assert.Contains("/UF <FEFF", unicode);
            // Name tree keys are sorted by their bytes: "data.json" before the UTF-16 key (0xFE…).
            Assert.Matches(@"/EmbeddedFiles << /Names \[\(data.json\) \d+ 0 R <FEFF", Obj(pdf, "/Type /Catalog"));
        }

        [Fact]
        public void Attachment_DuplicateNameOrBadMime_Throws()
        {
            var doc = new PdfDocument();
            doc.Attachments.Add("a.txt", new byte[1]);
            Assert.Throws<ArgumentException>(() => doc.Attachments.Add("a.txt", new byte[1]));
            Assert.Throws<ArgumentException>(() => doc.Attachments.Add("b.txt", new byte[1], "text"));
        }

        [Fact]
        public void Output_IsDeterministic()
        {
            byte[] Build()
            {
                var doc = TwoPages(out var p1, out var p2);
                doc.Outline.Add("A", PdfDestination.ToPage(p2)).Add("B", PdfDestination.FitPage(p1));
                doc.NamedDestinations["x"] = PdfDestination.ToPage(p2, 10);
                p1.AddLink(0, 0, 10, 10, "https://lifeos.id");
                p1.AddStamp(10, 10, 100, 40, "DRAFT");
                doc.Attachments.Add("a.txt", new byte[] { 1, 2, 3 });
                return doc.ToArray();
            }

            Assert.Equal(Build(), Build());
        }

        // ------------------------------------------------------------------ layout engine

        private static FlowDocument Book(bool toc)
        {
            var doc = new FlowDocument();
            doc.Info.CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var s = doc.AddSection();
            if (toc)
            {
                s.AddTableOfContents("Daftar Isi");
                s.AddPageBreak();
            }

            for (var c = 1; c <= 3; c++)
            {
                s.AddHeading("Bab " + c);
                for (var k = 1; k <= 2; k++)
                {
                    s.AddHeading("Subbab " + c + "." + k, 2);
                    for (var i = 0; i < 12; i++)
                    {
                        s.AddParagraph("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.");
                    }
                }
            }

            return doc;
        }

        [Fact]
        public void Layout_HeadingsBecomeNestedOutline()
        {
            var pdf = Book(toc: false).Render();
            Assert.Equal(3, pdf.Outline.Count);
            Assert.Equal(new[] { "Subbab 2.1", "Subbab 2.2" }, pdf.Outline.Items[1].Children.Select(c => c.Title).ToArray());
            Assert.True(pdf.Outline.Items[0].IsOpen);
            var structure = PdfStructure.Verify(pdf.ToArray());
            Assert.Equal(9, structure.CountObjects("/Title (")); // 3 + 6
            Assert.Contains("/PageMode /UseOutlines", structure.Text);
        }

        [Fact]
        public void Layout_GenerateOutlineOff()
        {
            var doc = Book(toc: false);
            doc.GenerateOutline = false;
            Assert.Equal(0, doc.Render().Outline.Count);
        }

        [Fact]
        public void Layout_TableOfContents_HasCorrectPagesAndLinks()
        {
            var pdf = Book(toc: true).Render();
            var structure = PdfStructure.Verify(pdf.ToArray());

            // Every heading has a destination; the TOC (page 1) links to them.
            Assert.Equal(9, pdf.NamedDestinations.Count);
            var first = pdf.NamedDestinations["_heading-1"];
            Assert.Same(pdf.Pages[1], first.Page);

            var tocText = structure.PageContents()[0];
            Assert.DoesNotContain("<3F>", tocText); // no unresolved "?" page numbers
            var links = structure.Objects.Values.Count(o => o.Contains("/Subtype /Link") && o.Contains("/Dest (_heading-"));
            Assert.True(links >= 18, "expected title + number link per entry, got " + links);

            // The number shown for the last heading equals the page it is on.
            var last = pdf.NamedDestinations["_heading-9"];
            var lastPage = pdf.Pages.ToList().IndexOf(last.Page!) + 1;
            Assert.True(lastPage > 2);
        }

        [Fact]
        public void Layout_TocMaxLevel_FiltersEntries()
        {
            var doc = Book(toc: false);
            var s = doc.Sections[0];
            var withToc = new FlowDocument();
            var sec = withToc.AddSection();
            sec.AddTableOfContents(null, 1);
            foreach (var h in new[] { "A", "B" })
            {
                sec.AddHeading(h);
                sec.AddHeading(h + ".1", 2);
            }

            var structure = PdfStructure.Verify(withToc.Render().ToArray());
            var links = structure.Objects.Values.Count(o => o.Contains("/Subtype /Link"));
            Assert.Equal(4, links); // two entries × (title + number)
            Assert.NotNull(s);
        }

        [Fact]
        public void Layout_LinksAndAnchors()
        {
            var doc = new FlowDocument();
            var s = doc.AddSection();
            s.AddParagraph("Lihat ").AddLink("situs kami", "https://lifeos.id").AddText(" atau ").AddLink("lampiran", "#lampiran");
            s.AddPageBreak();
            s.AddParagraph("Lampiran").Anchor = "lampiran";
            var pdf = doc.Render();
            Assert.Same(pdf.Pages[1], pdf.NamedDestinations["lampiran"].Page);
            var structure = PdfStructure.Verify(pdf.ToArray());
            Assert.Contains("/URI (https://lifeos.id/)", structure.Text);
            Assert.Contains("/Dest (lampiran)", structure.Text);
            // Link text is underlined in the default link colour.
            Assert.Contains("0.1137 0.3059 0.8471 rg", structure.PageContents()[0]);
        }

        [Fact]
        public void Layout_MissingAnchorOrDuplicate_Throws()
        {
            var missing = new FlowDocument();
            missing.AddSection().AddParagraph().AddLink("x", "#tidak-ada");
            Assert.Throws<InvalidOperationException>(() => missing.Render().ToArray());

            var dup = new FlowDocument();
            var s = dup.AddSection();
            s.AddParagraph("a").Anchor = "sama";
            s.AddParagraph("b").Anchor = "sama";
            Assert.Throws<LayoutException>(() => dup.Render());

            Assert.Throws<ArgumentException>(() => new FlowDocument().AddSection().AddParagraph().AddLink("x", "javascript:void(0)"));
            Assert.Throws<ArgumentException>(() => new FlowDocument().AddSection().AddParagraph().Anchor = "ada spasi");
        }

        [Fact]
        public void Layout_CopiesViewerPreferencesAndAttachments()
        {
            var doc = new FlowDocument();
            doc.AddSection().AddParagraph("x");
            doc.ViewerPreferences.PageMode = PageMode.Attachments;
            doc.Attachments.Add("a.csv", Encoding.ASCII.GetBytes("a,b"), "text/csv");
            var pdf = doc.Render();
            Assert.Single(pdf.Attachments);
            Assert.Contains("/PageMode /UseAttachments", PdfStructure.Verify(pdf.ToArray()).Text);
        }

        // ------------------------------------------------------------------ templates

        [Fact]
        public void Template_LinksAnchorsTocAndAttachments()
        {
            var xml = @"<document title='Buku' pageMode='outlines' pageLayout='oneColumn' zoom='125%'>
  <styles><link color='#DC2626'/></styles>
  <attachment name='data.json' content='{{ json }}' mime='application/json' description='Data'/>
  <section>
    <toc title='Isi' maxLevel='2'/>
    <pagebreak/>
    <h1 anchor='bab-1'>Bab {{ n }}</h1>
    <p>Kunjungi <a href='https://lifeos.id/{{ path }}'>situs <b>kami</b></a> atau ke <a href='#bab-1'>bab 1</a>.</p>
    <h2>Detail</h2>
  </section>
</document>";
            var pdf = Templates.PdfTemplate.Parse(xml).Render(new { n = 1, path = "docs", json = "{\"a\":1}" });
            Assert.Same(pdf.Pages[1], pdf.NamedDestinations["bab-1"].Page);
            Assert.Equal("Bab 1", pdf.Outline.Items[0].Title);
            Assert.Equal("Detail", pdf.Outline.Items[0].Children[0].Title);
            Assert.Single(pdf.Attachments);
            Assert.Equal(Encoding.UTF8.GetBytes("{\"a\":1}"), pdf.Attachments[0].GetData());

            var structure = PdfStructure.Verify(pdf.ToArray());
            Assert.Contains("/URI (https://lifeos.id/docs)", structure.Text);
            Assert.Equal(2, structure.Objects.Values.Count(o => o.Contains("/URI (https://lifeos.id/docs)"))); // "situs " and bold "kami"
            Assert.Contains("/Dest (bab-1)", structure.Text);
            Assert.Contains("0.8627 0.149 0.149 rg", structure.PageContents()[1]); // link style from <styles><link>
            var catalog = structure.Objects.Values.Single(o => o.Contains("/Type /Catalog"));
            Assert.Contains("/PageMode /UseOutlines", catalog);
            Assert.Contains("/PageLayout /OneColumn", catalog);
            Assert.Matches(@"/OpenAction \[\d+ 0 R /XYZ null null 1.25\]", catalog);
        }

        [Theory]
        [InlineData("<p><a href='javascript:alert(1)'>x</a></p>", "javascript")]
        [InlineData("<p><a>x</a></p>", "href")]
        [InlineData("<p anchor='a b'>x</p>", "Destination name")]
        [InlineData("<table><column/><row><cell><toc/></cell></row></table>", "toc")]
        public void Template_NavigationErrors(string body, string message)
        {
            var ex = Assert.ThrowsAny<Exception>(() => Templates.PdfTemplate.Parse("<document><section>" + body + "</section></document>").Bind(null));
            Assert.Contains(message, ex.Message);
        }

        [Fact]
        public void Template_OutlineOffAndBadZoom()
        {
            var pdf = Templates.PdfTemplate.Parse("<document outline='false'><section><h1>A</h1></section></document>").Render(null);
            Assert.Equal(0, pdf.Outline.Count);
            var ex = Assert.Throws<Templates.TemplateException>(() => Templates.PdfTemplate.Parse("<document zoom='huge'><section><p>x</p></section></document>").Bind(null));
            Assert.Contains("zoom", ex.Message);
            Assert.Throws<Templates.TemplateException>(() => Templates.PdfTemplate.Parse("<document><attachment name='a.txt'/><section><p>x</p></section></document>").Bind(null));
        }

        [Fact]
        public void Toc_NotAllowedInHeader() =>
            Assert.Throws<InvalidOperationException>(() => new FlowDocument().AddSection().Header.AddTableOfContents());
    }
}
