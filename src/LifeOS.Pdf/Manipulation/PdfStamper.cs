using System;
using System.Collections.Generic;
using System.Globalization;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf.Manipulation
{
    /// <summary>
    /// Adds watermarks, page numbers and Bates numbers to every page (or selected pages) of a
    /// document — new or opened. Positions follow the page as it is displayed, so rotated pages
    /// get their stamp at the visual bottom.
    /// <code>
    /// var doc = PdfDocument.Open("contract.pdf");
    /// PdfStamper.AddTextWatermark(doc, "SALINAN");
    /// PdfStamper.AddPageNumbers(doc, "Halaman {page} dari {pages}");
    /// doc.Save("contract-copy.pdf");
    /// </code>
    /// </summary>
    public static class PdfStamper
    {
        /// <summary>Diagonal text across each page, semi-transparent.</summary>
        public static void AddTextWatermark(PdfDocument document, string text, WatermarkOptions? options = null)
        {
            RequireDocument(document);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Watermark text must not be empty.", nameof(text));
            }

            options = options ?? new WatermarkOptions();
            var unit = new PdfFont(options.Font, 1).MeasureWidth(text);
            foreach (var page in Selected(document, options.Pages))
            {
                var g = page.Graphics;
                void Draw()
                {
                    using (g.DisplayFrame())
                    {
                        var w = g.DisplayWidth;
                        var h = g.DisplayHeight;
                        var radians = options.Angle * Math.PI / 180;
                        var cos = Math.Abs(Math.Cos(radians));
                        var sin = Math.Abs(Math.Sin(radians));
                        // Longest line through the centre at this angle that stays on the page.
                        var room = Math.Min(cos > 1e-6 ? w / cos : double.MaxValue, sin > 1e-6 ? h / sin : double.MaxValue);
                        var size = options.FontSize ?? Math.Max(6, Math.Min(room * 0.75 / unit, h * 0.25));
                        var font = new PdfFont(options.Font, size);
                        using (g.Save())
                        {
                            g.SetOpacity(options.Opacity);
                            g.RotateAt(-options.Angle, w / 2, h / 2);
                            g.DrawText(text, font, w / 2, h / 2 - (font.Ascent - font.Descent) / 2, options.Color, TextAlign.Center);
                        }
                    }
                }

                if (options.Behind)
                {
                    g.DrawBehind(Draw);
                }
                else
                {
                    Draw();
                }
            }
        }

        /// <summary>An image (logo, "LUNAS" stamp scan) centred on each page.</summary>
        public static void AddImageWatermark(PdfDocument document, PdfImage image, WatermarkOptions? options = null)
        {
            RequireDocument(document);
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            options = options ?? new WatermarkOptions();
            foreach (var page in Selected(document, options.Pages))
            {
                var g = page.Graphics;
                void Draw()
                {
                    using (g.DisplayFrame())
                    {
                        var w = g.DisplayWidth * options.Scale;
                        var h = w * image.PixelHeight / image.PixelWidth;
                        var cx = g.DisplayWidth / 2;
                        var cy = g.DisplayHeight / 2;
                        using (g.Save())
                        {
                            g.SetOpacity(options.Opacity);
                            g.RotateAt(-options.Angle, cx, cy);
                            g.DrawImage(image, cx - w / 2, cy - h / 2, w, h);
                        }
                    }
                }

                if (options.Behind)
                {
                    g.DrawBehind(Draw);
                }
                else
                {
                    Draw();
                }
            }
        }

        /// <summary>
        /// Page numbers. <paramref name="format"/> may contain <c>{page}</c> and <c>{pages}</c>,
        /// e.g. <c>"Halaman {page} dari {pages}"</c>. Only the selected pages are counted.
        /// </summary>
        public static void AddPageNumbers(PdfDocument document, string format = "{page} / {pages}", StampOptions? options = null)
        {
            RequireDocument(document);
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            options = options ?? new StampOptions();
            var pages = Selected(document, options.Pages);
            var total = (pages.Count + options.StartNumber - 1).ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < pages.Count; i++)
            {
                var number = (i + options.StartNumber).ToString(CultureInfo.InvariantCulture);
                Stamp(pages[i], format.Replace("{page}", number).Replace("{pages}", total), options);
            }
        }

        /// <summary>Bates numbers (legal document numbering): <paramref name="prefix"/> + a zero-padded running number.</summary>
        public static void AddBatesNumbers(PdfDocument document, string prefix, int start = 1, int digits = 6, StampOptions? options = null)
        {
            RequireDocument(document);
            if (digits < 1 || digits > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(digits), digits, "Digits must be between 1 and 12.");
            }

            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, "The first Bates number must not be negative.");
            }

            options = options ?? new StampOptions { Position = StampPosition.BottomRight };
            var pages = Selected(document, options.Pages);
            for (var i = 0; i < pages.Count; i++)
            {
                var number = ((long)start + i).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
                Stamp(pages[i], (prefix ?? string.Empty) + number, options);
            }
        }

        private static void Stamp(PdfPage page, string text, StampOptions options)
        {
            var g = page.Graphics;
            using (g.DisplayFrame())
            {
                var w = g.DisplayWidth;
                var h = g.DisplayHeight;
                var font = options.Font ?? throw new ArgumentException("StampOptions.Font must not be null.");
                var m = options.Margin;
                double x;
                TextAlign align;
                switch (options.Position)
                {
                    case StampPosition.TopLeft:
                    case StampPosition.BottomLeft:
                        x = m;
                        align = TextAlign.Left;
                        break;
                    case StampPosition.TopRight:
                    case StampPosition.BottomRight:
                        x = w - m;
                        align = TextAlign.Right;
                        break;
                    default:
                        x = w / 2;
                        align = TextAlign.Center;
                        break;
                }

                var top = options.Position <= StampPosition.TopRight;
                var y = top ? m : h - m - font.LineHeight;
                g.DrawText(text, font, x, y, options.Color, align);
            }
        }

        internal static List<PdfPage> Selected(PdfDocument document, PageRange? range)
        {
            var result = new List<PdfPage>();
            foreach (var i in (range ?? PageRange.All).Resolve(document.Pages.Count))
            {
                result.Add(document.Pages[i]);
            }

            return result;
        }

        private static void RequireDocument(PdfDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }
        }
    }
}
