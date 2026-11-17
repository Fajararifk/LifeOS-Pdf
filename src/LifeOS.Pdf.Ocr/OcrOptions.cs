using Tesseract;

namespace LifeOS.Pdf.Ocr
{
    /// <summary>Options for <see cref="OcrEngine"/>.</summary>
    public sealed class OcrOptions
    {
        /// <summary>
        /// Resolution of the input image, or the DPI used to render a PDF page. Word positions are
        /// returned in points (pixel × 72 / DPI). Default 72 (one pixel = one point).
        /// </summary>
        public double Dpi { get; set; } = 72;

        /// <summary>Tesseract page segmentation mode.</summary>
        public PageSegMode PageSegmentation { get; set; } = PageSegMode.Auto;
    }
}
