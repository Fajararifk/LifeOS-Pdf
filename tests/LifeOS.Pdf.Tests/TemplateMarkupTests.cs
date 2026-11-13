using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Templates;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>Template XML → FlowDocument, control flow, security and the built-in templates.</summary>
    public class TemplateMarkupTests
    {
        private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

        private static FlowDocument Bind(string body, object? data = null, TemplateOptions? options = null) =>
            PdfTemplate.Parse("<document><section>" + body + "</section></document>", options).Bind(data);

        private static Section Body(string body, object? data = null, TemplateOptions? options = null) => Bind(body, data, options).Sections[0];

        private static string Texts(Paragraph p) => string.Concat(p.Inlines.Select(i => i.Kind == InlineKind.LineBreak ? "\n" : i.Text));

        private static Paragraph[] Paragraphs(BlockContainer c) => c.Blocks.OfType<Paragraph>().ToArray();

        // ------------------------------------------------------------------ structure

        [Fact]
        public void Elements_MapToFlowDocument()
        {
            var doc = PdfTemplate.Parse(@"
<document title='Laporan {{ Name }}' author='LifeOS'>
  <section size='A5' orientation='landscape' margin='10mm' columns='2'>
    <header><p>Kepala</p></header>
    <footer><p>Hal <pageNumber/>/<pageCount/></p></footer>
    <h1>Judul</h1>
    <p align='justify' spaceAfter='0'>Isi</p>
    <list kind='numbered'><item>Satu</item><item>Dua<list><item>Anak</item></list></item></list>
    <hr/><spacer height='20'/><pagebreak/>
    <table headerRows='1'><column width='auto'/><column width='2*'/><column width='30%'/><column width='25mm'/>
      <row><cell>a</cell><cell>b</cell><cell>c</cell><cell>d</cell></row>
    </table>
  </section>
</document>").Bind(new { Name = "Nov" });

            Assert.Equal("Laporan Nov", doc.Info.Title);
            var s = doc.Sections[0];
            Assert.True(s.PageSetup.PageSize.Width > s.PageSetup.PageSize.Height);
            Assert.Equal(2, s.PageSetup.Columns);
            Assert.Equal(10 * 72 / 25.4, s.PageSetup.Margins.Left, 6);
            Assert.False(s.Header.IsEmpty);
            Assert.Equal(new[] { InlineKind.Text, InlineKind.PageNumber, InlineKind.Text, InlineKind.PageCount }, Paragraphs(s.Footer)[0].Inlines.Select(i => i.Kind));
            Assert.Equal(1, Paragraphs(s)[0].HeadingLevel);
            Assert.Equal(ParagraphAlignment.Justify, Paragraphs(s)[1].Format.Alignment);
            var list = s.Blocks.OfType<ListBlock>().Single();
            Assert.Equal(ListKind.Numbered, list.Kind);
            Assert.Single(list.Items[1].Children);
            Assert.Single(s.Blocks.OfType<PageBreakBlock>());
            var table = s.Blocks.OfType<Table>().Single();
            Assert.Equal(new[] { ColumnWidthKind.Auto, ColumnWidthKind.Relative, ColumnWidthKind.Percent, ColumnWidthKind.Fixed }, table.Columns.Select(c => c.Width.Kind));
            Assert.Equal(25 * 72 / 25.4, table.Columns[3].Width.Value, 6);
        }

        [Fact]
        public void Whitespace_IsCollapsedLikeHtml_ButDataKeepsItsLineBreaks()
        {
            var p = Paragraphs(Body("<p>\n   Halo    <b>dunia</b>   !  {{ Address }}  </p>", new { Address = "Jl. A\nJakarta" }))[0];

            Assert.Equal(new[] { "Halo ", "dunia", " ! ", "Jl. A\nJakarta" }, p.Inlines.Select(i => i.Text).ToArray());
            Assert.True(p.Inlines[1].Style!.Bold);
        }

        [Fact]
        public void InlineStyles_Nest()
        {
            var p = Paragraphs(Body("<p><span color='#FF0000' size='14'>merah <i>miring <u>garis</u></i></span></p>"))[0];
            var last = p.Inlines.Last().Style!;

            Assert.Equal(14, last.Size);
            Assert.True(last.Italic);
            Assert.True(last.Underline);
            Assert.Equal(255, last.Color!.Value.R);
        }

        [Fact]
        public void LooseTextInSectionAndCells_BecomesParagraphs()
        {
            var s = Body("Teks bebas <b>tebal</b><table><column/><row><cell>sel</cell></row></table>");
            Assert.Equal("Teks bebas tebal", Texts(Paragraphs(s)[0]));
            var cell = s.Blocks.OfType<Table>().Single().Rows[0].Cells[0];
            Assert.Equal(0, Paragraphs(cell)[0].Format.SpaceAfter); // compact cells
        }

        // ------------------------------------------------------------------ control flow

        [Fact]
        public void Foreach_WithIndexAndNesting()
        {
            var data = TemplateData.FromJson("{\"Groups\":[{\"Name\":\"A\",\"Items\":[1,2]},{\"Name\":\"B\",\"Items\":[3]}]}");
            var s = Body("<foreach in='Groups' as='g' index='i'><foreach in='g.Items' as='x'><p>{{ i + 1 }}{{ g.Name }}{{ x }}</p></foreach></foreach>", data);

            Assert.Equal(new[] { "1A1", "1A2", "2B3" }, Paragraphs(s).Select(Texts).ToArray());
        }

        [Fact]
        public void If_Else_AndIfAttribute()
        {
            var s = Body(
                "<if test='Total > 100'><p>besar</p></if><else><p>kecil</p></else>" +
                "<p if='Show'>tampil</p><else><p>sembunyi</p></else>" +
                "<p if='!Show'>tidak</p>", new { Total = 50, Show = true });

            Assert.Equal(new[] { "kecil", "tampil" }, Paragraphs(s).Select(Texts).ToArray());
        }

        [Fact]
        public void Set_DefinesVariablesForFollowingSiblings()
        {
            var s = Body("<set name='Subtotal' value=\"Items | sumproduct:'Q':'P'\"/><set name='Tax' value='Subtotal * 0.11'/><p>{{ Subtotal | rupiah }} {{ Tax | rupiah }}</p>",
                TemplateData.FromJson("{\"Items\":[{\"Q\":2,\"P\":50000},{\"Q\":1,\"P\":100000}]}"));

            Assert.Equal("Rp200.000 Rp22.000", Texts(Paragraphs(s)[0]));
        }

        [Fact]
        public void InlineForeachAndIf_InsideParagraph()
        {
            var p = Paragraphs(Body("<p>Tag: <foreach in='Tags' as='t' index='i'><if test='i > 0'>, </if>{{ t }}</foreach>.</p>", new { Tags = new[] { "a", "b", "c" } }))[0];
            Assert.Equal("Tag: a, b, c.", Texts(p));
        }

        // ------------------------------------------------------------------ tables

        [Fact]
        public void Table_RowsFromDataWithSpansAndAlignment()
        {
            var s = Body(@"<table headerRows='1' zebra='#EEEEEE'>
                <column width='auto'/><column width='*'/><column width='90' align='right'/>
                <row><cell rowspan='2'>Grup</cell><cell>a</cell><cell>1</cell></row>
                <row><cell>b</cell><cell>2</cell></row>
                <foreach in='Rows' as='r'><row><cell colspan='2'>{{ r.Name }}</cell><cell>{{ r.Amount | rupiah }}</cell></row></foreach>
              </table>", new { Rows = new[] { new { Name = "x", Amount = 1000 } } });

            var t = s.Blocks.OfType<Table>().Single();
            Assert.Equal(3, t.Rows.Count);
            Assert.Equal(2, t.Rows[0].Cells[0].RowSpan);
            Assert.Equal("b", Texts(Paragraphs(t.Rows[1].Cells[1])[0]));          // placed after the row-span
            Assert.Equal(2, t.Rows[2].Cells[0].ColSpan);
            Assert.Equal("Rp1.000", Texts(Paragraphs(t.Rows[2].Cells[2])[0]));
            Assert.Equal(ParagraphAlignment.Right, Paragraphs(t.Rows[2].Cells[2])[0].Format.Alignment);
            Assert.NotNull(t.AlternateRowBackground);

            var pdf = PdfTemplate.Parse("<document><section><table><column/><row><cell>a</cell></row></table></section></document>").Render(null);
            PdfStructure.Verify(pdf.ToArray());
        }

        [Fact]
        public void Table_TooManyCells_IsReported()
        {
            var ex = Assert.Throws<TemplateException>(() => Body("<table><column/><row><cell>a</cell>\n<cell>b</cell></row></table>"));
            Assert.Contains("more cells", ex.Message);
            Assert.Equal(2, ex.Line);
        }

        // ------------------------------------------------------------------ errors

        [Theory]
        [InlineData("<paragraph>x</paragraph>", "Unknown element <paragraph>")]
        [InlineData("<p allign='right'>x</p>", "Unknown attribute 'allign'")]
        [InlineData("<else><p>x</p></else>", "<else> must directly follow")]
        [InlineData("<table><p>x</p></table>", "<p> is not allowed inside <table>")]
        [InlineData("<p>{{ Total | rupiah </p>", "not closed")]
        [InlineData("<foreach as='x'><p/></foreach>", "needs an in=")]
        [InlineData("<p size='besar'>x</p>", "not a valid length")]
        [InlineData("<p family='Arial'>x</p>", "Unknown font family 'Arial'")]
        [InlineData("<section/>", "not allowed inside <section>")]
        public void TemplateErrors_AreDescriptive(string body, string message)
        {
            var ex = Assert.Throws<TemplateException>(() => Body(body));
            Assert.Contains(message, ex.Message);
            Assert.True(ex.Line > 0);
        }

        [Fact]
        public void Dtd_IsRejected()
        {
            const string xxe = "<?xml version='1.0'?><!DOCTYPE d [<!ENTITY x SYSTEM 'file:///c:/windows/win.ini'>]><document><section><p>&x;</p></section></document>";
            Assert.Contains("not valid XML", Assert.Throws<TemplateException>(() => PdfTemplate.Parse(xxe)).Message);
        }

        [Fact]
        public void FilePaths_AreConfinedToTheTemplateFolder()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "TestAssets");
            var options = new TemplateOptions { BaseDirectory = folder };

            Assert.Single(Body("<image src='rgb.jpg'/>", null, options).Blocks.OfType<ImageBlock>());
            Assert.Contains("leaves the template folder", Assert.Throws<TemplateException>(() => Body("<image src='../LifeOS.Pdf.dll'/>", null, options)).Message);
            Assert.Contains("Absolute path", Assert.Throws<TemplateException>(() => Body("<image src='" + Asset("rgb.jpg") + "'/>", null, options)).Message);
            Assert.Contains("no base directory", Assert.Throws<TemplateException>(() => Body("<image src='rgb.jpg'/>")).Message);

            var allowed = new TemplateOptions { AllowFileAccessOutsideBaseDirectory = true };
            Assert.Single(Body("<image src='" + Asset("rgb.jpg") + "'/>", null, allowed).Blocks.OfType<ImageBlock>());
        }

        [Fact]
        public void Images_FromDataBytesDataUriAndNamedImages()
        {
            var bytes = File.ReadAllBytes(Asset("rgb.jpg"));
            var options = new TemplateOptions();
            options.Images["logo"] = PdfImage.FromBytes(bytes);

            var s = Body("<image src='{{ Bytes }}'/><image src='{{ Uri }}'/><image src='logo'/><image src='{{ Missing }}'/>",
                new { Bytes = bytes, Uri = "data:image/jpeg;base64," + Convert.ToBase64String(bytes) }, options);

            Assert.Equal(3, s.Blocks.OfType<ImageBlock>().Count()); // empty src = optional image, skipped
        }

        [Fact]
        public void FontsDeclaredInTemplate_AndFallbackFontsFromOptions()
        {
            var options = new TemplateOptions { BaseDirectory = Path.Combine(AppContext.BaseDirectory, "TestAssets") };
            var noto = FontFamily.FromFiles(Asset(Path.Combine("fonts", "NotoSans-subset.ttf")));
            options.FallbackFonts.Add(noto);
            var doc = PdfTemplate.Parse(@"<document><styles><font name='noto' regular='fonts/NotoSans-subset.ttf'/><normal family='noto' size='11'/></styles>
                <section><p>Tiếng Việt</p></section></document>", options).Bind(null);

            Assert.Equal("Noto Sans", doc.Styles.Normal.Family!.Name);
            Assert.Equal(11, doc.Styles.Normal.Size);
            Assert.Contains(noto, doc.Styles.Normal.Fallbacks);
            PdfStructure.Verify(doc.Render().ToArray());
        }

        [Fact]
        public void TemplatesAreReusable()
        {
            var t = PdfTemplate.Parse("<document><section><p>{{ N }}</p></section></document>");
            Assert.Equal("1", Texts(Paragraphs(t.Bind(new { N = 1 }).Sections[0])[0]));
            Assert.Equal("2", Texts(Paragraphs(t.Bind(new { N = 2 }).Sections[0])[0]));
        }

        // ------------------------------------------------------------------ built-in templates

        public static TheoryData<BuiltInTemplate, string, string[]> BuiltIns => new TheoryData<BuiltInTemplate, string, string[]>
        {
            { BuiltInTemplate.Invoice, "invoice", new[] { "INVOICE", "Rp24.031.500", "dua puluh empat juta tiga puluh satu ribu lima ratus rupiah" } },
            { BuiltInTemplate.Receipt, "receipt", new[] { "KWITANSI", "Rp2.750.000", "Dua Juta Tujuh Ratus Lima Puluh Ribu Rupiah" } },
            { BuiltInTemplate.Payslip, "payslip", new[] { "SLIP GAJI", "November 2026", "Rp20.414.577" } },
            { BuiltInTemplate.MonthlyReport, "monthly-report", new[] { "Laporan Keuangan November 2026", "Rp24.760.577" } },
            { BuiltInTemplate.Certificate, "certificate", new[] { "SERTIFIKAT", "Fajar Arif", ".NET Conf Indonesia 2026" } },
        };

        [Theory]
        [MemberData(nameof(BuiltIns))]
        public void BuiltInTemplates_RenderWithSampleData(BuiltInTemplate template, string dataFile, string[] expected)
        {
            var data = TemplateData.FromJson(File.ReadAllText(Asset(Path.Combine("data", dataFile + ".json"))));
            var options = new TemplateOptions();
            options.Images["logo"] = PdfImage.FromFile(Asset("rgb.jpg"));

            var pdf = PdfStructure.Verify(BuiltInTemplates.Get(template, options).Render(data).ToArray());
            var content = string.Join("\n", pdf.PageContents());
            foreach (var text in expected)
            {
                // Text may share a run with neighbouring words, so search the hex without its delimiters.
                Assert.Contains(string.Concat(WinAnsiEncoding.Encode(text).Select(b => b.ToString("X2"))), content);
            }
        }

        [Fact]
        public void BuiltInTemplate_SourceIsAvailable()
        {
            Assert.Contains("<document", BuiltInTemplates.GetSource(BuiltInTemplate.Invoice));
        }

        [Fact]
        public void Invoice_With500Items_IsFast()
        {
            var items = string.Join(",", Enumerable.Range(1, 500).Select(i => "{\"Name\":\"Barang " + i + "\",\"Quantity\":" + (i % 5 + 1) + ",\"Unit\":\"pcs\",\"Price\":" + i * 1000 + "}"));
            var data = TemplateData.FromJson("{\"Number\":\"INV-1\",\"Date\":\"2026-11-13\",\"Seller\":{\"Name\":\"S\"},\"Customer\":{\"Name\":\"C\"},\"Items\":[" + items + "]}");
            var template = BuiltInTemplates.Invoice;
            template.Render(data).ToArray(); // warm-up

            var sw = Stopwatch.StartNew();
            var pdf = template.Render(data);
            pdf.ToArray();
            sw.Stop();

            Assert.True(pdf.Pages.Count > 10);
            Assert.True(sw.ElapsedMilliseconds < 3000, "took " + sw.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void SameData_GivesIdenticalPdf()
        {
            var data = TemplateData.FromJson(File.ReadAllText(Asset(Path.Combine("data", "payslip.json"))));
            var options = new TemplateOptions();
            options.Images["logo"] = PdfImage.FromFile(Asset("rgb.jpg"));
            byte[] Build()
            {
                var doc = BuiltInTemplates.Get(BuiltInTemplate.Payslip, options).Bind(data);
                doc.Info.CreationDate = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
                return doc.Render().ToArray();
            }

            Assert.Equal(Build(), Build());
        }
    }
}
