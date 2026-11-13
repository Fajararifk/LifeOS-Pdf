using System.Collections.Generic;

namespace LifeOS.Pdf.Extraction
{
    /// <summary>A match of <see cref="PdfDocument.Search"/>.</summary>
    public sealed class SearchResult
    {
        internal SearchResult(int pageIndex, string text, IReadOnlyList<PdfRect> rects)
        {
            PageIndex = pageIndex;
            Text = text;
            Rects = rects;
            var bounds = rects[0];
            foreach (var r in rects)
            {
                bounds = bounds.Union(r);
            }

            Bounds = bounds;
        }

        /// <summary>Zero-based page index.</summary>
        public int PageIndex { get; }

        /// <summary>The matched text as it appears on the page.</summary>
        public string Text { get; }

        /// <summary>One rectangle per line the match spans.</summary>
        public IReadOnlyList<PdfRect> Rects { get; }

        /// <summary>Union of <see cref="Rects"/>.</summary>
        public PdfRect Bounds { get; }

        public override string ToString() => "page " + (PageIndex + 1) + ": " + Text;
    }
}
