using System;
using System.Globalization;
using System.IO;
using System.Threading;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class DocumentTests
    {
        private static readonly DateTimeOffset FixedDate = new DateTimeOffset(2026, 11, 13, 10, 15, 0, TimeSpan.FromHours(7));

        [Fact]
        public void EmptyPage_ProducesStructurallyValidFile()
        {
            var doc = new PdfDocument();
            doc.AddPage();

            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Equal(1, pdf.CountObjects("/Type /Catalog"));
            Assert.Equal(1, pdf.CountObjects("/Type /Pages /Kids [3 0 R] /Count 1"));
            Assert.Contains("/MediaBox [0 0 595.2756 841.8898]", pdf.Text);
        }

        [Fact]
        public void Save_WithoutPages_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new PdfDocument().ToArray());
            Assert.Contains("AddPage", ex.Message);
        }

        [Fact]
        public void SameInput_ProducesIdenticalBytes()
        {
            byte[] Build()
            {
                var doc = new PdfDocument();
                doc.Info.Title = "Laporan";
                doc.Info.CreationDate = FixedDate;
                doc.AddPage().Graphics.DrawText("Halo", new PdfFont(StandardFont.Helvetica, 12), 72, 72);
                return doc.ToArray();
            }

            Assert.Equal(Build(), Build());
        }

        [Fact]
        public void MultiplePages_AreAllInPageTree()
        {
            var doc = new PdfDocument();
            doc.AddPage(PageSize.A4);
            doc.AddPage(PageSize.Letter);
            doc.AddPage(PageSize.A5.Landscape);

            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Contains("/Count 3", pdf.Text);
            Assert.Contains("/MediaBox [0 0 612 792]", pdf.Text);
            Assert.Contains("/MediaBox [0 0 595.2756 419.5276]", pdf.Text);
        }

        [Fact]
        public void Metadata_AsciiIsLiteral_UnicodeIsUtf16()
        {
            var doc = new PdfDocument();
            doc.Info.Title = "Laporan Keuangan";
            doc.Info.Author = "Fajar — Jakarta";
            doc.Info.CreationDate = FixedDate;
            doc.AddPage();

            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Contains("/Title (Laporan Keuangan)", pdf.Text);
            // UTF-16BE with BOM: FEFF 0046 0061 ... 2014 ...
            Assert.Contains("/Author <FEFF00460061006A00610072002020140020004A0061006B0061007200740061>", pdf.Text);
            Assert.Contains("/CreationDate (D:20261113101500+07'00')", pdf.Text);
            Assert.Contains("/DisplayDocTitle true", pdf.Text);
            Assert.Contains("/Producer (LifeOS.Pdf ", pdf.Text);
        }

        [Fact]
        public void Compression_CanBeTurnedOff()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.FillRectangle(PdfColor.Red, 10, 10, 50, 50);

            var compressed = PdfStructure.Verify(doc.ToArray());
            var plain = PdfStructure.Verify(doc.ToArray(new PdfSaveOptions { CompressContent = false }));

            Assert.Contains("/Filter /FlateDecode", compressed.Text);
            Assert.DoesNotContain("/Filter /FlateDecode", plain.Text);
            Assert.Equal(compressed.PageContents()[0], plain.PageContents()[0]);
            Assert.Contains(" re f\n", plain.Text);
        }

        [Fact]
        public void Output_DoesNotDependOnCurrentCulture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // id-ID and de-DE use ',' as decimal separator — must never leak into the PDF.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("id-ID");
                var doc = new PdfDocument();
                doc.AddPage().Graphics.DrawLine(new PdfPen(PdfColor.Black, 0.5), 10.25, 20.5, 30.75, 40.125);
                var content = PdfStructure.Verify(doc.ToArray()).PageContents()[0];

                Assert.Contains("0.5 w", content);
                Assert.Contains("10.25 821.3898 m", content);
                Assert.DoesNotContain(",", content);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Fact]
        public void SaveToFileAndStream_WriteSameBytes()
        {
            var doc = new PdfDocument();
            doc.Info.CreationDate = FixedDate;
            doc.AddPage();

            var path = Path.Combine(Path.GetTempPath(), "lifeos-pdf-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                doc.Save(path);
                using (var ms = new MemoryStream())
                {
                    doc.Save(ms);
                    Assert.Equal(File.ReadAllBytes(path), ms.ToArray());
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void DefaultPageSize_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new PdfDocument().AddPage(default(PageSize)));
        }
    }
}
