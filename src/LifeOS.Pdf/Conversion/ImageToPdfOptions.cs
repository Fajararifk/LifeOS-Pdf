namespace LifeOS.Pdf.Conversion
{
    /// <summary>Options for <see cref="ImageToPdf"/>.</summary>
    public sealed class ImageToPdfOptions
    {
        /// <summary>
        /// Page size for every page. Null (default) makes each page exactly as big as its
        /// image at the image's resolution, plus margins.
        /// </summary>
        public PageSize? PageSize { get; set; }

        /// <summary>Empty border around the image, in points. Default 0.</summary>
        public double Margin { get; set; }

        /// <summary>How the image fills the area inside the margins. Default <see cref="ImageFit.Contain"/>.</summary>
        public ImageFit Fit { get; set; } = ImageFit.Contain;

        /// <summary>Turn the page to landscape for images wider than tall. Default true.</summary>
        public bool AutoRotatePage { get; set; } = true;

        /// <summary>Resolution assumed for images that do not store one. Default 96 (screen images).</summary>
        public double DefaultDpi { get; set; } = 96;
    }
}
