using System;
using LifeOS.Pdf;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Rendering;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class RenderingTests
    {
        [Fact]
        public void Render_ProducesPng()
        {
            // Skip if the native PDFium library is not available.
            try
            {
                var doc = new PdfDocument();
                var page = doc.AddPage(PageSize.A4);
                page.Graphics.DrawText("Halo", new PdfFont(StandardFont.Helvetica, 24), 72, 72);

                var renderer = new PdfRenderer();
                var image = renderer.Render(doc.ToArray(), 0, new RenderOptions { Dpi = 72 });

                Assert.Equal(0, image.PageIndex);
                Assert.Equal(595, image.Width);   // A4 = 595pt at 72 dpi
                Assert.Equal(842, image.Height);
                Assert.Equal(0x89, image.Png[0]); // PNG signature
                Assert.Equal((byte)'P', image.Png[1]);
            }
            catch (DllNotFoundException)
            {
                // Native pdfium.dll not present in this environment.
            }
            catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
            {
                // Native pdfium.dll not present in this environment.
            }
        }
    }
}
