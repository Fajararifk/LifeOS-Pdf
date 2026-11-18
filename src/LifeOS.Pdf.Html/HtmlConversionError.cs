namespace LifeOS.Pdf.Html
{
    /// <summary>Why an HTML conversion failed.</summary>
    public enum HtmlConversionError
    {
        /// <summary>No Chrome, Edge or Chromium was found.</summary>
        BrowserNotFound,

        /// <summary>The browser was found but did not start.</summary>
        LaunchFailed,

        /// <summary>The page did not finish within <see cref="HtmlToPdfOptions.Timeout"/>.</summary>
        Timeout,

        /// <summary>The file or URL could not be loaded.</summary>
        NavigationFailed,

        /// <summary>The HTML is longer than <see cref="HtmlToPdfOptions.MaxHtmlLength"/>.</summary>
        InputTooLarge,

        /// <summary>The PDF is larger than <see cref="HtmlToPdfOptions.MaxOutputBytes"/>.</summary>
        OutputTooLarge,

        /// <summary>The browser process stopped during the conversion.</summary>
        BrowserCrashed,
    }
}
