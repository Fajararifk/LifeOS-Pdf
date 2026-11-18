using System;

namespace LifeOS.Pdf.Html
{
    /// <summary>Options for one HTML → PDF conversion. All sizes are in points (1/72 inch).</summary>
    public sealed class HtmlToPdfOptions
    {
        // ---- Paper & layout ----

        /// <summary>Paper size. Ignored when <see cref="PreferCssPageSize"/> is true and the page sets <c>@page { size }</c>.</summary>
        public PageSize PageSize { get; set; } = PageSize.A4;

        /// <summary>Turns the paper sideways.</summary>
        public bool Landscape { get; set; }

        /// <summary>Lets CSS <c>@page { size: … }</c> win over <see cref="PageSize"/>.</summary>
        public bool PreferCssPageSize { get; set; }

        /// <summary>Page margins; header and footer are drawn inside the top and bottom margins.</summary>
        public HtmlMargins Margins { get; set; } = HtmlMargins.Uniform(36);

        /// <summary>Zoom of the page content, 0.1–2.0.</summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>Prints background colors and images (browsers skip them by default).</summary>
        public bool PrintBackground { get; set; } = true;

        /// <summary>Only print these pages (1-based), e.g. <c>PageRange.Parse("1-3,5")</c>. Null = all.</summary>
        public PageRange? PageRanges { get; set; }

        // ---- Header & footer ----

        /// <summary>
        /// HTML drawn at the top of every page. Placeholders: <c>{page}</c>, <c>{pages}</c>, <c>{date}</c>,
        /// <c>{title}</c>, <c>{url}</c>. Styles must be inline; the page's CSS does not apply here.
        /// </summary>
        public string? HeaderHtml { get; set; }

        /// <summary>HTML drawn at the bottom of every page. Same placeholders as <see cref="HeaderHtml"/>.</summary>
        public string? FooterHtml { get; set; }

        // ---- Loading ----

        /// <summary>
        /// Address that relative links in an HTML string resolve against, e.g. a folder
        /// (<c>new Uri("file:///C:/reports/")</c>) or a site. A file URL also needs <see cref="AllowLocalFiles"/>.
        /// </summary>
        public Uri? BaseUrl { get; set; }

        /// <summary>When the page counts as ready.</summary>
        public HtmlWaitUntil WaitUntil { get; set; } = HtmlWaitUntil.Load;

        /// <summary>Also wait until this CSS selector exists (e.g. <c>#chart-ready</c>).</summary>
        public string? WaitForSelector { get; set; }

        /// <summary>Extra wait after the page is ready, for animations or late scripts.</summary>
        public TimeSpan ExtraDelay { get; set; } = TimeSpan.Zero;

        /// <summary>Upper limit for the whole conversion; a stuck page is closed (and the browser restarted if needed).</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Uses <c>screen</c> CSS instead of <c>print</c> CSS.</summary>
        public bool EmulateScreenMedia { get; set; }

        // ---- Security ----

        /// <summary>Runs the page's JavaScript. Set to false for HTML you do not trust.</summary>
        public bool AllowJavaScript { get; set; } = true;

        /// <summary>Allows http/https/WebSocket requests. Off by default so untrusted HTML cannot reach your network.</summary>
        public bool AllowNetwork { get; set; }

        /// <summary>Allows <c>file://</c> requests, but only inside the folder of <see cref="BaseUrl"/> (or of the converted file).</summary>
        public bool AllowLocalFiles { get; set; }

        /// <summary>Longest HTML string accepted, in characters.</summary>
        public int MaxHtmlLength { get; set; } = 20 * 1024 * 1024;

        /// <summary>Largest PDF accepted from the browser, in bytes.</summary>
        public long MaxOutputBytes { get; set; } = 256L * 1024 * 1024;

        // ---- Post-processing ----

        /// <summary>
        /// Re-saves the browser's PDF with the LifeOS.Pdf writer: metadata below is applied and the file ID is
        /// derived from the content, so the same input and <see cref="CreationDate"/> give the same bytes.
        /// Set to false to get the browser's bytes unchanged (slightly faster).
        /// </summary>
        public bool PostProcess { get; set; } = true;

        /// <summary>Document title. Null = the page's <c>&lt;title&gt;</c>.</summary>
        public string? Title { get; set; }

        public string? Author { get; set; }

        public string? Subject { get; set; }

        /// <summary>Creation date written to the file. Null = the time of conversion.</summary>
        public DateTimeOffset? CreationDate { get; set; }
    }
}
