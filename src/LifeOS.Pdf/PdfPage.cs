using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf
{
    /// <summary>One page of a <see cref="PdfDocument"/>. Create it with <see cref="PdfDocument.AddPage()"/>.</summary>
    public sealed class PdfPage
    {
        private readonly PdfDocument _document;

        internal PdfPage(PdfDocument document, PageSize size)
        {
            _document = document;
            Size = size;
            Graphics = new PdfGraphics(this);
        }

        public PageSize Size { get; }

        /// <summary>Page width in points.</summary>
        public double Width => Size.Width;

        /// <summary>Page height in points.</summary>
        public double Height => Size.Height;

        /// <summary>Draw on this page (origin top-left, units in points).</summary>
        public PdfGraphics Graphics { get; }

        /// <summary>Resource name → font, only the fonts this page uses.</summary>
        internal SortedDictionary<string, StandardFont> Fonts { get; } = new SortedDictionary<string, StandardFont>(System.StringComparer.Ordinal);

        /// <summary>Resource name → image, only the images this page uses.</summary>
        internal SortedDictionary<string, PdfImage> Images { get; } = new SortedDictionary<string, PdfImage>(System.StringComparer.Ordinal);

        internal string UseFont(StandardFont font)
        {
            var name = _document.Resources.FontName(font);
            Fonts[name] = font;
            return name;
        }

        internal string UseImage(PdfImage image)
        {
            var name = _document.Resources.ImageName(image);
            Images[name] = image;
            return name;
        }
    }
}
