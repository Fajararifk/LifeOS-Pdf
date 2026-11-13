namespace LifeOS.Pdf.Rendering
{
    /// <summary>A rendered page as a PNG image.</summary>
    public sealed class RenderedImage
    {
        public RenderedImage(int pageIndex, int width, int height, byte[] png)
        {
            PageIndex = pageIndex;
            Width = width;
            Height = height;
            Png = png;
        }

        public int PageIndex { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>The page as PNG bytes.</summary>
        public byte[] Png { get; }
    }
}
