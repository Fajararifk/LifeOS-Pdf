using LifeOS.Pdf.Forms.Internal;

namespace LifeOS.Pdf.Forms.Internal
{
    /// <summary>A widget appearance baked into page content by <see cref="PdfForm.Flatten"/>.</summary>
    internal sealed class FlattenItem
    {
        public FlattenItem(PdfPage page, double x, double y, double width, double height, FieldAppearance appearance)
        {
            Page = page;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Appearance = appearance;
        }

        public PdfPage Page { get; }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        public FieldAppearance Appearance { get; }
    }
}
