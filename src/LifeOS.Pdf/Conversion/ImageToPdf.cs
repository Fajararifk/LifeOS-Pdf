using System;
using System.Collections.Generic;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Conversion
{
    /// <summary>
    /// Turns images (JPEG, PNG) into a PDF, one image per page.
    /// <code>
    /// var doc = ImageToPdf.Convert(new[] { "scan1.jpg", "scan2.png" }, new ImageToPdfOptions { PageSize = PageSize.A4, Margin = 36 });
    /// doc.Save("scan.pdf");
    /// </code>
    /// The result is an ordinary <see cref="PdfDocument"/>: pages and text can still be added.
    /// </summary>
    public static class ImageToPdf
    {
        private const double MaxPageSide = 14400; // ISO 32000-1 Annex C

        public static PdfDocument Convert(IEnumerable<string> imagePaths, ImageToPdfOptions? options = null)
        {
            if (imagePaths == null)
            {
                throw new ArgumentNullException(nameof(imagePaths));
            }

            var images = new List<PdfImage>();
            foreach (var path in imagePaths)
            {
                images.Add(PdfImage.FromFile(path));
            }

            return Convert(images, options);
        }

        public static PdfDocument Convert(IEnumerable<PdfImage> images, ImageToPdfOptions? options = null)
        {
            if (images == null)
            {
                throw new ArgumentNullException(nameof(images));
            }

            var opts = options ?? new ImageToPdfOptions();
            PdfFormat.RequireNonNegative(opts.Margin, nameof(opts.Margin));
            PdfFormat.RequireFinite(opts.DefaultDpi, nameof(opts.DefaultDpi));
            if (opts.DefaultDpi <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), opts.DefaultDpi, "DefaultDpi must be greater than zero.");
            }

            var doc = new PdfDocument();
            foreach (var image in images)
            {
                if (image == null)
                {
                    throw new ArgumentException("The image list contains null.", nameof(images));
                }

                AddPage(doc, image, opts);
            }

            if (doc.Pages.Count == 0)
            {
                throw new ArgumentException("No images were given.", nameof(images));
            }

            return doc;
        }

        private static void AddPage(PdfDocument doc, PdfImage image, ImageToPdfOptions opts)
        {
            // Natural size in points from the stored resolution.
            var imageWidth = image.PixelWidth * 72.0 / (image.DpiX ?? opts.DefaultDpi);
            var imageHeight = image.PixelHeight * 72.0 / (image.DpiY ?? opts.DefaultDpi);
            var margin = opts.Margin;

            PageSize size;
            if (opts.PageSize.HasValue)
            {
                size = opts.PageSize.Value;
                if (opts.AutoRotatePage)
                {
                    size = imageWidth > imageHeight ? size.Landscape : size.Portrait;
                }
            }
            else
            {
                // Shrink oversized images so the page stays within PDF's limits.
                var shrink = Math.Min(1, Math.Min((MaxPageSide - 2 * margin) / imageWidth, (MaxPageSide - 2 * margin) / imageHeight));
                if (shrink <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(opts.Margin), margin, "Margin is too large for a PDF page.");
                }

                imageWidth *= shrink;
                imageHeight *= shrink;
                size = new PageSize(Math.Max(3, imageWidth + 2 * margin), Math.Max(3, imageHeight + 2 * margin));
            }

            var boxWidth = size.Width - 2 * margin;
            var boxHeight = size.Height - 2 * margin;
            if (boxWidth <= 0 || boxHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(opts.Margin), margin, "Margin leaves no room for the image on a " + size + " page.");
            }

            var page = doc.AddPage(size);
            var g = page.Graphics;
            var scale = opts.Fit == ImageFit.Cover
                ? Math.Max(boxWidth / imageWidth, boxHeight / imageHeight)
                : Math.Min(boxWidth / imageWidth, boxHeight / imageHeight);
            var w = imageWidth * scale;
            var h = imageHeight * scale;
            var x = margin + (boxWidth - w) / 2;
            var y = margin + (boxHeight - h) / 2;

            if (opts.Fit == ImageFit.Cover)
            {
                using (g.Save())
                {
                    g.ClipRectangle(margin, margin, boxWidth, boxHeight);
                    g.DrawImage(image, x, y, w, h);
                }
            }
            else
            {
                g.DrawImage(image, x, y, w, h);
            }
        }
    }
}
