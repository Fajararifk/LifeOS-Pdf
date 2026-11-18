using System.Globalization;

namespace LifeOS.Pdf.Html.Internal
{
    /// <summary>
    /// Turns LifeOS-style placeholders into the classes Chromium fills in when it prints a header or footer.
    /// The same placeholder names as <c>PdfStamper.AddPageNumbers</c> keep the two APIs consistent.
    /// </summary>
    internal static class HeaderFooterTemplate
    {
        internal static string Build(string? html, double leftMargin, double rightMargin)
        {
            // An empty template must still be sent: if one side is missing, Chromium prints its own default
            // (date + title + URL), which nobody wants in a report.
            if (string.IsNullOrEmpty(html))
            {
                return "<span></span>";
            }

            var body = html!
                .Replace("{page}", "<span class=\"pageNumber\"></span>")
                .Replace("{pages}", "<span class=\"totalPages\"></span>")
                .Replace("{date}", "<span class=\"date\"></span>")
                .Replace("{title}", "<span class=\"title\"></span>")
                .Replace("{url}", "<span class=\"url\"></span>");

            // The template is a separate document with a tiny default font and no page margins, so give it a
            // readable size and line it up with the page content. A font-size in the user's HTML wins.
            return "<div style=\"width:100%;font-size:9pt;font-family:sans-serif;box-sizing:border-box;"
                + "padding:0 " + Pt(rightMargin) + " 0 " + Pt(leftMargin) + ";\">" + body + "</div>";
        }

        private static string Pt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "pt";
    }
}
