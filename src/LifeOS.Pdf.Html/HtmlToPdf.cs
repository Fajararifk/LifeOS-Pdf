using System;
using System.Threading;
using System.Threading.Tasks;

namespace LifeOS.Pdf.Html
{
    /// <summary>
    /// One-line HTML → PDF with a shared <see cref="ChromiumConverter"/> (default options). The browser starts
    /// on first use and closes when the process exits. For custom browser settings create your own converter.
    /// </summary>
    public static class HtmlToPdf
    {
        private static readonly Lazy<ChromiumConverter> Shared = new Lazy<ChromiumConverter>(Create, LazyThreadSafetyMode.ExecutionAndPublication);

        public static byte[] Convert(string html, HtmlToPdfOptions? options = null) =>
            Shared.Value.ConvertHtml(html, options);

        public static Task<byte[]> ConvertAsync(string html, HtmlToPdfOptions? options = null, CancellationToken cancellationToken = default) =>
            Shared.Value.ConvertHtmlAsync(html, options, cancellationToken);

        public static byte[] ConvertFile(string path, HtmlToPdfOptions? options = null) =>
            Shared.Value.ConvertFile(path, options);

        public static byte[] ConvertUrl(Uri url, HtmlToPdfOptions? options = null) =>
            Shared.Value.ConvertUrl(url, options);

        /// <summary>Converts and opens the result, ready for merging, stamping, encrypting or signing.</summary>
        public static PdfDocument ToDocument(string html, HtmlToPdfOptions? options = null) =>
            PdfDocument.Open(Convert(html, options));

        private static ChromiumConverter Create()
        {
            var converter = new ChromiumConverter();
            // Without this the headless browser would outlive the application.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => converter.Dispose();
            return converter;
        }
    }
}
