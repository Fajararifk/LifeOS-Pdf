using System;

namespace LifeOS.Pdf
{
    /// <summary>Document properties shown in the viewer's "Document Properties" dialog.</summary>
    public sealed class PdfDocumentInfo
    {
        public string? Title { get; set; }

        public string? Author { get; set; }

        public string? Subject { get; set; }

        public string? Keywords { get; set; }

        /// <summary>The application that created the content.</summary>
        public string? Creator { get; set; }

        /// <summary>The library that wrote the file. Defaults to "LifeOS.Pdf x.y.z".</summary>
        public string? Producer { get; set; } = "LifeOS.Pdf " + LibraryVersion;

        /// <summary>When the document was created. If null, the time of saving is used.</summary>
        public DateTimeOffset? CreationDate { get; set; }

        public DateTimeOffset? ModificationDate { get; set; }

        internal static string LibraryVersion
        {
            get
            {
                var v = typeof(PdfDocumentInfo).Assembly.GetName().Version;
                return v == null ? "0.0.0" : v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }
}
