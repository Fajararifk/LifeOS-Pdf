using System;
using System.Collections.Generic;

namespace LifeOS.Pdf.Html
{
    /// <summary>How <see cref="ChromiumConverter"/> finds and runs the browser.</summary>
    public sealed class ChromiumOptions
    {
        /// <summary>
        /// Full path to chrome/msedge/chromium. When null the converter looks at the environment variable
        /// <c>LIFEOS_PDF_CHROMIUM</c>, then the usual install folders of Chrome, Edge and Chromium.
        /// </summary>
        public string? ExecutablePath { get; set; }

        /// <summary>How many conversions may run at the same time (one browser tab each), 1–64.</summary>
        public int MaxConcurrency { get; set; } = 4;

        /// <summary>How long starting the browser may take.</summary>
        public TimeSpan LaunchTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Extra command-line switches, e.g. <c>--no-sandbox</c> when running as root inside a container.
        /// </summary>
        public IList<string> ExtraArguments { get; } = new List<string>();
    }
}
