using System;
using System.IO;
using LifeOS.Pdf.Conversion;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class TransformAndConversionTests
    {
        private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

        private static string Draw(Action<PdfGraphics> draw, double width = 200, double height = 100)
        {
            var doc = new PdfDocument();
            draw(doc.AddPage(new PageSize(width, height)).Graphics);
            return PdfStructure.Verify(doc.ToArray()).PageContents()[0];
        }

        [Fact]
        public void Translate_FlipsYForPdf()
        {
            Assert.Contains("1 0 0 1 10 -20 cm", Draw(g => { using (g.Save()) { g.Translate(10, 20); } }));
        }

        [Fact]
        public void RotateAt_PositiveIsClockwiseOnScreen()
        {
            // Around the bottom-left corner (PDF origin): 90° clockwise = -90° in PDF space.
            Assert.Contains("0 -1 1 0 0 0 cm", Draw(g => { using (g.Save()) { g.RotateAt(90, 0, 100); } }));
        }

        [Fact]
        public void ScaleAt_KeepsPivotFixed()
        {
            // Pivot (10, 10) top-left = (10, 90) in PDF space.
            Assert.Contains("2 0 0 2 -10 -90 cm", Draw(g => { using (g.Save()) { g.ScaleAt(2, 2, 10, 10); } }));
        }

        [Fact]
        public void Opacity_UsesSharedExtGState()
        {
            var doc = new PdfDocument();
            for (var i = 0; i < 3; i++)
            {
                var g = doc.AddPage().Graphics;
                using (g.Save())
                {
                    g.SetOpacity(0.5);
                    g.FillRectangle(PdfColor.Blue, 0, 0, 10, 10);
                }
            }

            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Equal(1, pdf.CountObjects("/Type /ExtGState"));
            Assert.Contains("<< /Type /ExtGState /CA 0.5 /ca 0.5 >>", pdf.Text);
            Assert.Contains("/ExtGState << /GS1 ", pdf.Text);
            Assert.Contains("/GS1 gs", pdf.PageContents()[2]);
        }

        [Fact]
        public void Clip_WritesClippingPath()
        {
            Assert.Contains("0 50 20 50 re W n", Draw(g => { using (g.Save()) { g.ClipRectangle(0, 0, 20, 50); } }));
        }

        [Fact]
        public void UnbalancedState_IsReportedAtSave()
        {
            var doc = new PdfDocument();
            doc.AddPage().Graphics.Save();

            var ex = Assert.Throws<InvalidOperationException>(() => doc.ToArray());
            Assert.Contains("Restore()", ex.Message);
        }

        [Fact]
        public void RestoreWithoutSave_AndDeepNesting_Throw()
        {
            var g = new PdfDocument().AddPage().Graphics;
            Assert.Throws<InvalidOperationException>(() => g.Restore());

            for (var i = 0; i < 27; i++)
            {
                g.Save();
            }

            Assert.Throws<InvalidOperationException>(() => g.Save());
        }

        [Fact]
        public void DisposingOuterState_RestoresForgottenInnerStates()
        {
            var doc = new PdfDocument();
            var g = doc.AddPage().Graphics;
            using (g.Save())
            {
                g.Save(); // forgotten
                g.Save(); // forgotten
            }

            PdfStructure.Verify(doc.ToArray()); // balanced, no exception
        }

        [Fact]
        public void InvalidTransformArguments_Throw()
        {
            var g = new PdfDocument().AddPage().Graphics;
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SetOpacity(1.5));
            Assert.Throws<ArgumentException>(() => g.SetOpacity(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.ScaleAt(0, 1, 0, 0));
            Assert.Throws<ArgumentException>(() => g.RotateAt(double.PositiveInfinity, 0, 0));
        }

        [Fact]
        public void RotatedWatermark_ProducesValidPdf()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var g = page.Graphics;
            using (g.Save())
            {
                g.RotateAt(-30, page.Width / 2, page.Height / 2);
                g.SetOpacity(0.15);
                g.DrawText("DRAFT", new PdfFont(StandardFont.HelveticaBold, 96), page.Width / 2, page.Height / 2, align: TextAlign.Center);
            }

            PdfStructure.Verify(doc.ToArray());
        }

        [Fact]
        public void ImageToPdf_PageFollowsImageSizeAndDpi()
        {
            // rgb.jpg is 64×32 px without DPI → 96 dpi default → 48×24 pt.
            var doc = ImageToPdf.Convert(new[] { Asset("rgb.jpg") });
            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Contains("/MediaBox [0 0 48 24]", pdf.Text);
            Assert.Contains("48 0 0 24 0 0 cm", pdf.PageContents()[0]);
        }

        [Fact]
        public void ImageToPdf_FixedPageSize_RotatesForLandscapeImages()
        {
            var doc = ImageToPdf.Convert(
                new[] { Asset("rgb.jpg"), Asset(Path.Combine("png", "one-pixel.png")) },
                new ImageToPdfOptions { PageSize = PageSize.A4, Margin = 36 });
            var pdf = PdfStructure.Verify(doc.ToArray());

            Assert.Equal(2, doc.Pages.Count);
            Assert.Contains("/MediaBox [0 0 841.8898 595.2756]", pdf.Text);   // wide JPEG → landscape
            Assert.Contains("/MediaBox [0 0 595.2756 841.8898]", pdf.Text);   // square PNG → portrait
        }

        [Fact]
        public void ImageToPdf_CoverClipsToMarginBox()
        {
            var doc = ImageToPdf.Convert(new[] { PdfImage.FromFile(Asset("rgb.jpg")) },
                new ImageToPdfOptions { PageSize = PageSize.A4, AutoRotatePage = false, Fit = ImageFit.Cover, Margin = 20 });

            Assert.Contains(" re W n", PdfStructure.Verify(doc.ToArray()).PageContents()[0]);
        }

        [Fact]
        public void ImageToPdf_InvalidInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => ImageToPdf.Convert(new string[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ImageToPdf.Convert(new[] { Asset("rgb.jpg") }, new ImageToPdfOptions { PageSize = PageSize.A5, Margin = 400 }));
        }
    }
}
