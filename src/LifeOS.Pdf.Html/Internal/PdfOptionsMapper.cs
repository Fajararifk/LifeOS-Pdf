using System.Globalization;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace LifeOS.Pdf.Html.Internal
{
    /// <summary>
    /// Maps <see cref="HtmlToPdfOptions"/> (points, top-left, LifeOS conventions) to Chromium's
    /// <c>Page.printToPDF</c> parameters (inches). Every number goes through the invariant culture:
    /// on an id-ID machine "8,27in" would be read by Chromium as an invalid size (CLAUDE.md #3).
    /// </summary>
    internal static class PdfOptionsMapper
    {
        internal static (double Width, double Height) PaperSize(HtmlToPdfOptions o)
        {
            // Swap the sides ourselves instead of sending Chromium's `landscape` flag: with an explicit
            // width/height that flag is applied inconsistently across Chromium versions.
            var size = o.Landscape ? o.PageSize.Landscape : o.PageSize.Portrait;
            return (size.Width, size.Height);
        }

        internal static PdfOptions Map(HtmlToPdfOptions o)
        {
            var (width, height) = PaperSize(o);
            var hasHeaderFooter = o.HeaderHtml != null || o.FooterHtml != null;

            var pdf = new PdfOptions
            {
                Width = Inches(width),
                Height = Inches(height),
                Landscape = false,
                PreferCSSPageSize = o.PreferCssPageSize,
                PrintBackground = o.PrintBackground,
                Scale = (decimal)o.Scale,
                MarginOptions = new MarginOptions
                {
                    Top = Inches(o.Margins.Top),
                    Right = Inches(o.Margins.Right),
                    Bottom = Inches(o.Margins.Bottom),
                    Left = Inches(o.Margins.Left),
                },
                DisplayHeaderFooter = hasHeaderFooter,
                // Tagged/outlined output adds a structure tree that is not carried through post-processing
                // yet (tagged PDF is Phase 14), so it is switched off for a predictable result.
                Tagged = false,
                Outline = false,
            };

            if (hasHeaderFooter)
            {
                pdf.HeaderTemplate = HeaderFooterTemplate.Build(o.HeaderHtml, o.Margins.Left, o.Margins.Right);
                pdf.FooterTemplate = HeaderFooterTemplate.Build(o.FooterHtml, o.Margins.Left, o.Margins.Right);
            }

            var ranges = PageRangesText(o.PageRanges);
            if (ranges != null)
            {
                pdf.PageRanges = ranges;
            }

            return pdf;
        }

        /// <summary>PageRange text as Chromium expects it, or null for "all pages".</summary>
        internal static string? PageRangesText(PageRange? range)
        {
            if (range is null)
            {
                return null;
            }

            var text = range.Text.Replace(" ", string.Empty);
            return text == "1-" ? null : text;
        }

        internal static string Inches(double points) =>
            (points / 72.0).ToString("0.####", CultureInfo.InvariantCulture) + "in";
    }
}
