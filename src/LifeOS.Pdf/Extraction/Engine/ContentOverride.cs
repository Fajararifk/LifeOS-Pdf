using System.Collections.Generic;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>Replacement content of an imported page (after redaction).</summary>
    internal sealed class ContentOverride
    {
        public ContentOverride(byte[] content)
        {
            Content = content;
        }

        /// <summary>The new, decoded content stream.</summary>
        public byte[] Content { get; }

        /// <summary>XObject resources replaced by redacted copies, or removed (null).</summary>
        public Dictionary<string, PdfStream?> XObjects { get; } = new Dictionary<string, PdfStream?>();
    }
}
