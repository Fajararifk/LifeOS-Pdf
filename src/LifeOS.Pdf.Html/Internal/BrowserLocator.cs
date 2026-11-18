using System;
using System.Collections.Generic;
using System.IO;

namespace LifeOS.Pdf.Html.Internal
{
    /// <summary>Finds an installed Chrome, Edge or Chromium. File and environment access are injectable for tests.</summary>
    internal sealed class BrowserLocator
    {
        internal const string EnvironmentVariable = "LIFEOS_PDF_CHROMIUM";

        private readonly Func<string, bool> _fileExists;
        private readonly Func<string, string?> _getEnvironment;
        private readonly OSPlatformKind _platform;

        internal BrowserLocator()
            : this(File.Exists, Environment.GetEnvironmentVariable, Current())
        {
        }

        internal BrowserLocator(Func<string, bool> fileExists, Func<string, string?> getEnvironment, OSPlatformKind platform)
        {
            _fileExists = fileExists;
            _getEnvironment = getEnvironment;
            _platform = platform;
        }

        internal enum OSPlatformKind { Windows, Linux, MacOS }

        internal string Locate(string? explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                if (_fileExists(explicitPath!))
                {
                    return explicitPath!;
                }

                throw new HtmlConversionException(
                    HtmlConversionError.BrowserNotFound,
                    $"ChromiumOptions.ExecutablePath points to '{explicitPath}', but no file exists there. " +
                    "Fix the path or leave it empty to detect Chrome, Edge or Chromium automatically.");
            }

            var tried = new List<string>();
            foreach (var candidate in Candidates())
            {
                tried.Add(candidate);
                if (_fileExists(candidate))
                {
                    return candidate;
                }
            }

            throw new HtmlConversionException(
                HtmlConversionError.BrowserNotFound,
                "No Chrome, Edge or Chromium was found. Install one of them, or set ChromiumOptions.ExecutablePath " +
                $"or the {EnvironmentVariable} environment variable to the browser executable. Paths tried:" +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", tried));
        }

        internal IEnumerable<string> Candidates()
        {
            var fromEnvironment = _getEnvironment(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                yield return fromEnvironment!;
            }

            switch (_platform)
            {
                case OSPlatformKind.Windows:
                    var roots = new[] { _getEnvironment("ProgramFiles"), _getEnvironment("ProgramFiles(x86)"), _getEnvironment("LOCALAPPDATA") };
                    var relative = new[]
                    {
                        @"Google\Chrome\Application\chrome.exe",
                        @"Microsoft\Edge\Application\msedge.exe",
                        @"Chromium\Application\chrome.exe",
                    };
                    // Browser first, then root: an installed Chrome is preferred over Edge wherever they live.
                    foreach (var path in relative)
                    {
                        foreach (var root in roots)
                        {
                            if (!string.IsNullOrEmpty(root))
                            {
                                yield return Path.Combine(root!, path);
                            }
                        }
                    }

                    break;
                case OSPlatformKind.MacOS:
                    yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
                    yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
                    yield return "/Applications/Chromium.app/Contents/MacOS/Chromium";
                    break;
                default:
                    var names = new[] { "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "microsoft-edge" };
                    var folders = new List<string>();
                    var pathVariable = _getEnvironment("PATH");
                    if (!string.IsNullOrEmpty(pathVariable))
                    {
                        folders.AddRange(pathVariable!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries));
                    }

                    foreach (var standard in new[] { "/usr/bin", "/usr/local/bin", "/snap/bin" })
                    {
                        if (!folders.Contains(standard))
                        {
                            folders.Add(standard);
                        }
                    }

                    foreach (var name in names)
                    {
                        foreach (var folder in folders)
                        {
                            yield return folder.TrimEnd('/') + "/" + name;
                        }
                    }

                    break;
            }
        }

        private static OSPlatformKind Current() =>
            OperatingSystem.IsWindows() ? OSPlatformKind.Windows
            : OperatingSystem.IsMacOS() ? OSPlatformKind.MacOS
            : OSPlatformKind.Linux;
    }
}
