using System;
using System.Threading;
using System.Threading.Tasks;

namespace LifeOS.Pdf.Html
{
    /// <summary>
    /// Converts HTML/CSS to PDF. One interface, several backends (Chromium now, wkhtmltopdf in
    /// Phase 11b), so callers can switch engines without changing their code.
    /// </summary>
    public interface IHtmlToPdfConverter : IDisposable, IAsyncDisposable
    {
        /// <summary>Converts an HTML string. Relative links resolve against <see cref="HtmlToPdfOptions.BaseUrl"/>.</summary>
        Task<byte[]> ConvertHtmlAsync(string html, HtmlToPdfOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>Converts a local .html file. Files next to it (CSS, images) may be read; nothing else.</summary>
        Task<byte[]> ConvertFileAsync(string path, HtmlToPdfOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>Converts a web page. Needs <see cref="HtmlToPdfOptions.AllowNetwork"/> = true.</summary>
        Task<byte[]> ConvertUrlAsync(Uri url, HtmlToPdfOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>Blocking version of <see cref="ConvertHtmlAsync"/>. Prefer the async one in ASP.NET.</summary>
        byte[] ConvertHtml(string html, HtmlToPdfOptions? options = null);

        /// <summary>Blocking version of <see cref="ConvertFileAsync"/>.</summary>
        byte[] ConvertFile(string path, HtmlToPdfOptions? options = null);

        /// <summary>Blocking version of <see cref="ConvertUrlAsync"/>.</summary>
        byte[] ConvertUrl(Uri url, HtmlToPdfOptions? options = null);
    }
}
