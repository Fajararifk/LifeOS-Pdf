using System;
using System.IO;

namespace LifeOS.Pdf.Html.Internal
{
    /// <summary>
    /// Decides which requests a page may make. Default: only inline content (data:, blob:, about:).
    /// This keeps untrusted HTML from reading local files or calling into the network (SSRF).
    /// </summary>
    internal static class RequestPolicy
    {
        /// <param name="url">The requested URL.</param>
        /// <param name="options">Conversion options (AllowNetwork, AllowLocalFiles).</param>
        /// <param name="localRoot">Folder file:// requests are confined to, or null when none is allowed.</param>
        internal static bool IsAllowed(string url, HtmlToPdfOptions options, string? localRoot)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            switch (uri.Scheme.ToLowerInvariant())
            {
                case "data":
                case "blob":
                case "about":
                    return true;
                case "http":
                case "https":
                case "ws":
                case "wss":
                    return options.AllowNetwork;
                case "file":
                    return localRoot != null && IsInside(uri, localRoot);
                default:
                    // chrome:, chrome-extension:, ftp:, javascript: … are never needed to print a document.
                    return false;
            }
        }

        /// <summary>The folder file:// requests may come from, as a full path ending in a separator.</summary>
        internal static string? LocalRoot(HtmlToPdfOptions options, string? convertedFile)
        {
            if (convertedFile != null)
            {
                // Converting a file implies reading the files next to it (its CSS and images).
                return WithSeparator(Path.GetDirectoryName(Path.GetFullPath(convertedFile))!);
            }

            if (options.AllowLocalFiles && options.BaseUrl != null && options.BaseUrl.IsFile)
            {
                var path = options.BaseUrl.LocalPath;
                // A base URL like file:///C:/reports/ is a folder; file:///C:/reports/index.html means its folder.
                var folder = path.EndsWith("/", StringComparison.Ordinal) || path.EndsWith("\\", StringComparison.Ordinal) || Directory.Exists(path)
                    ? path
                    : Path.GetDirectoryName(path)!;
                return WithSeparator(Path.GetFullPath(folder));
            }

            return null;
        }

        private static bool IsInside(Uri uri, string root)
        {
            string full;
            try
            {
                // GetFullPath removes "..", so file:///C:/reports/../Windows/win.ini is judged by where it really points.
                full = Path.GetFullPath(Uri.UnescapeDataString(uri.LocalPath));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }

            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return full.StartsWith(root, comparison);
        }

        private static string WithSeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
    }
}
