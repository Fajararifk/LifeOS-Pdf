using System;

namespace LifeOS.Pdf.Html.Internal
{
    /// <summary>Checks options before the browser is involved, so mistakes fail fast with a clear message.</summary>
    internal static class OptionsValidator
    {
        internal static void Validate(HtmlToPdfOptions o)
        {
            if (o is null)
            {
                throw new ArgumentNullException(nameof(o));
            }

            if (double.IsNaN(o.Scale) || o.Scale < 0.1 || o.Scale > 2.0)
            {
                throw new ArgumentOutOfRangeException(nameof(o.Scale), o.Scale, "Scale must be between 0.1 and 2.0 (1.0 = 100%).");
            }

            if (o.Timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(o.Timeout), o.Timeout, "Timeout must be longer than zero, e.g. TimeSpan.FromSeconds(30).");
            }

            if (o.ExtraDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(o.ExtraDelay), o.ExtraDelay, "ExtraDelay cannot be negative.");
            }

            if (o.MaxHtmlLength <= 0 || o.MaxOutputBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(o.MaxHtmlLength), "MaxHtmlLength and MaxOutputBytes must be greater than zero.");
            }

            if (o.Margins is null)
            {
                throw new ArgumentNullException(nameof(o.Margins), "Margins cannot be null; use HtmlMargins.None for no margins.");
            }

            var (width, height) = PdfOptionsMapper.PaperSize(o);
            if (o.Margins.Left + o.Margins.Right >= width || o.Margins.Top + o.Margins.Bottom >= height)
            {
                throw new ArgumentException(
                    $"The margins leave no room on a {Points(width)} × {Points(height)} pt page. Make the margins smaller or the paper larger.",
                    nameof(o.Margins));
            }
        }

        internal static void Validate(ChromiumOptions o)
        {
            if (o.MaxConcurrency < 1 || o.MaxConcurrency > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(o.MaxConcurrency), o.MaxConcurrency, "MaxConcurrency must be between 1 and 64.");
            }

            if (o.LaunchTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(o.LaunchTimeout), o.LaunchTimeout, "LaunchTimeout must be longer than zero.");
            }
        }

        private static string Points(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
