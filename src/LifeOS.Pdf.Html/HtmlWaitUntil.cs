namespace LifeOS.Pdf.Html
{
    /// <summary>When the page counts as ready to print.</summary>
    public enum HtmlWaitUntil
    {
        /// <summary>The <c>load</c> event: the document and its images, CSS and fonts have loaded.</summary>
        Load,

        /// <summary>The <c>DOMContentLoaded</c> event: HTML parsed, images may still be loading.</summary>
        DomContentLoaded,

        /// <summary>No network request for 500 ms (useful for pages that fetch data with JavaScript).</summary>
        NetworkIdle,
    }
}
